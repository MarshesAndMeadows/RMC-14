using System.Numerics;
using Content.Shared._RMC14.Line;
using Content.Shared._RMC14.Map;
using Content.Shared._RMC14.Xenonids.Construction.FloorResin;
using Content.Shared._RMC14.Xenonids.Construction.Tunnel;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Insight;
using Content.Shared._RMC14.Xenonids.Plasma;
using Content.Shared._RMC14.Xenonids.ResinSurge;
using Content.Shared.Actions;
using Content.Shared.Coordinates;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.MouseRotator;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using YamlDotNet.Core;

namespace Content.Shared._RMC14.Xenonids.AcidMine;
public sealed class XenoAcidMineSystem : EntitySystem
{
    [Dependency] private readonly EntityManager _entityManager = default!;
    [Dependency] private readonly IMapManager _map = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedXenoHiveSystem _hive = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _sharedMap = default!;
    [Dependency] private readonly XenoPlasmaSystem _xenoPlasma = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly RMCMapSystem _rmcMap = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly LineSystem _line = default!;
    [Dependency] private readonly XenoInsightSystem _insight = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoAcidMineComponent, XenoAcidMineActionEvent>(OnXenoAcidMineAction);
        SubscribeLocalEvent<XenoAcidMineComponent, XenoAcidMineDoAfter>(OnDeployTrapsDoAfter);
    }

    private void AcidMine(Entity<XenoAcidMineComponent> xeno, EntityCoordinates target, bool empowered)
    {
        if (!target.IsValid(EntityManager))
            return;

        if (_net.IsServer)
        {
            //explosion behavior here

        }
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

        var insight = _insight.GetInsight(xeno.Owner);

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
        if (xeno.Comp.DeployTrapsDoAfter != null ||
            !_xenoPlasma.TryRemovePlasmaPopup((xeno.Owner, null), args.PlasmaCost))
            return;

        // Check if user is empowered by Insight
        if (insight == 10)
            xeno.Comp.Empowered = true;

        // Deploy Traps
        var ev = new XenoAcidMineDoAfter(GetNetCoordinates(target));
        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.AcidMineDoAfterPeriod, ev, xeno)
            { BreakOnMove = true, DuplicateCondition = DuplicateConditions.SameEvent };
        if (_doAfter.TryStartDoAfter(doAfter, out var id))
            xeno.Comp.AcidMineDoAfter = id;
        else
            ReduceDeployTrapsCooldown(xeno);

        args.Handled = false;
    }

    private void OnDeployTrapsDoAfter(Entity<XenoAcidMineComponent> xeno, ref XenoAcidMineDoAfter args)
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

        if (_net.IsServer)
        {



            foreach (var turf in explodingTiles)
            {
                //gotta make them entitycoords.
                var turfCoords = _transform.ToCoordinates(turf.Coordinates);
                //finally do tile by tile anchor check and deploy the damn traps.
                if (!_rmcMap.HasAnchoredEntityEnumerator<DeployTrapsBlockerComponent>(turfCoords, out _))
                    DeployTraps(xeno, turfCoords, xeno.Comp.Empowered);
            }
        }

        SetDeployTrapsCooldown(xeno);
    }
}
