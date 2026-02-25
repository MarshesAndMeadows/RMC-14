using Content.Shared._RMC14.Xenonids.ResinSurge;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Xenonids.DeployTraps;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoDeployTrapsSystem))]
public sealed partial class XenoDeployTrapsComponent : Component
{
    // Length of do-after
    [DataField, AutoNetworkedField]
    public TimeSpan DeployTrapsDoAfterPeriod = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public int DeployTrapsRadius = 2;

    // Prototype for trap to create
    [DataField, AutoNetworkedField]
    public EntProtoId DeployTrapsId = "XenoTrapperTrap";

    // Prototype for trap to create
    [DataField, AutoNetworkedField]
    public EntProtoId DeployEmpoweredTrapsId = "XenoTrapperEmpoweredTrap";

    [DataField, AutoNetworkedField]
    public int Range = 7;

    [DataField, AutoNetworkedField]
    public bool Empowered = false;

    [DataField]
    public DoAfterId? DeployTrapsDoAfter;



}
