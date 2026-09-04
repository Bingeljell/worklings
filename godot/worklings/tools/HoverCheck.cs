using Godot;
using Worklings.Core.Host;
using Worklings.Core.Pet;

/// Shows the hover summary over a stand-in window, and prints the sentence each
/// fixture produces.
///
/// The placement is what needs looking at: the pet roams into corners, and a
/// panel positioned by offset alone ends up off-screen exactly there. The
/// sentences themselves are already diffed against Swift in `status_probe`.
public partial class HoverCheck : Node
{
    public override async void _Ready()
    {
        var anchor = GetWindow();
        anchor.Size = new Vector2I(320, 320);

        var hover = new HoverSummary(this, 1.0f);
        var frame = DesktopWindow.UsableFrame(0);

        foreach (var (label, state, at) in new (string, PetState, Vector2I)[]
                 {
                     ("content, middle of the screen", Pet(),
                      new Vector2I((int)(frame.X + frame.Width / 2), (int)(frame.Y + frame.Height / 2))),
                     // Top-left: there is no room above, so it has to flip below.
                     ("hungry, hard against the top", Pet(hunger: 80),
                      new Vector2I((int)frame.X, (int)frame.Y)),
                     // Bottom-right: it has to clamp rather than run off.
                     ("two needs, bottom-right corner", Pet(hunger: 80, happiness: 25),
                      new Vector2I((int)(frame.X + frame.Width) - 320, (int)(frame.Y + frame.Height) - 320)),
                     ("everything at once", Pet(95, 5, 10, 5),
                      new Vector2I((int)(frame.X + frame.Width / 2), (int)frame.Y)),
                 })
        {
            anchor.Position = at;
            hover.Show(state, anchor);
            await ToSignal(GetTree().CreateTimer(0.2), "timeout");
            GD.Print($"{label}:");
            GD.Print($"  \"{PetCareStatus.Make(state).HoverSummary}\"");
            var placeFrame = DesktopWindow.UsableFrame(
                DisplayServer.WindowGetCurrentScreen(anchor.GetWindowId()));
            GD.Print($"  asked {at}, window actually at {anchor.Position} size {anchor.Size}");
            GD.Print($"  panel at {PanelPosition()}  place-frame "
                   + $"{placeFrame.X},{placeFrame.Y} {placeFrame.Width}x{placeFrame.Height}");
        }

        hover.Close();

        // The placement rules on their own, including the flip a real macOS
        // window can never reach — it will not sit under the menu bar, so the
        // panel always fits above it. A monitor arranged above another one, or
        // either of the two platforms this has never run on, can reach it.
        GD.Print("placement, on a 1000x800 screen at the origin:");
        var screen = new PlacementRect(0, 0, 1000, 800);
        var pet = new Vector2I(320, 320);
        var panel = new Vector2I(260, 56);
        foreach (var (label, at) in new (string, Vector2I)[]
                 {
                     ("middle", new Vector2I(340, 240)),
                     ("hard against the top — flips below", new Vector2I(340, 0)),
                     ("one pixel of room — still flips", new Vector2I(340, 63)),
                     ("just enough room — stays above", new Vector2I(340, 64)),
                     ("left edge — clamps", new Vector2I(0, 240)),
                     ("right edge — clamps", new Vector2I(680, 240)),
                     ("bottom — clamps", new Vector2I(340, 480)),
                 })
        {
            GD.Print($"  {label,-38} pet {at} -> "
                   + $"{HoverSummary.Place(at, pet, panel, 8, screen)}");
        }

        GetTree().Quit();
    }

    /// The panel is the only other window this scene owns.
    private Vector2I PanelPosition()
    {
        foreach (var child in GetChildren())
        {
            if (child is Window w) return w.Position;
        }
        return Vector2I.Zero;
    }

    private static PetState Pet(
        double hunger = 20, double energy = 80, double happiness = 70, double trust = 50) =>
        new(name: "Fren",
            family: PetFamily.Wildkin,
            needs: new PetNeeds(hunger, energy, happiness, trust),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: System.DateTimeOffset.Now);
}
