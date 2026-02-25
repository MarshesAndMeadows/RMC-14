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

namespace Content.Shared._RMC14.Xenonids.DeployTraps;

public sealed class XenoDeployTrapsSystem : EntitySystem
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
        SubscribeLocalEvent<XenoDeployTrapsComponent, XenoDeployTrapsActionEvent>(OnXenoDeployTrapsAction);
        SubscribeLocalEvent<XenoDeployTrapsComponent, XenoDeployTrapsDoAfter>(OnDeployTrapsDoAfter);
    }

    private void DeployTraps(Entity<XenoDeployTrapsComponent> xeno, EntityCoordinates target, bool empowered)
    {
        if (!target.IsValid(EntityManager))
            return;

        if (_net.IsServer)
        {
            //Deploy strong traps if empowered, otherwise do normal ones.
            if (empowered)
            {
                var traps = SpawnAtPosition(xeno.Comp.DeployEmpoweredTrapsId, target);
                _hive.SetSameHive(xeno.Owner, traps);

                //consume empowered status after.
                _insight.IncrementInsight(xeno.Owner, -10);
                xeno.Comp.Empowered = false;
            }
            else
            {
                var traps = SpawnAtPosition(xeno.Comp.DeployTrapsId, target);
                _hive.SetSameHive(xeno.Owner, traps);
            }
        }
    }

    private void ReduceDeployTrapsCooldown(Entity<XenoDeployTrapsComponent> xeno, double? cooldownMult = null)
    {
        foreach (var action in _actions.GetActions(xeno))
        {
            if (TryComp(action, out XenoDeployTrapsActionComponent? actionComp))
            {
                _actions.SetCooldown(action.AsNullable(),
                    actionComp.SuccessCooldown * (cooldownMult ?? actionComp.FailCooldownMult));
                break;
            }
        }
    }

    private void SetDeployTrapsCooldown(Entity<XenoDeployTrapsComponent> xeno, TimeSpan? cooldown = null)
    {
        foreach (var action in _actions.GetActions(xeno))
        {
            if (TryComp(action, out XenoDeployTrapsActionComponent? actionComp))
            {
                _actions.SetCooldown(action.AsNullable(), cooldown ?? actionComp.SuccessCooldown);
                break;
            }
        }
    }

    private void OnXenoDeployTrapsAction(Entity<XenoDeployTrapsComponent> xeno, ref XenoDeployTrapsActionEvent args)
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
        if (xeno.Comp.DeployTrapsDoAfter != null ||
            !_xenoPlasma.TryRemovePlasmaPopup((xeno.Owner, null), args.PlasmaCost))
            return;

        // Deploy Traps
        var ev = new XenoDeployTrapsDoAfter(GetNetCoordinates(target));
        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.DeployTrapsDoAfterPeriod, ev, xeno)
            { BreakOnMove = true, DuplicateCondition = DuplicateConditions.SameEvent };
        if (_doAfter.TryStartDoAfter(doAfter, out var id))
            xeno.Comp.DeployTrapsDoAfter = id;
        else
            ReduceDeployTrapsCooldown(xeno);

        args.Handled = false;
    }

    private void OnDeployTrapsDoAfter(Entity<XenoDeployTrapsComponent> xeno, ref XenoDeployTrapsDoAfter args)
    {
        xeno.Comp.DeployTrapsDoAfter = null;
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
            //vector math to project a line and make a orthogonal line relative to the trapper at the target point.
            var xenoCoords = _transform.GetMoverCoordinates(xeno);
            var targetCoords = args.Coordinates;
            var angle = Math.Atan2(targetCoords.Y - xenoCoords.Y, targetCoords.X - xenoCoords.X);
            Vector2d direction = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            Vector2d ortho = new Vector2d(-direction.Y, direction.X);

            //tip of the projected cone
            var tipX = xenoCoords.X + direction.X * xeno.Comp.Range;
            var tipY = xenoCoords.Y + direction.Y * xeno.Comp.Range;

            //start of ortho line
            var lineStartX = tipX + ortho.X * xeno.Comp.DeployTrapsRadius;
            var lineStartY = tipY + ortho.Y * xeno.Comp.DeployTrapsRadius;
            var lineEndX = tipX - ortho.X * xeno.Comp.DeployTrapsRadius;
            var lineEndY = tipY - ortho.Y * xeno.Comp.DeployTrapsRadius;

            //Convert to vectors
            var lineStartVec = new Vector2((float)lineStartX, (float)lineStartY);
            var lineEndVec = new Vector2((float)lineEndX, (float)lineEndY);

            //To entitycoordinates
            var trapStart = EntityCoordinatesExtensions.ToCoordinates(xeno, lineStartVec);
            var trapEnd = EntityCoordinatesExtensions.ToCoordinates(xeno, lineEndVec);

            //Finally draw the line to get the list of affected tiles.
            var trapTiles = _line.DrawLine(trapStart, trapEnd, TimeSpan.Zero, xeno.Comp.Range, out _);

            foreach (var turf in trapTiles)
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
