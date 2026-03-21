// XenoCursorSteeringSystem.cs

using Content.Shared._RMC14.Emote;
using Content.Shared._RMC14.Entrenching;
using Content.Shared._RMC14.Stun;
using Content.Shared._RMC14.Xenonids.Charge;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Charge.CursorCharge;

public sealed class XenoCursorSteeringSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedRMCEmoteSystem _rmcEmote = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;
    [Dependency] private readonly RMCSizeStunSystem _sizeStun = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<ActiveXenoCursorSteeringComponent> _activeQuery;
    private readonly ProtoId<DamageTypePrototype> _blunt = "Blunt";

    private readonly HashSet<(EntityUid Charger, EntityUid Target)> _cursorHit = new();
    private EntityQuery<XenoCursorSteeringComponent> _steeringQuery;
    private EntityQuery<XenoToggleChargingRecentlyHitComponent> _recentlyHitQuery;

    public override void Initialize()
    {
        SubscribeNetworkEvent<XenoCursorSteeringMessage>(OnCursorSteeringMessage);

        SubscribeLocalEvent<ActiveXenoCursorSteeringComponent, MoveInputEvent>(OnChargingMoveInput);
        SubscribeLocalEvent<ActiveXenoCursorSteeringComponent, MoveEvent>(OnChargingMove);
        SubscribeLocalEvent<ActiveXenoCursorSteeringComponent, StartCollideEvent>(OnCursorChargingCollide);

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _activeQuery = GetEntityQuery<ActiveXenoCursorSteeringComponent>();

        _steeringQuery = GetEntityQuery<XenoCursorSteeringComponent>();
        _recentlyHitQuery = GetEntityQuery<XenoToggleChargingRecentlyHitComponent>();
    }
    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;
        try
        {
            foreach (var (charger, target) in _cursorHit)
            {
                if (TerminatingOrDeleted(charger) || TerminatingOrDeleted(target))
                    continue;

                if (!EntityManager.EntityExists(target))
                    continue;

                if (!_steeringQuery.TryComp(charger, out var steering))
                    continue;

                if (steering.Stage == 0)
                    continue;

                // Cooldown gate reusing existing component
                if (_recentlyHitQuery.TryComp(target, out var recently) &&
                    time < recently.LastHitAt + recently.Cooldown)
                    continue;

                HandleCursorChargeCollision(charger, target, steering);

                if (!TerminatingOrDeleted(target) && EntityManager.EntityExists(target))
                {
                    var recentlyHit = EnsureComp<XenoToggleChargingRecentlyHitComponent>(target);
                    recentlyHit.LastHitAt = time;
                    Dirty(target, recentlyHit);
                }
            }
        }
        finally
        {
            _cursorHit.Clear();
        }


        var query = EntityQueryEnumerator<ActiveXenoCursorSteeringComponent, XenoCursorSteeringComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var active, out var steering, out var physics))
        {
            // Accumulate distance and increment stage
            var lastVel = physics.LinearVelocity;
            var distThisFrame = lastVel.Length() * frameTime;
            steering.DistanceTraveled += distThisFrame;

            if (steering.Stage < steering.MaxStage &&
                steering.DistanceTraveled >= steering.DistancePerStage)
            {
                steering.Stage++;
                steering.DistanceTraveled -= steering.DistancePerStage;

                if (steering.Stage == steering.MaxStage)
                    _rmcEmote.TryEmoteWithChat(uid, "XenoRoar", cooldown: TimeSpan.FromSeconds(20));
            }

            // Turn rate scales down as stage increases
            var stageRatio = steering.Stage / (float)steering.MaxStage;
            var maxTurnRate = MathHelper.Lerp(steering.BaseTurnRate, steering.MinTurnRate, stageRatio);

            var angleDelta = (steering.TargetHeading - steering.CurrentHeading).Reduced();
            var delta = angleDelta.Theta;
            if (delta > Math.PI) delta -= 2 * Math.PI;
            else if (delta < -Math.PI) delta += 2 * Math.PI;

            var turnAmount = (float)Math.Clamp(delta, -maxTurnRate * frameTime, maxTurnRate * frameTime);
            steering.CurrentHeading = new Angle(steering.CurrentHeading.Theta + turnAmount);

            // Speed scales up with stage
            var speed = steering.BaseSpeed + steering.Stage * steering.SpeedPerStage;
            var vel = AngleToWorldVec(steering.CurrentHeading) * speed;
            _physics.SetAwake((uid, physics), true);
            _physics.SetLinearVelocity(uid, vel, body: physics);

            var cardinalDir = steering.CurrentHeading.GetDir();
            _transform.SetWorldRotation(uid, cardinalDir.ToAngle());

            Dirty(uid, steering);
        }
    }
    private static System.Numerics.Vector2 AngleToWorldVec(Angle angle)
    {
        return new System.Numerics.Vector2((float)Math.Cos(angle.Theta), (float)Math.Sin(angle.Theta));
    }

    private void HandleCursorChargeCollision(EntityUid charger, EntityUid target, XenoCursorSteeringComponent steering)
{
    var stage = steering.Stage;
    var atMax = stage == steering.MaxStage;

    // Humans
    if (TryComp(target, out MobStateComponent? mobState) && !_mobState.IsDead(target, mobState))
    {
        if (!_xeno.CanAbilityAttackTarget(charger, target))
            return;

        var mult = atMax ? steering.HumanDamageMultiplierMax : steering.HumanDamageMultiplier;
        var damageAmount = stage * mult;
        var damage = new DamageSpecifier();
        damage.DamageDict[_blunt] = damageAmount;
        _damageable.TryChangeDamage(target, damage, origin: charger);

        var knockdown = atMax
            ? TimeSpan.FromSeconds(steering.HumanKnockdownDuration * 2)
            : TimeSpan.FromSeconds(steering.HumanKnockdownDuration);
        _stun.TryParalyze(target, knockdown, false);

        // Knock sideways like CM13
        var origin = _transform.GetMapCoordinates(charger);
        _sizeStun.KnockBack(target, origin, 2, 2, knockBackSpeed: stage * 2f);

        if (_net.IsServer)
        {
            _popup.PopupEntity(
                Loc.GetString("rmc-xeno-charge-knockback-others", ("user", charger), ("target", target)),
                target,
                PopupType.MediumCaution
            );
        }

        // Hitting a human loses 1 stage, matching CM13's CCA_MOMENTUM_LOSS_MIN
        steering.Stage = Math.Max(0, steering.Stage - 1);
        Dirty(charger, steering);
        return;
    }

    // Barricades — damage and stop
    if (HasComp<BarricadeComponent>(target))
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[_blunt] = stage * steering.BarricadeCollisionDamage;
        _damageable.TryChangeDamage(target, damage);

        if (_net.IsServer)
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/metalhit.ogg"), target);

        // Barricades always stop the charge in CM13
        ResetCharge(charger, steering);
        return;
    }

    // Generic destructible structures
    if (HasComp<DamageableComponent>(target))
    {
        var damage = new DamageSpecifier();
        damage.DamageDict[_blunt] = stage * steering.StructureDamageMultiplier;
        _damageable.TryChangeDamage(target, damage);

        // If destroyed, lose 1 stage and continue. If survived, stop.
        if (TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target))
        {
            steering.Stage = Math.Max(0, steering.Stage - 1);
            Dirty(charger, steering);
        }
        else if (_physicsQuery.TryGetComponent(target, out var physics) && physics.Hard && physics.BodyType == BodyType.Static)
        {
            ResetCharge(charger, steering);
        }
        return;
    }

    // Hard blocker with no health — walls etc
    if (_physicsQuery.TryGetComponent(target, out var wallPhysics) &&
        wallPhysics.Hard &&
        wallPhysics.BodyType == BodyType.Static)
    {
        ResetCharge(charger, steering);
    }
}

