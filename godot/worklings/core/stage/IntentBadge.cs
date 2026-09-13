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
    private readonly Control _root;
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

        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(_root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 2);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        _root.AddChild(column);

        _frame = new PanelContainer();
        column.AddChild(_frame);

        _glyph = StageType.Label("", 30, StageType.Ink, bold: true);
        _glyph.HorizontalAlignment = HorizontalAlignment.Center;
        _glyph.CustomMinimumSize = new Vector2(46, 0);
        _frame.AddChild(_glyph);

        _label = StageType.Label("", 15, StageType.Ink, bold: true);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(_label);

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
    private static string Glyph(FoeIntentKind kind) => kind switch
    {
        FoeIntentKind.WindUp => "◈",
        FoeIntentKind.Slam => "✦",
        FoeIntentKind.Grab => "✷",
        FoeIntentKind.Phase => "≈",
        _ => "⚔",
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
        _root.Position = new Vector2(at.X - size.X * 0.5f, at.Y - size.Y);
        _tip.Position = new Vector2(at.X + 34, at.Y - 10);
    }

    /// Whether the badge is currently claiming a threat, for anything that wants
    /// to colour itself to match.
    public bool Threatening => _threatening;
}
