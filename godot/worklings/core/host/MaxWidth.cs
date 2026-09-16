using Godot;

namespace Worklings.Core.Host;

/// Holds one child at its natural width up to a limit, then stops.
///
/// Godot has a minimum size and no maximum, so a control that should fill a
/// narrow window has no way to decline the space in a wide one. The character
/// screen needs both: at 560 the gear rail wants every pixel, and at 1040 three
/// tiles stretched across 780 of them read as empty plaques rather than as gear.
///
/// The child's own size flags still apply inside the rect it is given, so an
/// expanding child fills the capped width rather than collapsing to its minimum.
public sealed partial class MaxWidth : Container
{
    private readonly float _limit;
    private readonly bool _centred;

    public MaxWidth(float limit, Control child, bool centred = true)
    {
        _limit = limit;
        _centred = centred;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // The one case where the child is given less than it asked for is a
        // window narrower than the child's own minimum; clipped rather than
        // allowed to spill over whatever is beside it.
        ClipContents = true;
        AddChild(child);
    }

    public override Vector2 _GetMinimumSize()
    {
        var minimum = Vector2.Zero;
        foreach (var node in GetChildren())
        {
            if (node is not Control child || !child.Visible) continue;
            minimum = minimum.Max(child.GetCombinedMinimumSize());
        }
        // Never demand more than the cap, or the cap becomes a floor and the
        // window cannot be made narrower than it.
        minimum.X = Mathf.Min(minimum.X, _limit);
        return minimum;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren) return;
        foreach (var node in GetChildren())
        {
            if (node is not Control child || !child.Visible) continue;
            // Never below the child's own minimum. Handing a Container child a
            // rect smaller than it can be makes it ask to be re-sorted, which
            // re-hands it the same rect: the layout never settles and the
            // process stops responding without ever printing an error.
            float natural = child.GetCombinedMinimumSize().X;
            float width = Mathf.Clamp(Size.X, natural, Mathf.Max(_limit, natural));
            float left = _centred ? Mathf.Max((Size.X - width) * 0.5f, 0f) : 0f;
            FitChildInRect(child, new Rect2(left, 0, width, Size.Y));
        }
    }
}
