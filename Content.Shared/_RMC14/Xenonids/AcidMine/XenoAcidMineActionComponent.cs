namespace Content.Shared._RMC14.Xenonids.AcidMine;

public sealed class XenoAcidMineActionComponent : Component
{
    [DataField, AutoNetworkedField]
    public float FailCooldownMult = 0.5f;

    [DataField, AutoNetworkedField]
    public TimeSpan SuccessCooldown = TimeSpan.FromSeconds(20);
}
