// XenoCursorSteeringComponent.cs

using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._RMC14.Xenonids.Charge.CursorCharge;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoCursorSteeringComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Stage = 0;

    [DataField, AutoNetworkedField]
    public int MaxStage = 8;

    [DataField, AutoNetworkedField]
    public float DistancePerStage = 2f; // tiles traveled to gain a stage

    [DataField, AutoNetworkedField]
    public float DistanceTraveled = 0f;

    [DataField, AutoNetworkedField]
    public float BaseSpeed = 5f;

    [DataField, AutoNetworkedField]
    public float SpeedPerStage = 1f;

    [DataField, AutoNetworkedField]
    public float BaseTurnRate = MathF.PI * 2f; // fast turning at low speed

    [DataField, AutoNetworkedField]
    public float MinTurnRate = MathF.PI * 0.4f; // slowest turning at max stage

    [DataField, AutoNetworkedField]
    public Angle CurrentHeading;

    [DataField, AutoNetworkedField]
    public Angle TargetHeading;

    [DataField, AutoNetworkedField]
    public TimeSpan LastCursorUpdate;

    [DataField]
    public float HumanDamageMultiplier = 5f;

    [DataField]
    public float HumanDamageMultiplierMax = 8f;

    [DataField]
    public float BarricadeDamageMultiplier = 22f;

    [DataField]
    public float StructureDamageMultiplier = 40f;

    [DataField]
    public float SentryDamageMultiplier = 9f;

    [DataField]
    public int HumanKnockdownDuration = 1; // seconds, 2 at max stage

    [DataField]
    public FixedPoint2 DefaultCollisionDamage = 40f;

    [DataField]
    public FixedPoint2 BarricadeCollisionDamage = 22f;
}
