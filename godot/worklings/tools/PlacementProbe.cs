using Godot;
using Worklings.Core.Host;

/// Compares the pet window's placement math against reference output captured
/// from the Swift original.
///
/// The fixtures lean on the two cases that are wrong-by-default rather than the
/// happy path: a **second monitor with a negative origin**, which placement that
/// assumes (0, 0) puts off-screen on exactly the setup most likely to be in use;
/// and a **window larger than the screen**, where the clamp's maximum falls below
/// its minimum and inverts without the guard.
public partial class PlacementProbe : Node
{
    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        string F(double v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        string P(PlacementPoint p) => $"({F(p.X)}, {F(p.Y)})";

        var laptop = new PlacementRect(0, 25, 1512, 920);
        var second = new PlacementRect(-2560, -400, 2560, 1440);
        var tiny = new PlacementRect(100, 100, 120, 90);

        o.AppendLine("== defaultOrigin ==");
        foreach (var (label, frame) in new (string, PlacementRect)[]
                 { ("laptop", laptop), ("second", second), ("tiny", tiny) })
        {
            foreach (var size in new[]
                     { new PlacementSize(220, 220), new PlacementSize(640, 480) })
            {
                o.AppendLine($"{label} {F(size.Width)}x{F(size.Height)}: "
                           + P(ScreenPlacement.DefaultOrigin(size, frame)));
            }
        }
        o.AppendLine("margin 0: " + P(ScreenPlacement.DefaultOrigin(
            new PlacementSize(220, 220), laptop, margin: 0)));
        o.AppendLine("margin 200: " + P(ScreenPlacement.DefaultOrigin(
            new PlacementSize(220, 220), laptop, margin: 200)));

        o.AppendLine("== clampedOrigin ==");
        var size220 = new PlacementSize(220, 220);
        foreach (var (label, proposed) in new (string, PlacementPoint)[]
                 {
                     ("inside", new PlacementPoint(400, 300)),
                     ("off left", new PlacementPoint(-900, 300)),
                     ("off right", new PlacementPoint(9000, 300)),
                     ("off top", new PlacementPoint(400, -900)),
                     ("off bottom", new PlacementPoint(400, 9000)),
                     ("both corners", new PlacementPoint(-9000, 9000)),
                 })
        {
            o.AppendLine($"laptop {label}: "
                       + P(ScreenPlacement.ClampedOrigin(proposed, size220, laptop)));
            o.AppendLine($"laptop {label} m24: "
                       + P(ScreenPlacement.ClampedOrigin(proposed, size220, laptop, 24)));
            o.AppendLine($"second {label}: "
                       + P(ScreenPlacement.ClampedOrigin(proposed, size220, second, 24)));
        }
        o.AppendLine("oversized: " + P(ScreenPlacement.ClampedOrigin(
            new PlacementPoint(500, 500), new PlacementSize(4000, 3000), tiny, 24)));

        o.AppendLine("== roaming intents ==");
        foreach (ulong n in new ulong[] { 0, 1, 2, 3, 4, 5, 9 })
        {
            var i = PetRoamingPlanner.Intent(n);
            o.AppendLine($"{n}: h={F(i.HorizontalOffset)} v={F(i.VerticalOffset)} "
                       + $"rest={F(i.RestDuration)} travel={F(i.TravelDuration)}");
        }
        var clamped = new PetRoamingIntent(4.5, -3, -8, -0.5);
        o.AppendLine($"clamped: h={F(clamped.HorizontalOffset)} v={F(clamped.VerticalOffset)} "
                   + $"rest={F(clamped.RestDuration)} travel={F(clamped.TravelDuration)}");

        o.AppendLine("== roamingOrigin ==");
        var origin = ScreenPlacement.DefaultOrigin(size220, laptop);
        o.AppendLine("start: " + P(origin));
        for (ulong n = 0; n < 8; n++)
        {
            origin = ScreenPlacement.RoamingOrigin(
                origin, PetRoamingPlanner.Intent(n), size220, laptop);
            o.AppendLine($"step {n}: " + P(origin));
        }
        var corner = new PlacementPoint(laptop.MinX + 24, laptop.MinY + 24);
        o.AppendLine("flip from corner: " + P(ScreenPlacement.RoamingOrigin(
            corner, new PetRoamingIntent(-0.5, -0.5, 5, 2), size220, laptop)));
        o.AppendLine("tiny step: " + P(ScreenPlacement.RoamingOrigin(
            corner, new PetRoamingIntent(0.001, 0, 5, 2), size220, laptop)));
        o.AppendLine("second monitor step: " + P(ScreenPlacement.RoamingOrigin(
            new PlacementPoint(-1200, 200), PetRoamingPlanner.Intent(1), size220, second)));

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
