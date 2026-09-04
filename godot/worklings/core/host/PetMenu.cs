using Godot;
using Worklings.Core.Connect;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Stage;

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
    FocusSession,
    LogWork,
    ConnectRepo,
    ToggleClaudeCode,
    ToggleCodex,
    MuteAudio,
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
    private readonly PopupMenu _tools = new();
    private readonly PopupMenu _repos = new();
    private readonly PopupMenu _families = new();
    private readonly PopupMenu _classes = new();

    /// The connected repositories, in the order the submenu listed them, so an
    /// id can be turned back into the path it names.
    private readonly System.Collections.Generic.List<string> _repoPaths = new();

    /// Fired with the choice, plus the food or activity when the choice carries
    /// one. Null payloads for everything else.
    public event System.Action<PetMenuChoice, PetFood?, PetPlayActivity?>? Chosen;

    /// A connected repository the player asked to stop watching, by path.
    public event System.Action<string>? DisconnectRepo;

    /// Identity, chosen from the menu. These carry a payload that does not fit
    /// `Chosen`'s food-or-activity shape, so they get their own signal rather
    /// than a third nullable parameter nothing else would ever use.
    public event System.Action<PetFamily>? SelectFamily;
    public event System.Action<PetClass>? SelectClass;

    private const int FeedBase = 100;
    private const int PlayBase = 200;
    private const int RepoBase = 300;
    private const int FamilyBase = 400;
    private const int ClassBase = 500;

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
        _tools.Name = "Tools";
        _repos.Name = "Repositories";
        _families.Name = "Workling";
        _classes.Name = "Class";

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
        _root.AddChild(_tools);
        _root.AddChild(_repos);
        _root.AddChild(_families);
        _root.AddChild(_classes);
        _families.IdPressed += id =>
            SelectFamily?.Invoke(PetFamilyExtensions.AllCases[(int)id - FamilyBase]);
        _classes.IdPressed += id =>
            SelectClass?.Invoke(PetClassExtensions.AllCases[(int)id - ClassBase]);
        _tools.IdPressed += id => Chosen?.Invoke((PetMenuChoice)id, null, null);
        _repos.IdPressed += id =>
        {
            if (id >= RepoBase)
            {
                DisconnectRepo?.Invoke(_repoPaths[(int)id - RepoBase]);
                return;
            }
            Chosen?.Invoke((PetMenuChoice)id, null, null);
        };
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
    /// Takes the whole session rather than a `PetState`, because half of what
    /// the menu shows is a question only the session can answer: whether an
    /// action is allowed right now, and whether a focus session is running.
    ///
    /// `roaming` is still passed in, because the scene owns whether the pet
    /// wanders and the menu only reports it.
    public void Open(
        PetSession session,
        bool roaming,
        System.Collections.Generic.IReadOnlyList<ConnectedRepo> repositories,
        Vector2I atScreenPosition)
    {
        var state = session.State;
        var now = System.DateTimeOffset.Now;
        var care = session.CareStatus;
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

        // Greyed out when the pet does not need it. The session refuses these
        // anyway; showing them enabled and having nothing happen is the version
        // that reads as a broken menu rather than as a full Workling.
        _root.AddSubmenuNodeItem("Feed", _feed);
        Gate(care.Availability(PetCareActionKind.Feed, state));
        _root.AddSubmenuNodeItem("Play", _play);
        Gate(care.Availability(PetCareActionKind.Play, state));
        _root.AddItem("Pet", (int)PetMenuChoice.Pet);
        _root.AddItem("Let it sleep", (int)PetMenuChoice.Sleep);
        Gate(care.Availability(PetCareActionKind.Sleep, state));
        _root.AddSeparator();

        // The two hand-driven activity signals, for when nothing is watching.
        // "Log work" carries its own refusal — a cooldown and a daily cap — and
        // says why in the item itself, because it is the one action here that is
        // refused for a reason the pet's condition does not explain.
        _root.AddItem(
            session.IsFocusSessionActive ? "End focus session" : "Start focus session",
            (int)PetMenuChoice.FocusSession);
        var logging = session.WorkLogAvailability(now);
        _root.AddItem(logging.IsEnabled ? "Log work" : $"Log work — {logging.Explanation}",
                      (int)PetMenuChoice.LogWork);
        _root.SetItemDisabled(_root.ItemCount - 1, !logging.IsEnabled);
        _root.AddSeparator();

        // A checkbox rather than two items, so the current state is visible
        // without opening anything. Wandering is charming until you are trying
        // to work under it.
        _root.AddCheckItem("Stay put", (int)PetMenuChoice.StayPut);
        _root.SetItemChecked(_root.ItemCount - 1, !roaming);
        // Read from the setting rather than from a live player: the dungeon's
        // audio only exists while a delve is running, and this menu is not
        // reachable while one is.
        _root.AddCheckItem("Mute the dungeon", (int)PetMenuChoice.MuteAudio);
        _root.SetItemChecked(_root.ItemCount - 1, CombatAudio.Muted);
        _root.AddSeparator();

        // Each tool's own state, read fresh every time the menu opens: another
        // program can edit these files, so a remembered answer goes stale.
        _tools.Clear();
        foreach (var tool in new[] { ConnectableTool.ClaudeCode, ConnectableTool.Codex })
        {
            var wiring = tool.Connector().State();
            _tools.AddItem(
                $"{tool.DisplayName()} — {Describe(wiring)}",
                (int)(tool == ConnectableTool.ClaudeCode
                    ? PetMenuChoice.ToggleClaudeCode
                    : PetMenuChoice.ToggleCodex));
            // Unknown means the config exists and could not be read or parsed.
            // Offering a toggle there would mean writing over something we could
            // not inspect, which is the one thing this must never do.
            if (wiring == ConnectionState.Unknown)
            {
                _tools.SetItemDisabled(_tools.ItemCount - 1, true);
            }
        }
        _root.AddSubmenuNodeItem("Connect a tool", _tools);
        _root.AddSeparator();

        // A submenu that LISTS what is connected, not a single item that opens a
        // folder picker. The picker alone answered no question the player was
        // asking: whether the last one worked, whether a second is allowed, and
        // how to stop watching one. Seeing the list is the answer to all three.
        _repos.Clear();
        _repoPaths.Clear();
        foreach (var repo in repositories)
        {
            _repoPaths.Add(repo.Path);
            _repos.AddItem($"✓  {ShortPath(repo.Path)}", RepoBase + _repoPaths.Count - 1);
            // The full path as a tooltip, because two checkouts of the same
            // project have the same last component and would otherwise be two
            // identical rows.
            _repos.SetItemTooltip(_repos.ItemCount - 1, $"{repo.Path}\n(click to stop watching)");
        }
        if (repositories.Count > 0)
        {
            _repos.AddSeparator();
        }
        // Connecting a repository is itself the opt-in to the git source — a
        // separate toggle to find afterwards is a feature people conclude is
        // broken.
        _repos.AddItem("Connect a repository…", (int)PetMenuChoice.ConnectRepo);

        _root.AddSubmenuNodeItem(
            repositories.Count == 0
                ? "Repositories"
                : $"Repositories  ({repositories.Count})",
            _repos);
        _root.AddSeparator();

        // Identity, in the menu as well as on the character screen. The screen
        // is where you go to READ about a Workling; this is where you go to
        // change one, and looking for it here first is what everybody does.
        BuildFamilies(state);
        BuildClasses(state);
        _root.AddSubmenuNodeItem("Choose Workling", _families);
        _root.AddSubmenuNodeItem("Choose class", _classes);
        _root.AddSeparator();

        _root.AddItem("Character sheet…", (int)PetMenuChoice.CharacterSheet);
        _root.AddItem("Enter the Warren…", (int)PetMenuChoice.EnterTheWarren);
        _root.AddSeparator();

        _root.AddItem("Rename…", (int)PetMenuChoice.Rename);
        _root.AddItem("Quit", (int)PetMenuChoice.Quit);

        // One theme, carrying the font size with it. A popup's Size is in
        // *physical pixels*, so everything in the theme is scaled to the display
        // or the menu comes out half the size of every other menu on the machine.
        //
        // Applied to the submenus too: each is its own OS window and inherits
        // nothing from its parent.
        var theme = WorklingsTheme.For(EffectiveScale);
        foreach (var popup in
                 new[] { _root, _feed, _play, _tools, _repos, _families, _classes })
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

    /// Enough of a path to recognise it by.
    ///
    /// The leaf alone is not enough — "gitrepo2" or "worklings" tells you
    /// nothing about *which* checkout, and a stray one you did not mean to
    /// connect looks exactly like one you did. Home becomes `~`, and anything
    /// deeper than three segments keeps only the last two behind an ellipsis, so
    /// the row stays short and still names something.
    public static string ShortPath(string path)
    {
        string trimmed = path.TrimEnd('/');
        string home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.UserProfile);
        if (home.Length > 0 && trimmed.StartsWith(home, System.StringComparison.Ordinal))
        {
            trimmed = "~" + trimmed[home.Length..];
        }

        var parts = trimmed.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            // "/" trims to nothing and would render as a blank row — a menu item
            // you cannot read is worse than a long one.
            return path;
        }
        if (parts.Length <= 3)
        {
            return trimmed;
        }
        return $"…/{parts[^2]}/{parts[^1]}";
    }

    /// All five families, with the two design-stage ones listed and unpickable —
    /// the roster reads as five so the shape of the design is visible, and they
    /// un-grey on their own the day their art is baked.
    private void BuildFamilies(PetState state)
    {
        _families.Clear();
        var families = PetFamilyExtensions.AllCases;
        for (int i = 0; i < families.Length; i++)
        {
            var family = families[i];
            _families.AddRadioCheckItem(
                family.HasArt()
                    ? family.DisplayName()
                    : $"{family.DisplayName()} (coming soon)",
                FamilyBase + i);
            _families.SetItemChecked(i, family == state.Family);
            _families.SetItemDisabled(i, !family.HasArt());
        }
    }

    /// Every class carries its role: "Aegis" says nothing to someone meeting it
    /// for the first time and "Aegis — Tank" says all of it.
    private void BuildClasses(PetState state)
    {
        _classes.Clear();
        var classes = PetClassExtensions.AllCases;
        for (int i = 0; i < classes.Length; i++)
        {
            var petClass = classes[i];
            _classes.AddRadioCheckItem(
                $"{petClass.DisplayName()} — {petClass.Role()}", ClassBase + i);
            _classes.SetItemChecked(i, petClass == state.PetClass);
        }
    }

    /// What a tool's wiring looks like, in the words the menu uses.
    private static string Describe(ConnectionState state) => state switch
    {
        ConnectionState.Live => "connected",
        // Ours, but pointing at an adapter that is gone. Choosing it reconnects
        // rather than disconnects, which is why it does not say "connected".
        ConnectionState.Stale => "reconnect (app moved)",
        ConnectionState.Unknown => "config unreadable",
        _ => "not connected",
    };

    /// Disables the item just added when the pet has no use for it.
    private void Gate(PetActionAvailability availability)
    {
        if (!availability.IsEnabled)
        {
            _root.SetItemDisabled(_root.ItemCount - 1, true);
        }
    }
}