private void ResetCharge(EntityUid charger, XenoCursorSteeringComponent steering)
{
    steering.Stage = 0;
    steering.DistanceTraveled = 0f;
    _physics.SetLinearVelocity(charger, System.Numerics.Vector2.Zero);
    Dirty(charger, steering);

    if (_net.IsServer)
    {
        _popup.PopupEntity(
            Loc.GetString("rmc-xeno-charge-skids-halt", ("xeno", charger)),
            charger,
            PopupType.SmallCaution
        );
    }
}

    private void OnCursorChargingCollide(Entity<ActiveXenoCursorSteeringComponent> ent, ref StartCollideEvent args)
    {
        if (!_steeringQuery.TryComp(ent, out var steering))
            return;

        if (steering.Stage == 0)
            return;

        _cursorHit.Add((ent.Owner, args.OtherEntity));
    }

    private void OnCursorSteeringMessage(XenoCursorSteeringMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } controlled)
            return;

        if (!TryComp(controlled, out XenoCursorSteeringComponent? steering))
            return;

        steering.TargetHeading = msg.CursorAngle;
        steering.LastCursorUpdate = _timing.CurTime;
        Dirty(controlled, steering);
    }

    private void OnChargingMoveInput(Entity<ActiveXenoCursorSteeringComponent> ent, ref MoveInputEvent args)
    {
        // Suppress all directional input while charging
        args.Entity.Comp.HeldMoveButtons &= ~MoveButtons.AnyDirection;
    }

    private void OnChargingMove(Entity<ActiveXenoCursorSteeringComponent> ent, ref MoveEvent args)
    {
        // Movement is driven by physics velocity, not the mover system
        // Cancel any mover-driven movement
    }



}
