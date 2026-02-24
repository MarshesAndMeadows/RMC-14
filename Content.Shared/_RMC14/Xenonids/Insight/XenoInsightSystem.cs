using Content.Shared._RMC14.Slow;
using Content.Shared._RMC14.Xenonids.Insight;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Network;

namespace Content.Shared._RMC14.Xenonids.Insight;

public sealed class XenoInsightSystem : EntitySystem
{
    [Dependency] private readonly SharedProjectileSystem _projectileSystem = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoInsightComponent, ProjectileHitEvent>(OnShotgunProjectileHit);
    }

    public int GetInsight(EntityUid uid)
    {
        if (!TryComp<XenoInsightComponent>(uid, out var insight))
            return 0;

        return insight.Insight;
    }

    public void IncrementInsight(Entity<XenoInsightComponent?> xeno, int amount)
    {
        if (!Resolve(xeno, ref xeno.Comp, false))
            return;

        xeno.Comp.Insight += amount;
        xeno.Comp.Insight = Math.Min(xeno.Comp.Insight, xeno.Comp.MaxInsight);
        Dirty(xeno);

        if (xeno.Comp.Insight >= xeno.Comp.MaxInsight)
            return;

        if (xeno.Comp.Insight >= xeno.Comp.MaxInsight)
            InsightEmpower((xeno.Owner, xeno.Comp));
    }

    public void InsightEmpower(Entity<XenoInsightComponent> xeno)
    {
        xeno.Comp.Empowered = true;
        Dirty(xeno);

        //empower popup TBD
        _popup.PopupClient(Loc.GetString("rmc-xeno-insight-empower"), xeno, xeno, PopupType.Medium);
    }

    private void OnShotgunProjectileHit(Entity<XenoInsightComponent> xeno, ref ProjectileHitEvent args)
    {
        var validTarget = false;

        //Check if target is a valid one, so we cant stack Insight on non-targets.
        if (_xeno.CanAbilityAttackTarget(xeno.Owner, args.Target))
            validTarget = true;

        //If we hit a target that is rooted, we need to do 75% more damage and always get max stacks of insight.
        if (HasComp<RMCRootedComponent>(args.Target) && validTarget)
        {
            args.Damage *= 1.75;
            xeno.Comp.Insight = Math.Max(xeno.Comp.Insight, xeno.Comp.MaxInsight);
            InsightEmpower((xeno.Owner, xeno.Comp));
        }
        else if (validTarget)
        {
            //otherwise do normal damage and only increment by 1 per pellet.
            IncrementInsight(xeno.Owner, 1);
        }

        Dirty(xeno);
    }
}

