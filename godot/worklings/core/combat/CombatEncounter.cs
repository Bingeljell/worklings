using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// A single encounter: the pet versus one foe, resolved round by round against
/// the seeded stream. Deterministic — the same seed and inputs replay the same
/// fight — so it is fully checkable without a renderer.
///
/// Drive it by calling Step() until Status is a decision or an ending. On a
/// decision, call Decide(...); on an ending, read Pet.HPFraction for the delve's
/// exit tier. RunToCompletion() is the headless convenience.
///
/// Ported from Sources/CompanionCore/CombatEncounter.swift. Swift's version is a
/// struct with `mutating` methods; this is a class, because the encounter is a
/// long-lived object the renderer holds a reference to and steps.
public sealed class CombatEncounter
{
    public Combatant Pet { get; }
    public Combatant Foe { get; }
    public Approach Approach { get; private set; }
    public int Round { get; private set; }
    public CombatStatus Status { get; private set; }

    private readonly List<CombatEvent> _log = new();
    public IReadOnlyList<CombatEvent> Log => _log;

    private readonly PetCombatRates _rates;
    private readonly FoeBehavior _foeBehavior;
    private SeededGenerator _generator;
    private bool _signatureAvailable;
    private bool _pendingSignature;
    private bool _promptedLowHP;
    private int _lastCadenceRound;

    /// Rounds remaining before a grabber (Snag) may Snare again.
    private int _grabCooldownRemaining;
    /// Set when an evasive foe over-extends, so the next decision is the Unleash
    /// opening; cleared once that decision is taken.
    private bool _openingPending;
    /// Rounds remaining before an evasive foe may Phase-and-open again.
    private int _openingCooldownRemaining;
    /// Foe turns until a telegraphed Slam lands (0 = not winding up).
    private int _slamCountdown;
    /// Set when a colossus telegraphs, so the next decision is the Brace-or-eat
    /// prompt; cleared once that decision is taken.
    private bool _slamTelegraphPending;
    /// How many HP-phase Harden thresholds have already fired.
    private int _hardenPhasesApplied;
    /// A one-shot guaranteed Brace queued from a telegraph decision.
    private bool _pendingBrace;

    /// Whether a Careful pet is currently latched into Bracing. Held as state,
    /// not re-derived each round, because the threshold to ENTER the latch and
    /// the one to leave it deliberately differ.
    private bool _carefulBracing;

    /// Whether the last Careful action inside the hurt band was a Brace, so the
    /// band alternates Brace/Strike rather than bracing forever.
    private bool _carefulBracedLastRound;

    public CombatEncounter(Combatant pet, Foe foe, Approach approach, PetCombatRates rates, ulong seed)
    {
        Pet = pet;
        Foe = foe.MakeCombatant();
        Approach = approach;
        Round = 0;
        Status = CombatStatus.Ongoing;
        _rates = rates;
        _foeBehavior = foe.Behavior;
        _generator = new SeededGenerator(seed);
        _signatureAvailable = true;
        _log.Add(new CombatEvent.EncounterBegan(pet.Name, Foe.Name));

        // Blur is a passive: an evasive foe carries its evasion for the whole
        // fight, on top of its native Agility.
        if (_foeBehavior is FoeBehavior.Evasive evasive)
        {
            Foe.Apply(new StatusEffect(StatusEffectKind.Evasion, evasive.Evasion, isPermanent: true));
        }
    }

    /// Whether the pet still has its once-per-encounter Signature.
    public bool SignatureReady => _signatureAvailable;

    /// Advances the fight by one unit: either pausing for a decision, or
    /// resolving a full round (both combatants act, in initiative order). A
    /// no-op once the fight is awaiting a decision or over.
    public void Step()
    {
        if (!Status.IsOngoing) return;
        var reason = PendingDecision();
        if (reason.HasValue)
        {
            Status = CombatStatus.AwaitingDecision(reason.Value);
            _log.Add(new CombatEvent.DecisionPoint(reason.Value));
            return;
        }
        ResolveRound();
    }

    /// Resolves a pending decision: adopt an Approach, and optionally Unleash the
    /// Signature on the next round. A no-op unless a decision is pending.
    public void Decide(Approach approach, bool unleash)
    {
        if (!Status.IsAwaitingDecision) return;
        var reason = Status.Reason;
        Approach = approach;
        if (unleash && _signatureAvailable) _pendingSignature = true;

        switch (reason)
        {
            case DecisionReason.LowHP: _promptedLowHP = true; break;
            case DecisionReason.Cadence: _lastCadenceRound = Round; break;
            case DecisionReason.Opening: _openingPending = false; break;
            case DecisionReason.Telegraph:
                _slamTelegraphPending = false;
                // Choosing Careful into a telegraph is a deliberate Brace against
                // the incoming Slam, not the usual hurt-only Brace.
                if (approach == Approach.Careful && !unleash) _pendingBrace = true;
                break;
        }
        Status = CombatStatus.Ongoing;
    }

