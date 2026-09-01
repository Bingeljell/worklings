using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// The standing strategy the pet fights on between decisions.
public enum Approach
{
    /// Strike every round. No self-preservation, no held resources.
    Aggressive,
    /// Brace while hurt, Strike once recovered. The thresholds are hysteretic.
    Careful,
    /// Strike, holding the Signature until the foe is inside finishing range,
    /// then spending it unprompted.
    Clever,
}

/// One thing the pet can do on its turn.
public enum CombatAction { Strike, Brace, Signature }

/// Why the fight paused for input.
public enum DecisionReason
{
    Cadence,   // the every-few-rounds reassess beat
    LowHP,     // the pet is faltering
    Opening,   // an evasive foe over-extended — the window to Unleash
    Telegraph, // a heavy foe is winding up — Brace or eat it
}

/// Where the encounter is right now. Swift models this as an enum with an
/// associated DecisionReason on one case; here the reason rides alongside and is
/// only meaningful when Kind is AwaitingDecision.
public readonly struct CombatStatus : System.IEquatable<CombatStatus>
{
    public enum StatusKind { Ongoing, AwaitingDecision, PetVictory, PetDefeat }

    public StatusKind Kind { get; }
    public DecisionReason Reason { get; }

    private CombatStatus(StatusKind kind, DecisionReason reason = DecisionReason.Cadence)
    {
        Kind = kind;
        Reason = reason;
    }

    public static readonly CombatStatus Ongoing = new(StatusKind.Ongoing);
    public static readonly CombatStatus PetVictory = new(StatusKind.PetVictory);
    public static readonly CombatStatus PetDefeat = new(StatusKind.PetDefeat);
    public static CombatStatus AwaitingDecision(DecisionReason reason) =>
        new(StatusKind.AwaitingDecision, reason);

    public bool IsOngoing => Kind == StatusKind.Ongoing;
    public bool IsAwaitingDecision => Kind == StatusKind.AwaitingDecision;
    public bool IsOver => Kind is StatusKind.PetVictory or StatusKind.PetDefeat;

    public bool Equals(CombatStatus other) =>
        Kind == other.Kind
        && (Kind != StatusKind.AwaitingDecision || Reason == other.Reason);

    public override bool Equals(object? obj) => obj is CombatStatus o && Equals(o);
    public override int GetHashCode() => System.HashCode.Combine(Kind, Reason);
    public override string ToString() =>
        Kind == StatusKind.AwaitingDecision ? $"awaiting({Reason})" : Kind.ToString();
}

/// A structured record of what happened, one entry at a time, so the app can
/// narrate and animate each beat without re-deriving anything.
///
/// This is the seam the renderer consumes: Godot subscribes to the event stream
/// and plays animations off it, and never needs to know the combat rules.
public abstract record CombatEvent
{
    private CombatEvent() { }

    public sealed record EncounterBegan(string Pet, string Foe) : CombatEvent;
    public sealed record RoundBegan(int Round) : CombatEvent;
    public sealed record Struck(string Attacker, string Defender, StrikeOutcome Outcome) : CombatEvent;
    public sealed record Signature(string Attacker, string Defender, StrikeOutcome Outcome) : CombatEvent;
    public sealed record Braced(string Who, int Regen) : CombatEvent;

    /// A grabber (Snag) seizes the pet instead of striking, Snaring its Agility.
    public sealed record Grabbed(string Attacker, string Target, int AgilityLoss) : CombatEvent;

    /// An evasive foe (Flicker) blurs aside — the pet's next blow will slip.
    public sealed record Phased(string Who) : CombatEvent;

    /// A colossus (Monolith) winds up its Slam, telegraphed a turn ahead.
    public sealed record Telegraphed(string Who) : CombatEvent;

    /// The wound-up Slam lands — a heavy, guaranteed hit.
    public sealed record Slammed(string Attacker, string Defender, StrikeOutcome Outcome) : CombatEvent;

    /// A colossus Hardens at an HP phase, raising Guard for the rest of the fight.
    public sealed record Hardened(string Who, int GuardGain) : CombatEvent;

    public sealed record Defeated(string Who) : CombatEvent;
    public sealed record DecisionPoint(DecisionReason Reason) : CombatEvent;
    public sealed record EncounterEnded(bool Victory) : CombatEvent;
}
