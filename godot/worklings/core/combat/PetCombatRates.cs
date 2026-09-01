namespace Worklings.Core.Combat;

/// The dungeon combat model's tunable numbers and the core resolution formulas.
///
/// Every value is first-pass alpha tuning — a knob, held now and retuned from
/// real play later without touching the mechanism. The design and the full knob
/// list live in docs/design/dungeons.md.
///
/// Ported from Sources/CompanionCore/PetCombat.swift. The defaults and the
/// clamping in the constructor must match exactly: they are load-bearing for
/// every check in CombatChecks.
public sealed class PetCombatRates
{
    /// Flat combat HP every combatant starts from, before Vitality.
    public double BaseHP { get; }
    /// Combat HP added per point of Vitality.
    public double VitalityToHP { get; }
    /// Strike damage per point of Power.
    public double PowerScale { get; }
    /// Strike damage removed per point of the target's Guard.
    public double GuardScale { get; }
    /// Symmetric random swing applied to a Strike's damage, e.g. 0.15 = +/-15%.
    public double StrikeVariance { get; }
    /// Base hit chance before the attacker/defender Agility difference.
    public double BaseHitChance { get; }
    /// Hit chance gained per point of Agility advantage over the defender.
    public double AgilityToHit { get; }
    /// Lower bound on hit chance, so nothing is ever a guaranteed miss.
    public double HitChanceFloor { get; }
    /// Upper bound on hit chance, so nothing is ever a guaranteed hit.
    public double HitChanceCeiling { get; }
    /// Crit chance gained per point of Agility.
    public double CritChancePerAgility { get; }
    /// Damage multiplier applied on a crit.
    public double CritMultiplier { get; }
    /// Lower bound on the condition-to-combat effectiveness multiplier.
    public double CombatEffectivenessFloor { get; }
    /// Damage multiplier applied to a Strike landing on a Bracing target.
    public double BraceMitigation { get; }
    /// Floor on the HP a Bracing combatant regains that round.
    public int BraceRegen { get; }

    /// Share of max HP a Brace restores, so the patient option keeps paying as
    /// the numbers grow. A flat regen is a rounding error against a late-game HP
    /// pool, which is what turned Careful into a death spiral: the pet stopped
    /// attacking and could not heal its way back out either.
    public double BraceRegenFraction { get; }

    /// Damage multiplier on the once-per-encounter Signature, which always hits.
    public double SignatureMultiplier { get; }
    /// Rounds between the steady "reassess" decision points.
    public int DecisionCadenceRounds { get; }
    /// HP fraction below which the "faltering" decision point fires (once).
    public double LowHPEventThreshold { get; }
    /// HP fraction below which a Careful Approach starts choosing Brace.
    public double CarefulBraceThreshold { get; }

    /// HP fraction a Careful Approach must climb back ABOVE before it resumes
    /// Striking. The gap from CarefulBraceThreshold is deliberate hysteresis:
    /// with a single threshold a hurt pet latched into Brace forever, never
    /// damaged the foe, and never healed enough to unlatch.
    public double CarefulResumeThreshold { get; }

    /// Foe HP fraction at or below which a Clever Approach spends its held
    /// Signature. Without this, Clever was a byte-for-byte copy of Aggressive.
    public double CleverFinisherThreshold { get; }

    /// The level a Workling must reach before the first dungeon unlocks.
    public int DelveGateLevel { get; }
    /// If any need is at or below this, the pet refuses to delve.
    public double RefusalNeedThreshold { get; }
    /// Share of max HP regained between encounters within a delve.
    public double InterEncounterRegenFraction { get; }
    /// Bonus XP for completing a full delve, forfeited when banking early.
    public double DelveCompletionXP { get; }