    /// Runs the fight to an ending without further input, keeping the current
    /// Approach at every decision. For headless use and checks.
    public void RunToCompletion(int maxRounds = 200)
    {
        int safety = 0;
        int limit = maxRounds * 4;
        while (safety < limit)
        {
            if (Status.IsOngoing) Step();
            else if (Status.IsAwaitingDecision) Decide(Approach, unleash: false);
            else return;
            safety += 1;
        }
    }

    // MARK: - Internals

    private DecisionReason? PendingDecision()
    {
        if (!_promptedLowHP && Pet.HPFraction < _rates.LowHPEventThreshold)
            return DecisionReason.LowHP;
        if (_slamTelegraphPending) return DecisionReason.Telegraph;
        if (_openingPending) return DecisionReason.Opening;
        if (Round > 0 && Round % _rates.DecisionCadenceRounds == 0 && _lastCadenceRound != Round)
            return DecisionReason.Cadence;
        return null;
    }

    private void ResolveRound()
    {
        Round += 1;
        _log.Add(new CombatEvent.RoundBegan(Round));

        // Age any timed effects (Snare, Blur, Phase, Harden) at the top of the
        // round, before anyone acts, and drop the expired ones.
        Pet.TickStatuses();
        Foe.TickStatuses();

        var petAction = ChosenPetAction();
        bool bracing = petAction == CombatAction.Brace;

        // Higher Agility acts first; the pet wins ties. Reads effective Agility so
        // a Snare (which sags initiative) actually costs the pet its turn order.
        bool petFirst = Pet.EffectiveStats.Agility >= Foe.EffectiveStats.Agility;
        if (petFirst)
        {
            PerformPet(petAction);
            if (Status.IsOngoing) PerformFoe(bracing);
        }
        else
        {
            PerformFoe(bracing);
            if (Status.IsOngoing) PerformPet(petAction);
        }
    }

    private CombatAction ChosenPetAction()
    {
        if (_pendingBrace)
        {
            _pendingBrace = false;
            return CombatAction.Brace;
        }
        if (_pendingSignature)
        {
            _pendingSignature = false;
            if (_signatureAvailable) return CombatAction.Signature;
        }

        switch (Approach)
        {
            case Approach.Aggressive:
                return CombatAction.Strike;

            case Approach.Careful:
                // Enter the hurt band when low, leave it only once genuinely
                // recovered — two thresholds, so it isn't a one-way door.
                _carefulBracing = _carefulBracing
                    ? Pet.HPFraction <= _rates.CarefulResumeThreshold
                    : Pet.HPFraction < _rates.CarefulBraceThreshold;
                if (!_carefulBracing)
                {
                    _carefulBracedLastRound = false;
                    return CombatAction.Strike;
                }
                // Inside the band, Brace and Strike ALTERNATE. Bracing every round
                // was the actual death spiral: against anything that outdamages
                // the regen the pet could neither heal out of the band nor hurt
                // what was holding it there, so the fight became unwinnable the
                // moment it dipped — and unwatchable, since the foe was the only
                // one acting.
                _carefulBracedLastRound = !_carefulBracedLastRound;
                return _carefulBracedLastRound ? CombatAction.Brace : CombatAction.Strike;

            case Approach.Clever:
                // The held Signature, spent the moment the foe is inside finishing
                // range — the "chosen moment" the Approach is named for.
                if (_signatureAvailable && Foe.HPFraction <= _rates.CleverFinisherThreshold)
                    return CombatAction.Signature;
                return CombatAction.Strike;

            default:
                return CombatAction.Strike;
        }
    }

    private void PerformPet(CombatAction action)
    {
        switch (action)
        {
            case CombatAction.Strike:
            {
                var outcome = CombatResolver.ResolveStrike(
                    Pet.EffectiveStats, Foe, _rates, ref _generator);
                _log.Add(new CombatEvent.Struck(Pet.Name, Foe.Name, outcome));
                break;
            }
            case CombatAction.Brace:
            {
                int regen = _rates.BraceRegenAmount(Pet.MaxHP);
                Pet.Heal(regen);
                _log.Add(new CombatEvent.Braced(Pet.Name, regen));
                break;
            }
            case CombatAction.Signature:
            {
                _signatureAvailable = false;
                var outcome = CombatResolver.ResolveSignature(
                    Pet.EffectiveStats, Foe, _rates, ref _generator);
                _log.Add(new CombatEvent.Signature(Pet.Name, Foe.Name, outcome));
                break;
            }
        }
        ResolveDefeatIfAny();
    }

    private void PerformFoe(bool petIsBracing)
    {
        // Dispatch on the foe's archetype. Each special behaviour lands in its own
        // slice; until then every foe simply Strikes.
        switch (_foeBehavior)
        {
            case FoeBehavior.Mindless:
                FoeStrike(petIsBracing);
                break;
            case FoeBehavior.Colossus c:
                PerformColossus(c.SlamMultiplier, c.TelegraphRounds,
                                c.HardenThresholds, c.HardenGuard, petIsBracing);
                break;
            case FoeBehavior.Grabber g:
                PerformGrab(g.SnareChance, g.SnareMagnitude, g.SnareDuration,
                            g.GrabCooldown, petIsBracing);
                break;
            case FoeBehavior.Evasive e:
                PerformEvasive(e.PhaseChance, e.OpeningCooldown, petIsBracing);
                break;
        }
        ResolveDefeatIfAny();
    }

