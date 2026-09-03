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
    public void Open(PetState state, Vector2I atScreenPosition)
    {
        _root.Clear();

        // A disabled first item, used as a header. Godot has no title on a
        // PopupMenu, and the alternative — a separate label — cannot be part of
        // the same OS popup.
        _root.AddItem($"{state.Name}  ·  Lv {state.Level}  ·  {MoodWord(state.Mood)}", 0);
        _root.SetItemDisabled(0, true);
        _root.AddSeparator();

        _root.AddSubmenuNodeItem("Feed", _feed);
        _root.AddSubmenuNodeItem("Play", _play);
        _root.AddItem("Pet", (int)PetMenuChoice.Pet);
        _root.AddItem("Let it sleep", (int)PetMenuChoice.Sleep);
        _root.AddSeparator();

        _root.AddItem("Character sheet…", (int)PetMenuChoice.CharacterSheet);
        // Disabled rather than absent: the character screen is designed and not
        // built, and a menu that shows what is coming reads better than one that
        // silently lacks it.
        _root.SetItemDisabled(_root.ItemCount - 1, true);
        _root.AddItem("Enter the Warren…", (int)PetMenuChoice.EnterTheWarren);
        _root.AddSeparator();

        _root.AddItem("Rename…", (int)PetMenuChoice.Rename);
        _root.SetItemDisabled(_root.ItemCount - 1, true);
        _root.AddItem("Quit", (int)PetMenuChoice.Quit);

        // Applied to the submenus too: each is its own OS window and inherits
        // nothing from its parent.
        float scale = EffectiveScale;
        _root.ContentScaleFactor = scale;
        _feed.ContentScaleFactor = scale;
        _play.ContentScaleFactor = scale;

        _root.ResetSize();
        _feed.ResetSize();
        _play.ResetSize();
        _root.Popup(new Rect2I(atScreenPosition, Vector2I.Zero));
    }

    public bool IsOpen => _root.Visible;

    /// The mood, in the words the design uses rather than the enum's.
    private static string MoodWord(PetMood mood) => mood switch
    {
        PetMood.Happy => "Happy",
        PetMood.Content => "Content",
        PetMood.Hungry => "Hungry",
        PetMood.Sleepy => "Sleepy",
        PetMood.Sad => "Sad",
        PetMood.Wary => "Wary",
        _ => mood.ToString(),
    };
}
