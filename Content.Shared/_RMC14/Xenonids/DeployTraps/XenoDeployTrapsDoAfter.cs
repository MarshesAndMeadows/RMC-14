using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Xenonids.DeployTraps;

[Serializable, NetSerializable]
public sealed partial class XenoDeployTrapsDoAfter : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    public XenoDeployTrapsDoAfter(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}
