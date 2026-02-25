using System.Linq;
using System.Numerics;
using Content.Shared._RMC14.Entrenching;
using Content.Shared._RMC14.Line;
using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Xenonids.Construction.FloorResin;
using Content.Shared._RMC14.Xenonids.Construction.Tunnel;
using Content.Shared._RMC14.Xenonids.DeployTraps;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Insight;
using Content.Shared._RMC14.Xenonids.Plasma;
using Content.Shared._RMC14.Xenonids.ResinSurge;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Effects;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.MouseRotator;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using YamlDotNet.Core;

namespace Content.Shared._RMC14.Xenonids.AcidMine;

public sealed class XenoAcidMineSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _sharedMap = default!;
    [Dependency] private readonly XenoPlasmaSystem _xenoPlasma = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    private readonly HashSet<Entity<MobStateComponent>> _hit = new();


    public override void Initialize()
    {
        SubscribeLocalEvent<XenoAcidMineComponent, XenoAcidMineActionEvent>(OnXenoAcidMineAction);
        SubscribeLocalEvent<XenoAcidMineComponent, XenoAcidMineDoAfter>(OnAcidMineDoAfter);
    }

    private void ReduceAcidMineCooldown(Entity<XenoAcidMineComponent> xeno, double? cooldownMult = null)
    {
        foreach (var action in _actions.GetActions(xeno))
        {
            if (TryComp(action, out XenoAcidMineActionComponent? actionComp))
            {
                _actions.SetCooldown(action.AsNullable(),
                    actionComp.SuccessCooldown * (cooldownMult ?? actionComp.FailCooldownMult));
                break;
            }
        }
    }

    private void SetAcidMineCooldown(Entity<XenoAcidMineComponent> xeno, TimeSpan? cooldown = null)
    {
        foreach (var action in _actions.GetActions(xeno))
        {
            if (TryComp(action, out XenoAcidMineActionComponent? actionComp))
            {
                _actions.SetCooldown(action.AsNullable(), cooldown ?? actionComp.SuccessCooldown);
                break;
            }
        }
    }

    private void OnXenoAcidMineAction(Entity<XenoAcidMineComponent> xeno, ref XenoAcidMineActionEvent args)
    {
        if (args.Handled)
            return;

        // Check if target on grid
        if (_transform.GetGrid(args.Target) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
            return;

        if (!_examine.InRangeUnOccluded(xeno.Owner, args.Target, xeno.Comp.Range))
        {
            _popup.PopupClient(Loc.GetString("rmc-xeno-deploy-traps-see-fail"), xeno, xeno);
            return;
        }

        args.Handled = true;

        var target = args.Target.SnapToGrid(EntityManager, _map);

        // Check if user has enough plasma
        if (xeno.Comp.AcidMineDoAfter != null ||
            !_xenoPlasma.TryRemovePlasmaPopup((xeno.Owner, null), args.PlasmaCost))
            return;

        var ev = new XenoAcidMineDoAfter(GetNetCoordinates(target));
        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.AcidMineDoAfterPeriod, ev, xeno)
            { BreakOnMove = true, DuplicateCondition = DuplicateConditions.SameEvent };
        if (_doAfter.TryStartDoAfter(doAfter, out var id))
            xeno.Comp.AcidMineDoAfter = id;
        else
            ReduceAcidMineCooldown(xeno);

        args.Handled = false;
    }

    private void OnAcidMineDoAfter(Entity<XenoAcidMineComponent> xeno, ref XenoAcidMineDoAfter args)
    {
        xeno.Comp.AcidMineDoAfter = null;
        if (args.Cancelled)
            return;

        var coords = GetCoordinates(args.Coordinates);

        if (_transform.GetGrid(coords) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
            return;

        var popupSelf = Loc.GetString("rmc-xeno-deploy-traps-self");
        var popupOthers = Loc.GetString("rmc-xeno-deploy-traps-others", ("xeno", xeno));
        _popup.PopupPredicted(popupSelf, popupOthers, xeno, xeno);

        var explodingTiles = _sharedMap.GetTilesIntersecting(
            gridId,
            grid,
            Box2.CenteredAround(coords.Position,
                new(xeno.Comp.AcidMineRadius * 2,
                    xeno.Comp.AcidMineRadius * 2)));

        //total list of struck entities
        HashSet<EntityUid> hitEntities = new();

        //collect all hit entities
        foreach (var tile in explodingTiles)
        {
            hitEntities.UnionWith(_lookup.GetEntitiesInTile(tile));
        }

        var damageToMobs = new DamageSpecifier(xeno.Comp.DamageToMobs);
        var damageToCades = new DamageSpecifier(xeno.Comp.DamageToStructures);

        //sort out only valid targets
        foreach (var target in hitEntities)
        {
            if (!_xeno.CanAbilityAttackTarget(xeno, target, true, true))
                continue;

            //apply damage
            if (TryComp(target, out BarricadeComponent? barricade))
            {
                var change = _damage.TryChangeDamage(target, damageToCades, origin: xeno, tool: xeno);
            }
            else
            {
                var change = _damage.TryChangeDamage(target, damageToMobs, origin: xeno, tool: xeno);
                if (change?.GetTotal() > FixedPoint2.Zero)
                {
                    var filter = Filter.Pvs(target, entityManager: EntityManager).RemoveWhereAttachedEntity(o => o == xeno.Owner);
                    _colorFlash.RaiseEffect(Color.Red, new List<EntityUid> { target }, filter);
                }
            }
        }

        //do telegraph
        foreach (var tile in explodingTiles)
        {
            if (!_interaction.InRangeUnobstructed(xeno.Owner, _turf.GetTileCenter(tile), xeno.Comp.Range + 0.5f))
                continue;

            SpawnAtPosition(xeno.Comp.TelegraphEffect, _turf.GetTileCenter(tile));
        }

        SetAcidMineCooldown(xeno);
    }
}
