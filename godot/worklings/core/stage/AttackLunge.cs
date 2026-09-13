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
/// The shape is wind-up / travel / strike / recover, which is how a fighting
/// beat actually reads:
///
///   wind-up   on the mark        — the clip's own preparation, going nowhere
///   travel    fast, eased out    — a short burst that ARRIVES on contact
///   strike    held at contact    — where the blow lands and impact frames fire
///   recover   slower, eased in   — the retreat that sells the effort
///
/// **The travel sits at the END of the wind-up, not the start.** The first
/// version approached in 0.26s and then stood beside its target for the
/// remaining ~0.6s of the clip, so the attacker arrived before it swung — which
/// is exactly the "the models slide across the screen" reading. Compressing the
/// same distance into the last fraction of a second before contact is the whole
/// difference between sliding and charging, and it is what gives the ghost
/// trail something fast enough to streak.
///
/// Deliberately not a physics or pathing system. The stage is a fixed diorama
/// with two marks on it; a lerp along the line between them is the whole job.
public sealed class AttackLunge
{
    private enum Phase { Idle, WindUp, Travel, Hold, Recover }

    private const double HoldSeconds = 0.14;
    private const double RecoverSeconds = 0.42;

    /// When false the attacker stays on its mark and only its animation plays;
    /// contact still fires on the same schedule, so impact frames, shake, sparks
    /// and damage numbers are unchanged.
    ///
    /// The travelling version was read as "the models slide across the screen",
    /// and that is a fair description of what it is: the mesh translates while
    /// its animation plays a stationary attack, so nothing about the body sells
    /// the movement. A walk cycle underneath would fix it properly; until then
    /// this switch is the honest comparison.
    public bool Travel { get; set; } = true;

    private StageActor? _actor;
    private GhostTrail? _trail;
    private Vector3 _travel;
    private double _travelSeconds;
    private double _travelStart;
    private Phase _phase = Phase.Idle;
    private double _elapsed;
    private System.Action? _onContact;
    private bool _contactFired;

    /// When the blow lands, in seconds from Begin(). Supplied by the caller
    /// from the attack animation's own length rather than assumed here — the
    /// contact frame belongs to the animation, not to the movement.
    private double _contactDelay;

    public bool IsBusy => _phase != Phase.Idle;

    /// Total time the lunge occupies, given when contact lands. The travel now
    /// fits inside the wind-up rather than preceding it, so contact time is the
    /// only thing that sets the length.
    public static double DurationFor(double contactDelay) =>
        contactDelay + HoldSeconds + RecoverSeconds;

    /// Send `attacker` at `target`. `contactDelay` is when the blow lands,
    /// measured from now.
    ///
    /// That timing comes from the attack animation, not from the movement. The
    /// first version fired contact the moment the approach finished — 0.26s in,
    /// near the *start* of a roughly one-second attack — so the flash and the
    /// damage happened while the Ram was still winding up. The strike frame has
    /// to land where the animation actually connects, which is near its end.
    ///
    /// The approach still runs at its own pace and simply waits if it arrives
    /// early, so travelling and stationary attacks connect on the same frame.
    public void Begin(StageActor attacker, StageActor target, double contactDelay,
                      System.Action onContact, GhostTrail? trail = null)
    {
        _actor = attacker;
        _trail = trail;

        // Aimed along the real axis between the two marks, so an attacker on the
        // bottom-left goes up and to the right and one on the far side comes back
        // down it. Stopping short of the target's centre leaves the bodies
        // adjacent rather than interpenetrating, which is what a strike looks
        // like — and how short is per character, because reach is per character.
        _travel = Travel
            ? (target.Root.Position - attacker.Root.Position) * attacker.Animations.TravelFraction
            : Vector3.Zero;

        _contactDelay = contactDelay;
        // A burst that ends on the contact frame. Clamped so a very short attack
        // clip cannot ask the travel to start before the swing does.
        _travelSeconds = System.Math.Min(attacker.Animations.TravelSeconds, contactDelay);
        _travelStart = contactDelay - _travelSeconds;

        _phase = Phase.WindUp;
        _elapsed = 0;
        _onContact = onContact;
        _contactFired = false;
        _trail?.Begin(_travel);
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
            case Phase.WindUp:
                // On the mark, letting the clip's preparation play. Nothing moves
                // here, which is what makes the launch that follows read as one.
                _actor.SetOffset(Vector3.Zero);
                if (_elapsed >= _travelStart) _phase = Phase.Travel;
                break;

            case Phase.Travel:
            {
                double t = _travelSeconds <= 0
                    ? 1
                    : System.Math.Min(1, (_elapsed - _travelStart) / _travelSeconds);
                // Ease-out: quick off the mark, settling into the blow.
                float eased = 1f - (float)System.Math.Pow(1 - t, 3);
                _actor.SetOffset(_travel * eased);
                _trail?.Advance((float)t);
                if (_elapsed >= _contactDelay)
                {
                    _actor.SetOffset(_travel);
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
        _trail?.Clear();
        _actor = null;
        _trail = null;
        _phase = Phase.Idle;
        _onContact = null;
    }
}
