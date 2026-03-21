// Content.Client/_RMC14/Xenonids/Charge/CursorCharge/XenoCursorSteeringClientSystem.cs

using Content.Shared._RMC14.Xenonids.Charge.CursorCharge;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._RMC14.Xenonids.Charge.CursorCharge;

public sealed class XenoCursorSteeringClientSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private Angle _lastSentAngle;
    private XenoCursorSteeringOverlay? _overlay;

    public override void Initialize()
    {
        SubscribeLocalEvent<XenoCursorSteeringComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<XenoCursorSteeringComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnAttached(Entity<XenoCursorSteeringComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        _lastSentAngle = default;
        _overlay = new XenoCursorSteeringOverlay(EntityManager);
        _overlayManager.AddOverlay(_overlay);
    }

    private void OnDetached(Entity<XenoCursorSteeringComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _lastSentAngle = default;
        if (_overlay != null)
        {
            _overlayManager.RemoveOverlay(_overlay);
            _overlay = null;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_player.LocalEntity is not { } controlled)
            return;

        // Only stream cursor when actively charging
        if (!TryComp(controlled, out ActiveXenoCursorSteeringComponent? _))
            return;

        if (!TryComp(controlled, out XenoCursorSteeringComponent? steering))
            return;

        var screenPos = _input.MouseScreenPosition;
        var mapCoords = _eye.PixelToMap(screenPos);

        if (mapCoords.MapId == MapId.Nullspace)
            return;

        var entityPos = _transform.GetMapCoordinates(controlled);

        if (entityPos.MapId != mapCoords.MapId)
            return;

        var diff = mapCoords.Position - entityPos.Position;
        if (diff.LengthSquared() < 0.01f)
            return;

        var angle = diff.ToAngle();

        if (Math.Abs((angle - _lastSentAngle).Reduced().Degrees) < 2.0)
            return;

        _lastSentAngle = angle;
        RaiseNetworkEvent(new XenoCursorSteeringMessage { CursorAngle = angle });
    }
}
