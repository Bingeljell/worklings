using Godot;
using Worklings.Core.Combat;

namespace Worklings.Core.Stage;

/// The icon over a foe's head saying what it is about to do.
///
/// **The single change that turns a round into a decision.** A foe used to
/// choose its move at the moment it made it, so the move was unknowable until it
/// had already landed — and the Monolith, whose whole design is telegraph then
/// slam, could only be read one round too late. "Brace or eat it" was a prompt
/// with nothing behind it: there was no way to tell which round the slam was
/// coming, so bracing was a coin flip the game presented as tactics.
///
/// The intent is now rolled at the top of the round and shown here before the
/// player commits, and the foe is bound to it. That is the whole loop Nikhil
/// asked for: see what it will do, choose accordingly, watch both resolve.
///
/// Drawn as a screen-space badge tracking the creature's head in 3D rather than
/// as a billboard in the world, so it stays the same size whatever the foe's
/// stage height is — a Scamp at 2.40 units and a Monolith at 7.50 need the same
/// legible icon, and a world-space quad would give the Scamp a postage stamp.
public sealed class IntentBadge
{
    /// How far above the creature's own height the badge floats, in world units.
    private const float Clearance = 0.9f;

    private readonly Camera3D _camera;
    private readonly VBoxContainer _root;
    private readonly PanelContainer _frame;
    private readonly Label _glyph;
    private readonly Label _label;
    private readonly Control _tip;
    private readonly Label _tipText;

    private StageActor? _actor;
    private float _headHeight;
    private bool _threatening;

    public IntentBadge(Control parent, Camera3D camera)
    {
        _camera = camera;

        // The badge is the container itself rather than a bare Control wrapping
        // one. A plain Control does not size to its children, so wrapping left
        // the badge at zero by zero — which positions and hit-tests as a point,
        // and drew nothing anywhere near the creature's head.
        _root = new VBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddThemeConstantOverride("separation", 2);
        _root.Alignment = BoxContainer.AlignmentMode.Center;
        parent.AddChild(_root);

        _frame = new PanelContainer();
        _root.AddChild(_frame);

        _glyph = StageType.Label("", 30, StageType.Ink, bold: true);
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.CustomMinimumSize = new Vector2(46, 0);
        _frame.AddChild(_glyph);

        _label = StageType.Label("", 15, StageType.Ink, bold: true);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(_label);

        // The hover text. Parented to the HUD root rather than to the badge so a
        // long sentence is clipped by the screen and not by the badge's own
        // 46-pixel box.
        _tip = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var tipStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.03f, 0.03f, 0.95f),
            BorderColor = new Color(0.45f, 0.40f, 0.32f, 0.9f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 7, ContentMarginBottom = 8,
        };
        tipStyle.SetBorderWidthAll(1);
        tipStyle.SetCornerRadiusAll(3);
        _tip.AddThemeStyleboxOverride("panel", tipStyle);
        parent.AddChild(_tip);

        _tipText = StageType.Label("", 16, StageType.Ink);
        _tipText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tipText.CustomMinimumSize = new Vector2(280, 0);
        _tip.AddChild(_tipText);

        // A real hover target over the badge, so the mouse can reach it. The
        // badge itself ignores the mouse; this sits on top of it.
        var hover = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        hover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hover.MouseEntered += () => _tip.Visible = _root.Visible;
        hover.MouseExited += () => _tip.Visible = false;
        _frame.AddChild(hover);
    }

    /// A one-glyph pictogram per intent. Text rather than art on purpose: the
    /// icon set is a real art task and this is the placeholder that lets the
    /// mechanic ship and be judged first — the shape of the badge, its position
    /// and its tooltip are what need testing, not the drawing inside it.
    ///
    /// **Every glyph here must exist in Chakra Petch**, which is a Latin text
    /// face and not a symbol font. The first pass used ⚔ ◈ ✦ ✷ and three of the
    /// four were absent from the file, so the capture showed the missing-glyph
    /// box instead — a swords icon that rendered as a small square with a line
    /// through it. Checked against the TTF's cmap rather than eyeballed: the
    /// Dingbats and Geometric Shapes blocks are almost entirely missing, while
    /// Latin-1 punctuation and the four solid triangles/diamonds are present.
    /// Anything added here should be checked the same way, or drawn as real art
    /// when the icon set lands.
    private static string Glyph(FoeIntentKind kind) => kind switch
    {
        // Gathering upward, then coming down: the wind-up and the slam are a
        // pair and read as one because the arrows point at each other.
        FoeIntentKind.WindUp => "▲",
        FoeIntentKind.Slam => "▼",
        FoeIntentKind.Grab => "¤",
        FoeIntentKind.Phase => "≈",
        _ => "†",
    };

    /// Shows the badge over an actor, for an intent.
    public void Show(StageActor actor, float stageHeight, FoeIntent intent, bool threatens)
    {
        _actor = actor;
        _headHeight = stageHeight + Clearance;
        _threatening = threatens;
        _glyph.Text = Glyph(intent.Kind);
        _label.Text = intent.Label.ToUpperInvariant();
        _tipText.Text = intent.Detail;

        // Threat reads red, a breather reads neutral. A wind-up is the round the
        // player gets for free and it should not look like the round that hurts.
        var accent = threatens ? new Color(0.95f, 0.35f, 0.28f) : new Color(0.62f, 0.72f, 0.85f);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.045f, 0.04f, 0.92f),
            BorderColor = accent,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        _frame.AddThemeStyleboxOverride("panel", style);
        _label.AddThemeColorOverride("font_color", accent);
        _glyph.AddThemeColorOverride("font_color", accent);

        _root.Visible = true;
        // The text just changed, so the badge's own size has too — and Track
        // centres on it.
        _root.ResetSize();
        Track();
    }

    public void Hide()
    {
        _root.Visible = false;
        _tip.Visible = false;
        _actor = null;
    }

    /// Follows the creature. Called every frame because the body moves — it
    /// lunges, it is knocked back, and it falls over.
    public void Track()
    {
        if (_actor == null || !_root.Visible) return;
        var head = _actor.Root.GlobalPosition + new Vector3(0, _headHeight, 0);
        if (_camera.IsPositionBehind(head)) { _root.Visible = false; return; }
        var at = _camera.UnprojectPosition(head);
        var size = _root.Size;
        var frame = _root.GetViewportRect().Size;

        // Clamped into the frame. A 7.50-unit Monolith puts its head near the
        // top of a 720-tall viewport, and the badge floats above that — so the
        // badge for the one creature whose intent matters most was the one
        // hanging off the top edge. Pinning it below the run readout is better
        // than a badge that is only visible for short foes.
        const float TopGuard = 74f;
        float x = Mathf.Clamp(at.X - size.X * 0.5f, 8f, frame.X - size.X - 8f);
        float y = Mathf.Max(at.Y - size.Y, TopGuard);
        _root.Position = new Vector2(x, y);

        _tip.ResetSize();
        // The tip flips to the badge's left rather than running off the right
        // edge, which is where a foe standing stage-right always put it.
        float tipX = x + size.X + 14;
        if (tipX + _tip.Size.X > frame.X - 8) tipX = x - _tip.Size.X - 14;
        _tip.Position = new Vector2(Mathf.Max(8f, tipX), y);
    }

    /// Whether the badge is currently claiming a threat, for anything that wants
    /// to colour itself to match.
    public bool Threatening => _threatening;
}