    public PetCombatRates(
        double baseHP = 20,
        double vitalityToHP = 3,
        double powerScale = 1.5,
        double guardScale = 1,
        double strikeVariance = 0.15,
        double baseHitChance = 0.75,
        double agilityToHit = 0.03,
        double hitChanceFloor = 0.25,
        double hitChanceCeiling = 0.95,
        double critChancePerAgility = 0.01,
        double critMultiplier = 1.5,
        double combatEffectivenessFloor = 0.5,
        double braceMitigation = 0.5,
        int braceRegen = 2,
        double braceRegenFraction = 0.08,
        double signatureMultiplier = 1.5,
        int decisionCadenceRounds = 3,
        double lowHPEventThreshold = 0.3,
        double carefulBraceThreshold = 0.4,
        double carefulResumeThreshold = 0.6,
        double cleverFinisherThreshold = 0.35,
        int delveGateLevel = 3,
        double refusalNeedThreshold = 10,
        double interEncounterRegenFraction = 0.3,
        double delveCompletionXP = 50)
    {
        BaseHP = System.Math.Max(baseHP, 0);
        VitalityToHP = System.Math.Max(vitalityToHP, 0);
        PowerScale = System.Math.Max(powerScale, 0);
        GuardScale = System.Math.Max(guardScale, 0);
        StrikeVariance = System.Math.Clamp(strikeVariance, 0, 1);
        BaseHitChance = System.Math.Clamp(baseHitChance, 0, 1);
        AgilityToHit = System.Math.Max(agilityToHit, 0);
        HitChanceFloor = System.Math.Clamp(hitChanceFloor, 0, 1);
        HitChanceCeiling = System.Math.Clamp(hitChanceCeiling, 0, 1);
        CritChancePerAgility = System.Math.Max(critChancePerAgility, 0);
        CritMultiplier = System.Math.Max(critMultiplier, 1);
        CombatEffectivenessFloor = System.Math.Clamp(combatEffectivenessFloor, 0, 1);
        BraceMitigation = System.Math.Clamp(braceMitigation, 0, 1);
        BraceRegen = System.Math.Max(braceRegen, 0);
        BraceRegenFraction = System.Math.Clamp(braceRegenFraction, 0, 1);
        SignatureMultiplier = System.Math.Max(signatureMultiplier, 1);
        DecisionCadenceRounds = System.Math.Max(decisionCadenceRounds, 1);
        LowHPEventThreshold = System.Math.Clamp(lowHPEventThreshold, 0, 1);
        double brace = System.Math.Clamp(carefulBraceThreshold, 0, 1);
        CarefulBraceThreshold = brace;
        // Resume can never sit below the brace threshold, or the hysteresis
        // inverts and the latch comes back.
        CarefulResumeThreshold = System.Math.Clamp(carefulResumeThreshold, brace, 1);
        CleverFinisherThreshold = System.Math.Clamp(cleverFinisherThreshold, 0, 1);
        DelveGateLevel = System.Math.Max(delveGateLevel, 1);
        RefusalNeedThreshold = System.Math.Clamp(refusalNeedThreshold, 0, 100);
        InterEncounterRegenFraction = System.Math.Clamp(interEncounterRegenFraction, 0, 1);
        DelveCompletionXP = System.Math.Max(delveCompletionXP, 0);
    }

    /// A combatant's maximum combat HP. A transient pool, unrelated to the
    /// condition needs.
    ///
    /// Swift's `.rounded()` is round-half-away-from-zero, NOT C#'s default
    /// banker's rounding — Math.Round(2.5) is 2 in C# and 3 in Swift. Every
    /// rounding call in this port passes MidpointRounding.AwayFromZero for that
    /// reason; getting it wrong shifts damage and HP by one at every .5.
    public int MaxHP(int vitality) =>
        (int)System.Math.Round(BaseHP + vitality * VitalityToHP, System.MidpointRounding.AwayFromZero);

    /// What a Brace restores to a combatant of this size: a share of its max HP,
    /// never less than the flat floor. Scaling with the pool is what keeps
    /// Bracing a real option rather than a slower way to lose.
    public int BraceRegenAmount(int maxHP) =>
        System.Math.Max(
            BraceRegen,
            (int)System.Math.Round(System.Math.Max(maxHP, 0) * BraceRegenFraction,
                                   System.MidpointRounding.AwayFromZero));

    /// The base damage of a Strike before variance and crit, floored at 1 so a
    /// heavily armoured target still takes chip damage rather than none.
    public double StrikeDamage(int power, int targetGuard) =>
        System.Math.Max(1, power * PowerScale - targetGuard * GuardScale);

    /// The chance a Strike lands, shaded by the Agility gap and clamped so the
    /// result is always a real gamble.
    public double HitChance(int attackerAgility, int defenderAgility)
    {
        double raw = BaseHitChance + (attackerAgility - defenderAgility) * AgilityToHit;
        return System.Math.Clamp(raw, HitChanceFloor, HitChanceCeiling);
    }

    /// The chance a landed Strike crits, from the attacker's Agility.
    public double CritChance(int agility) =>
        System.Math.Clamp(agility * CritChancePerAgility, 0, 1);
}
