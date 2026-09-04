using Godot;

namespace Worklings.Core.Host;

/// The puff the pet leaves in, and arrives in.
///
/// Eight 256x256 frames on one 1024x512 sheet, four across and two down: the
/// cloud builds over the first four and disperses over the last four. Played
/// forward when the pet goes down to the Warren, and again when it comes back —
/// the same animation reads as both, because a puff that gathers and clears
/// says "something happened here" in either direction.
///
/// The pet is emptied at the puff's densest frame rather than at its start, so
/// the smoke covers the moment the body disappears. That timing is the whole
/// trick: hide the cut, and the pet reads as having *left* rather than as having
/// been switched off.
///
/// **Note on the art:** this sheet is pixel art, from the direction the project
/// has since left — the roster is stylized 3D renders now. A pixel puff over a
/// rendered Ram is a deliberate mixed-media choice or a mismatch, and it is a
/// look-at-it call rather than an argument. The alternative is a particle burst
/// tinted from FamilyEnergy, which the dungeon already uses for hit sparks.
public partial class SmokePuff : Node2D
{
    private const int Columns = 4;
    private const int Rows = 2;
    private const int FrameCount = Columns * Rows;
    private const int Cell = 256;

    /// The frame the pet vanishes on — the densest cloud, frame four of eight.
    public const int CoverFrame = 3;

    private AnimatedSprite2D _sprite = null!;

    /// How long the whole puff takes. Short: it is a transition, not a cutscene.
    [Export] public float Seconds { get; set; } = 0.7f;

    /// Fired on the frame the smoke is thickest — the moment to swap what is
    /// underneath it.
    public event System.Action? Covered;

    /// Fired when the puff has cleared and the node is about to free itself.
    public event System.Action? Cleared;

    private bool _covered;

    public override void _Ready()
    {
        var texture = GD.Load<Texture2D>("res://assets/effects/smoke_puff.png");
        var frames = new SpriteFrames();
        frames.SetAnimationSpeed("default", FrameCount / System.Math.Max(Seconds, 0.05f));
        frames.SetAnimationLoopMode("default", SpriteFrames.LoopMode.None);

        for (int i = 0; i < FrameCount; i++)
        {
            var region = new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(i % Columns * Cell, i / Columns * Cell, Cell, Cell),
            };
            frames.AddFrame("default", region);
        }

        _sprite = new AnimatedSprite2D { SpriteFrames = frames };
        AddChild(_sprite);
        _sprite.FrameChanged += OnFrameChanged;
        _sprite.AnimationFinished += OnFinished;
        _sprite.Play();
    }

    private void OnFrameChanged()
    {
        if (_covered || _sprite.Frame < CoverFrame)
        {
            return;
        }
        _covered = true;
        Covered?.Invoke();
    }

    private void OnFinished()
    {
        // A puff shorter than four frames would never reach the cover frame, and
        // the thing underneath would never be swapped. Belt and braces.
        if (!_covered)
        {
            _covered = true;
            Covered?.Invoke();
        }
        Cleared?.Invoke();
        QueueFree();
    }

    /// Scales the puff so its 256px art covers `pixels` of window.
    public void FitTo(float pixels) => Scale = Vector2.One * (pixels / Cell);
}
