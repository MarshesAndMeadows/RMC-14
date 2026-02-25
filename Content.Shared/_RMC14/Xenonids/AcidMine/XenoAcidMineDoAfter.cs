using Content.Shared.DoAfter;
using Robust.Shared.Map;

namespace Content.Shared._RMC14.Xenonids.AcidMine;

public sealed partial class XenoAcidMineDoAfter : SimpleDoAfterEvent
{
    [DataField]
    public NetCoordinates Coordinates;

    [DataField]
    public bool Empowered;

    public XenoAcidMineDoAfter(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public XenoAcidMineDoAfter(bool empowered)
    {
        Empowered = empowered;
    }
}
