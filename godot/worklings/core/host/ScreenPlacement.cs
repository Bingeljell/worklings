namespace Worklings.Core.Host;

/// A minimal, platform-free 2D point for placement math.
public readonly record struct PlacementPoint(double X, double Y);

/// A minimal, platform-free 2D size for placement math.
public readonly record struct PlacementSize(double Width, double Height);

/// A minimal, platform-free rectangle for placement math, origin at (X, Y).
public readonly record struct PlacementRect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
}

/// One leg of the pet's wander: how far to go, and how long to rest first.
///
/// Offsets are fractions of the *available* space rather than pixels, so the
/// same pattern reads the same on a laptop and on a 32-inch monitor.
public readonly struct PetRoamingIntent
{
    public double HorizontalOffset { get; }
    public double VerticalOffset { get; }
    public double RestDuration { get; }
    public double TravelDuration { get; }

    public PetRoamingIntent(
        double horizontalOffset,
        double verticalOffset,
        double restDuration,
        double travelDuration)
    {
        HorizontalOffset = System.Math.Min(System.Math.Max(horizontalOffset, -1), 1);
        VerticalOffset = System.Math.Min(System.Math.Max(verticalOffset, -1), 1);
        RestDuration = System.Math.Max(0, restDuration);
        TravelDuration = System.Math.Max(0, travelDuration);
    }
}

/// The wander pattern, as a fixed cycle rather than a random walk: the pet
/// should read as having somewhere to be, and randomness reads as twitching.
public static class PetRoamingPlanner
{
    private static readonly PetRoamingIntent[] Pattern =
    {
        new PetRoamingIntent(-0.24, 0, 7, 2.8),
        new PetRoamingIntent(0.18, 0.04, 9, 2.4),
        new PetRoamingIntent(-0.12, -0.03, 12, 2.2),
        new PetRoamingIntent(0.26, 0, 8, 3),
    };

    public static PetRoamingIntent Intent(ulong sequenceNumber) =>
        Pattern[(int)(sequenceNumber % (ulong)Pattern.Length)];
}

/// Where the pet's window sits, in screen coordinates.
///
/// Ported from Sources/CompanionCore/ScreenPlacement.swift. Deliberately free of
/// any engine type — it takes a rectangle and returns a point, so the same math
/// serves Godot's `DisplayServer` here and AppKit's `NSScreen` there, and can be
/// diffed against the Swift original without a window existing.
///
/// Everything here works in the screen's own coordinate space, including a
/// negative origin. A monitor placed left of or above the primary one has a
/// negative frame, and placement that assumes (0, 0) puts the pet off-screen on
/// exactly the setup most likely to be in use.
public static class ScreenPlacement
{
    /// Top-right of the screen, inset by a margin — out of the way of the work,
    /// and near the menu bar where a companion belongs.
    public static PlacementPoint DefaultOrigin(
        PlacementSize windowSize,
        PlacementRect visibleFrame,
        double margin = 24) =>
        ClampedOrigin(
            new PlacementPoint(
                visibleFrame.MaxX - windowSize.Width - margin,
                visibleFrame.MinY + margin),
            windowSize,
            visibleFrame,
            margin);

    /// Holds an origin inside the screen. The `Math.Max` on the maximums is not
    /// defensive noise: a window larger than the screen makes the maximum fall
    /// below the minimum, and without it the clamp would invert and place the
    /// window somewhere neither bound allows.
    public static PlacementPoint ClampedOrigin(
        PlacementPoint proposed,
        PlacementSize windowSize,
        PlacementRect visibleFrame,
        double margin = 0)
    {
        double minimumX = visibleFrame.MinX + margin;
        double minimumY = visibleFrame.MinY + margin;
        double maximumX = System.Math.Max(minimumX, visibleFrame.MaxX - windowSize.Width - margin);
        double maximumY = System.Math.Max(minimumY, visibleFrame.MaxY - windowSize.Height - margin);

        return new PlacementPoint(
            System.Math.Min(System.Math.Max(proposed.X, minimumX), maximumX),
            System.Math.Min(System.Math.Max(proposed.Y, minimumY), maximumY));
    }

    /// The next place to wander to. A step that would land closer than
    /// `minimumTravelDistance` reverses instead — otherwise the pet in a corner
    /// picks a destination the clamp folds back onto where it already is, and
    /// spends the whole pattern twitching in place.
    public static PlacementPoint RoamingOrigin(
        PlacementPoint currentOrigin,
        PetRoamingIntent intent,
        PlacementSize windowSize,
        PlacementRect visibleFrame,
        double margin = 24,
        double minimumTravelDistance = 48)
    {
        double availableWidth = System.Math.Max(
            0, visibleFrame.Width - windowSize.Width - margin * 2);
        double availableHeight = System.Math.Max(
            0, visibleFrame.Height - windowSize.Height - margin * 2);
        var offset = new PlacementPoint(
            intent.HorizontalOffset * availableWidth,
            intent.VerticalOffset * availableHeight);

        var destination = ClampedOrigin(
            new PlacementPoint(currentOrigin.X + offset.X, currentOrigin.Y + offset.Y),
            windowSize,
            visibleFrame,
            margin);

        double dx = destination.X - currentOrigin.X;
        double dy = destination.Y - currentOrigin.Y;
        if (System.Math.Sqrt(dx * dx + dy * dy) >= minimumTravelDistance)
        {
            return destination;
        }

        return ClampedOrigin(
            new PlacementPoint(currentOrigin.X - offset.X, currentOrigin.Y - offset.Y),
            windowSize,
            visibleFrame,
            margin);
    }
}
