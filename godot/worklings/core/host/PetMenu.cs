using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// What the player asked the pet to do.
public enum PetMenuChoice
{
    None,
    Feed,
    Play,
    Pet,
    Sleep,
    StayPut,
    CharacterSheet,
    EnterTheWarren,
    Rename,
    Quit,
}

/// The right-click menu: the first piece of *app* on the Godot side rather than
/// of port.
///
/// A borderless window has no chrome, so this is the only way in — everything
/// the Swift app puts in a menubar item hangs off here, and until it existed
/// there was no way to quit except Esc.
///
/// **It is an OS menu inside a click-through window**, which is the one thing
/// worth watching: the window passes mouse events through everywhere except the
/// pet's own box, and a popup has to capture them regardless of where it opens.
/// Godot's `PopupMenu` is its own OS window on desktop, so it gets its own input
/// handling and the passthrough region of the window beneath does not apply to
/// it.
///
/// Feed and Play are submenus rather than flat items because *which* food and
/// *which* activity are real mechanics — a Workling has a favourite of each and
/// pays out roughly double for it. Flattening them would hide the only choice in
/// the interaction.
public sealed class PetMenu
{
    private readonly PopupMenu _root = new();
    private readonly PopupMenu _feed = new();
    private readonly PopupMenu _play = new();

    /// Fired with the choice, plus the food or activity when the choice carries
    /// one. Null payloads for everything else.
    public event System.Action<PetMenuChoice, PetFood?, PetPlayActivity?>? Chosen;

    private const int FeedBase = 100;
    private const int PlayBase = 200;

    /// How much to magnify the menu. 0 asks the display.
    ///
    /// **The pet window disables content scaling** — that is what stops a square
    /// window letterboxing the project's 16:9 render size — and a popup inherits
    /// that. On a Retina display everything else on screen is drawn at 2x while
    /// this menu is drawn at 1x, so it comes out half the size of every other
    /// menu the player has ever seen. Scaling the popup is the fix; scaling the
    /// window is not, because that brings the black bars back.
    public float Scale { get; set; }

    private float EffectiveScale => Scale > 0
        ? Scale
        : (float)DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());

    public PetMenu(Node owner)
    {
        _feed.Name = "Feed";
        _play.Name = "Play";

        foreach (var food in PetNeedsEnumExtensions.AllFood)
        {
            _feed.AddItem(food.DisplayName(), FeedBase + (int)food);
        }
        foreach (var activity in PetNeedsEnumExtensions.AllPlayActivities)
        {
            _play.AddItem(activity.DisplayName(), PlayBase + (int)activity);
        }

        _root.AddChild(_feed);
        _root.AddChild(_play);
        owner.AddChild(_root);

        _feed.IdPressed += id => Chosen?.Invoke(
            PetMenuChoice.Feed, (PetFood)(id - FeedBase), null);
        _play.IdPressed += id => Chosen?.Invoke(
            PetMenuChoice.Play, null, (PetPlayActivity)(id - PlayBase));
        _root.IdPressed += id => Chosen?.Invoke((PetMenuChoice)id, null, null);
    }

    /// Rebuilt on every open rather than built once, because the header carries
    /// the pet's name, level and mood — a menu that opened showing yesterday's
    /// level would be worse than no header at all.
    /// `roaming` is passed in rather than held here, because the scene owns
    /// whether the pet wanders and the menu only reports it.
    public void Open(PetState state, bool roaming, Vector2I atScreenPosition)
    {
        _root.Clear();

        // A disabled first item, used as a header. Godot has no title on a
        // PopupMenu, and the alternative — a separate label — cannot be part of
        // the same OS popup.
        // Two short lines rather than one long one. A single
        // "Fren · Lv 14 · Hungry" is the widest thing in the menu by some way,
        // and a menu sized to its own header is wider than it needs to be
        // everywhere else.
        _root.AddItem($"{state.Name}  ·  Lv {state.Level}", 0);
        _root.SetItemDisabled(0, true);
        // The mood word comes from PetPresentation rather than a second copy
        // of the same table living here.
        _root.AddItem(PetPresentation.Make(state).MoodLabel, 1);
        _root.SetItemDisabled(1, true);
        _root.AddSeparator();

        _root.AddSubmenuNodeItem("Feed", _feed);
        _root.AddSubmenuNodeItem("Play", _play);
        _root.AddItem("Pet", (int)PetMenuChoice.Pet);
        _root.AddItem("Let it sleep", (int)PetMenuChoice.Sleep);
        _root.AddSeparator();

        // A checkbox rather than two items, so the current state is visible
        // without opening anything. Wandering is charming until you are trying
        // to work under it.
        _root.AddCheckItem("Stay put", (int)PetMenuChoice.StayPut);
        _root.SetItemChecked(_root.ItemCount - 1, !roaming);
        _root.AddSeparator();

        _root.AddItem("Character sheet…", (int)PetMenuChoice.CharacterSheet);
        _root.AddItem("Enter the Warren…", (int)PetMenuChoice.EnterTheWarren);
        _root.AddSeparator();

        _root.AddItem("Rename…", (int)PetMenuChoice.Rename);
        _root.SetItemDisabled(_root.ItemCount - 1, true);
        _root.AddItem("Quit", (int)PetMenuChoice.Quit);

        // One theme, carrying the font size with it. A popup's Size is in
        // *physical pixels*, so everything in the theme is scaled to the display
        // or the menu comes out half the size of every other menu on the machine.
        //
        // Applied to the submenus too: each is its own OS window and inherits
        // nothing from its parent.
        var theme = WorklingsTheme.For(EffectiveScale);
        foreach (var popup in new[] { _root, _feed, _play })
        {
            popup.Theme = theme;
            popup.ResetSize();
        }
        // Held on screen. The pet's default spot is the top-right corner, which
        // is the worst case for a menu that opens down and to the right: without
        // this it hangs off the edge and the submenus, finding no room beside
        // them, open on top of their own parent.
        //
        // ScreenPlacement already does exactly this arithmetic for the pet's own
        // window, negative-origin monitors included, so it does it here too.
        int screen = DisplayServer.WindowGetCurrentScreen();
        var frame = DesktopWindow.UsableFrame(screen);
        var placed = ScreenPlacement.ClampedOrigin(
            new PlacementPoint(atScreenPosition.X, atScreenPosition.Y),
            new PlacementSize(_root.Size.X, _root.Size.Y),
            frame,
            margin: 8);

        _root.Popup(new Rect2I(
            new Vector2I((int)System.Math.Round(placed.X), (int)System.Math.Round(placed.Y)),
            Vector2I.Zero));
    }

    public bool IsOpen => _root.Visible;
}
