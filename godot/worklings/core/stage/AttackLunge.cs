using Godot;

namespace Worklings.Core.Stage;

/// Moves an attacker into its target, holds on contact, and returns it.
///
/// Without this the combatants stand ~11 units apart — more than two body
/// lengths — and swing at empty air, which reads exactly as what it is: two
/// models playing clips in turn. Impact frames cannot fix that, because they
/// are the reaction to a collision that never happens. Closing the distance is
/// the prerequisite, not the polish.
///
/// The shape is approach / strike / recover, which is how a fighting beat
/// actually reads:
///
///   approach  fast, eased out    — commitment, the wind-up already played
///   strike    held at contact    — where the blow lands and impact frames fire
///   recover   slower, eased in   — the retreat that sells the effort
///
/// Deliberately not a physics or pathing system. The stage is a fixed diorama
/// with two marks on it; a lerp along the line between them is the whole job.
public sealed class AttackLunge
{
    private enum Phase { Idle, Approach, Hold, Recover }

    /// How close the attacker gets, as a share of the gap. Not 1.0 — stopping
    /// short of the target's centre leaves the bodies adjacent rather than
    /// interpenetrating, which is what a strike looks like.
    private const float CloseFraction = 0.62f;

    private const double ApproachSeconds = 0.26;
    private const double HoldSeconds = 0.14;
    private const double RecoverSeconds = 0.42;

    private StageActor? _actor;
    private Vector3 _travel;
    private Phase _phase = Phase.Idle;
    private double _elapsed;
    private System.Action? _onContact;
    private bool _contactFired;

    public bool IsBusy => _phase != Phase.Idle;

    /// Total time the lunge occupies, so the caller can hold the beat open for
    /// exactly as long as the movement needs rather than guessing.
    public static double Duration => ApproachSeconds + HoldSeconds + RecoverSeconds;

    /// Send `attacker` at `target`. `onContact` fires once, at the moment the
    /// approach completes — that is the frame the blow lands, so damage and
    /// impact frames hang off it rather than off the animation's start.
    public void Begin(StageActor attacker, StageActor target, System.Action onContact)
    {
        _actor = attacker;
        _travel = (target.Root.Position - attacker.Root.Position) * CloseFraction;
        _phase = Phase.Approach;
        _elapsed = 0;
        _onContact = onContact;
        _contactFired = false;
    }

    /// Advances the lunge. `scale` lets the caller slow or stop the movement —
    /// hit-stop passes 0, which freezes the attacker at the point of contact
    /// instead of letting it slide through the held frame.
    public void Tick(double delta, double scale = 1)
    {
        if (_actor == null || _phase == Phase.Idle) return;
        _elapsed += delta * scale;

        switch (_phase)
        {
            case Phase.Approach:
            {
                double t = System.Math.Min(1, _elapsed / ApproachSeconds);
                // Ease-out: quick off the mark, settling into the blow.
                float eased = 1f - (float)System.Math.Pow(1 - t, 3);
                _actor.SetOffset(_travel * eased);
                if (t >= 1)
                {
                    _phase = Phase.Hold;
                    _elapsed = 0;
                    if (!_contactFired) { _contactFired = true; _onContact?.Invoke(); }
                }
                break;
            }
            case Phase.Hold:
                _actor.SetOffset(_travel);
                if (_elapsed >= HoldSeconds) { _phase = Phase.Recover; _elapsed = 0; }
                break;

            case Phase.Recover:
            {
                double t = System.Math.Min(1, _elapsed / RecoverSeconds);
                // Ease-in-out back to the mark: the unhurried part, which is what
                // makes the fast approach read as commitment by contrast.
                float eased = t < 0.5
                    ? 2f * (float)(t * t)
                    : 1f - (float)System.Math.Pow(-2 * t + 2, 2) / 2f;
                _actor.SetOffset(_travel * (1f - eased));
                if (t >= 1) { _actor.ClearOffset(); _actor = null; _phase = Phase.Idle; }
                break;
            }
        }
    }

    public void Cancel()
    {
        _actor?.ClearOffset();
        _actor = null;
        _phase = Phase.Idle;
        _onContact = null;
    }
}
