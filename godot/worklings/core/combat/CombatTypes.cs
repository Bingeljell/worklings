using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// One thing the pet can do on its turn.
///
/// **The player now picks one of these directly, every round.** They used to
/// pick an `Approach` — a standing stance (Aggressive / Careful / Clever) that
/// *derived* the action — and the fight resolved itself between occasional
/// prompts. That was retired on 2026-09-13 on Nikhil's call: "each action is
/// player decided". The stances read as odd in play and, worse, they lied by
/// omission — choosing Careful meant "brace **if** hurt", so a player who chose
/// it expecting to brace could watch their Workling strike instead and read
/// that as the game ignoring them. A verb the player presses and the creature
/// then performs cannot have that gap.
///
/// This enum is also where new moves land as they are designed — spells, and
/// per-class attacks. It is deliberately the whole vocabulary of a pet turn.
public enum CombatAction { Strike, Brace, Signature }

/// What the foe will do this round, decided at the top of it and published
/// before the player commits.
///
/// **This is the other half of making the fight legible.** The foe used to
/// decide at the moment it acted, which meant its move was unknowable until it
/// had already happened — so a Monolith wind-up could only be read *after* the
/// round it mattered in, and bracing against a slam was guesswork dressed as a
/// prompt. Rolling the intent up front and showing it as an icon over the
/// creature's head turns every round into a real decision: you can see the slam
/// coming and choose to brace it.
///
/// The roll happens here, once, and the performance is bound by it. A foe that
/// telegraphs a slam and then does something else would be worse than no icon.
public enum FoeIntentKind
{
    /// An ordinary attack.
    Strike,
    /// Winding up. It does not attack this round; the blow lands next round.
    WindUp,
    /// The wound-up heavy blow, landing this round. Guaranteed to hit.
    Slam,
    /// Seizing the Workling to Snare its Agility instead of attacking.
    Grab,
    /// Attacking, and blurring aside afterwards — over-extending into an
    /// opening.
    Phase,
}

/// The foe's declared move for the round, and how to describe it to a player.
public readonly record struct FoeIntent(FoeIntentKind Kind)
{
    /// The two or three words that go under the icon.
    public string Label => Kind switch
    {
        FoeIntentKind.WindUp => "Winding up",
        FoeIntentKind.Slam => "Heavy slam",
        FoeIntentKind.Grab => "Grasping",
        FoeIntentKind.Phase => "Blur strike",
        _ => "Attacking",
    };

    /// The sentence a hover earns — what it does, and what to do about it.
    public string Detail => Kind switch
    {
        FoeIntentKind.WindUp =>
            "Gathering for a heavy blow. It will not attack this round — the slam lands next.",
        FoeIntentKind.Slam =>
            "A guaranteed heavy hit, this round. Bracing halves it.",
        FoeIntentKind.Grab =>
            "It will seize you instead of striking, dulling your Agility for a few rounds.",
        FoeIntentKind.Phase =>
            "A quick strike, then it blurs aside and over-extends — an opening for your Signature.",
        _ => "An ordinary attack. Bracing halves it.",
    };
}

/// Where the encounter is right now.
///
/// `AwaitingAction` replaced an `AwaitingDecision(DecisionReason)` that a round
/// only sometimes entered — on a cadence, at low HP, on a telegraph, on an
/// opening. Every round now waits for the player, so the reason a round is
/// waiting is no longer interesting: it is waiting because that is what a round
/// does.
public readonly struct CombatStatus : System.IEquatable<CombatStatus>
{
    public enum StatusKind { Ongoing, AwaitingAction, PetVictory, PetDefeat }

    public StatusKind Kind { get; }

    private CombatStatus(StatusKind kind) { Kind = kind; }

    public static readonly CombatStatus Ongoing = new(StatusKind.Ongoing);
    public static readonly CombatStatus AwaitingAction = new(StatusKind.AwaitingAction);
    public static readonly CombatStatus PetVictory = new(StatusKind.PetVictory);
    public static readonly CombatStatus PetDefeat = new(StatusKind.PetDefeat);

    public bool IsOngoing => Kind == StatusKind.Ongoing;
    public bool IsAwaitingAction => Kind == StatusKind.AwaitingAction;
    public bool IsOver => Kind is StatusKind.PetVictory or StatusKind.PetDefeat;

    public bool Equals(CombatStatus other) => Kind == other.Kind;
    public override bool Equals(object? obj) => obj is CombatStatus o && Equals(o);
    public override int GetHashCode() => Kind.GetHashCode();
    public override string ToString() => Kind.ToString();
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
    /// The round has opened, the foe has declared, and the fight is waiting for
    /// the player. Carries the intent so the renderer never has to ask twice.
    public sealed record AwaitingAction(FoeIntent Intent) : CombatEvent;
    public sealed record EncounterEnded(bool Victory) : CombatEvent;
}
