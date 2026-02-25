using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Xenonids.AcidMine;
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoAcidMineSystem))]
public sealed partial class XenoAcidMineComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public DamageSpecifier DamageToMobs = new();

    [DataField(required: true), AutoNetworkedField]
    public DamageSpecifier DamageToStructures = new();

    [DataField(required: true), AutoNetworkedField]
    public DamageSpecifier DamageToStructuresEmpowered = new();

    [DataField, AutoNetworkedField]
    public bool Empowered = false;

    [DataField, AutoNetworkedField]
    public int Range = 7;

    [DataField]
    public DoAfterId? AcidMineDoAfter;

    //1 for a 3x3 area.
    [DataField, AutoNetworkedField]
    public int AcidMineRadius = 1;

    // Length of do-after
    [DataField, AutoNetworkedField]
    public TimeSpan AcidMineDoAfterPeriod = TimeSpan.FromSeconds(2);

    [DataField, AutoNetworkedField]
    public EntProtoId TelegraphEffect = "RMCEffectXenoTelegraphRed";
}