    /// An evasive foe (Flicker): it always darts in for chip damage, and — off
    /// cooldown — sometimes Phases, slipping the pet's next blow and
    /// over-extending into an Unleash opening. The opening only arms while the
    /// Signature is still in hand, since that is the whole point of the window.
    private void PerformEvasive(double phaseChance, int openingCooldown, bool petIsBracing)
    {
        FoeStrike(petIsBracing);
        if (_openingCooldownRemaining > 0)
        {
            _openingCooldownRemaining -= 1;
        }
        else if (_generator.Chance(phaseChance))
        {
            Foe.Apply(new StatusEffect(StatusEffectKind.Phasing, 0, remainingRounds: 2));
            _log.Add(new CombatEvent.Phased(Foe.Name));
            if (_signatureAvailable) _openingPending = true;
            _openingCooldownRemaining = openingCooldown;
        }
    }

    /// A grabber (Snag): off cooldown, it may seize the pet instead of striking,
    /// Snaring its Agility for a few rounds; otherwise it just attacks. The grab
    /// is spaced by a cooldown so it cannot lock the pet down every turn.
    private void PerformGrab(
        double snareChance, int snareMagnitude, int snareDuration,
        int grabCooldown, bool petIsBracing)
    {
        if (_grabCooldownRemaining > 0)
        {
            _grabCooldownRemaining -= 1;
            FoeStrike(petIsBracing);
            return;
        }
        if (_generator.Chance(snareChance))
        {
            Pet.Apply(new StatusEffect(
                StatusEffectKind.AgilityDebuff, snareMagnitude, remainingRounds: snareDuration));
            _grabCooldownRemaining = grabCooldown;
            _log.Add(new CombatEvent.Grabbed(Foe.Name, Pet.Name, snareMagnitude));
        }
        else
        {
            FoeStrike(petIsBracing);
        }
    }

    /// A colossus (Monolith): slow but heavy. It Hardens as its HP crosses phase
    /// thresholds, and instead of ordinary attacks it winds up a telegraphed Slam
    /// one turn, then lands it — a guaranteed, doubled hit — the next.
    private void PerformColossus(
        double slamMultiplier, int telegraphRounds,
        IReadOnlyList<double> hardenThresholds, int hardenGuard, bool petIsBracing)
    {
        ApplyHardenIfCrossed(hardenThresholds, hardenGuard);
        if (_slamCountdown > 0)
        {
            _slamCountdown -= 1;
            if (_slamCountdown == 0) ExecuteSlam(slamMultiplier, petIsBracing);
            // Otherwise it is still winding up and does not attack this turn.
        }
        else
        {
            _slamCountdown = System.Math.Max(1, telegraphRounds);
            _slamTelegraphPending = true;
            _log.Add(new CombatEvent.Telegraphed(Foe.Name));
        }
    }

    /// The wound-up Slam: a guaranteed hit at the Slam multiplier, halved if the
    /// pet Braced the blow.
    private void ExecuteSlam(double multiplier, bool petIsBracing)
    {
        var outcome = CombatResolver.ResolveStrike(
            Foe.EffectiveStats, Pet, _rates, ref _generator,
            damageMultiplier: multiplier * (petIsBracing ? _rates.BraceMitigation : 1),
            guaranteedHit: true);
        _log.Add(new CombatEvent.Slammed(Foe.Name, Pet.Name, outcome));
    }

    /// Applies each Harden threshold once, in order, as the foe's HP drops past
    /// it — a single big hit can cross several at once.
    private void ApplyHardenIfCrossed(IReadOnlyList<double> thresholds, int guardGain)
    {
        while (_hardenPhasesApplied < thresholds.Count
               && Foe.HPFraction <= thresholds[_hardenPhasesApplied])
        {
            Foe.Apply(new StatusEffect(StatusEffectKind.GuardBuff, guardGain, isPermanent: true));
            _log.Add(new CombatEvent.Hardened(Foe.Name, guardGain));
            _hardenPhasesApplied += 1;
        }
    }

    /// The foe's plain attack — the baseline every archetype falls back to.
    private void FoeStrike(bool petIsBracing)
    {
        var outcome = CombatResolver.ResolveStrike(
            Foe.EffectiveStats, Pet, _rates, ref _generator,
            damageMultiplier: petIsBracing ? _rates.BraceMitigation : 1);
        _log.Add(new CombatEvent.Struck(Foe.Name, Pet.Name, outcome));
    }

    private void ResolveDefeatIfAny()
    {
        if (!Status.IsOngoing) return;
        if (Foe.IsDefeated)
        {
            _log.Add(new CombatEvent.Defeated(Foe.Name));
            _log.Add(new CombatEvent.EncounterEnded(true));
            Status = CombatStatus.PetVictory;
        }
        else if (Pet.IsDefeated)
        {
            _log.Add(new CombatEvent.Defeated(Pet.Name));
            _log.Add(new CombatEvent.EncounterEnded(false));
            Status = CombatStatus.PetDefeat;
        }
    }
}
