using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// A single encounter: the pet versus one foe, resolved round by round against
/// the seeded stream. Deterministic — the same seed and inputs replay the same
/// fight — so it is fully checkable without a renderer.
///
/// **A round is two calls.** `Step()` opens one — it ages statuses, rolls the
/// foe's intent, and stops, leaving `Status` at `AwaitingAction` with the intent
/// readable on `Intent`. `Act(action)` then resolves it: the pet performs the
/// verb the player pressed, and the foe performs the intent it already
/// declared. `RunToCompletion()` is the headless convenience and supplies its
/// own actions.
///
/// **This shape replaced an auto-resolving one on 2026-09-13**, on the first
/// play session's feedback, and the change is a design change rather than a
/// refactor:
///
///   * **The player picks a verb, not a stance.** `Approach` — Aggressive /
///     Careful / Clever — derived the pet's action from a standing strategy, and
///     is gone. It was odd to play and it lied by omission: Careful meant "brace
///     *if* hurt", so choosing it and then watching a strike read as the game
///     ignoring the input.
///   * **The foe declares before the player commits.** Its move is rolled at the
///     top of the round and published, so bracing a Monolith slam is a decision
///     rather than a guess. The performance is bound by the declaration.
///   * **The pet always acts first.** Initiative by Agility meant the foe could
///     open an encounter before the player had seen the screen. Agility still
///     matters everywhere it did except turn order, and Snare's Agility drain
///     now costs accuracy and evasion rather than the turn.
///   * **Every round waits.** There is no cadence, no low-HP prompt and no
///     telegraph prompt, because every round is all three.
///
/// It therefore **no longer matches `Sources/CompanionCore/CombatEncounter.swift`**,
/// which still holds the auto-resolving rules. The Swift core is legacy — the
/// engine is Godot — so the C# has deliberately moved ahead of it, and
/// `tools/FightProbe` stops being a port check against Swift and becomes a
/// regression check against itself.
public sealed class CombatEncounter
{
    public Combatant Pet { get; }
    public Combatant Foe { get; }
    public int Round { get; private set; }
    public CombatStatus Status { get; private set; }

    /// What the foe will do this round. Meaningful once `Step()` has opened a
    /// round and until `Act(...)` has resolved it.
    public FoeIntent Intent { get; private set; }

    private readonly List<CombatEvent> _log = new();
    public IReadOnlyList<CombatEvent> Log => _log;

    private readonly PetCombatRates _rates;
    private readonly FoeBehavior _foeBehavior;
    private SeededGenerator _generator;
    private bool _signatureAvailable;

    /// Rounds remaining before a grabber (Snag) may Snare again.
    private int _grabCooldownRemaining;
    /// Rounds remaining before an evasive foe may Phase-and-open again.
    private int _openingCooldownRemaining;
    /// Foe turns until a telegraphed Slam lands (0 = not winding up).
    private int _slamCountdown;
    /// How many HP-phase Harden thresholds have already fired.
    private int _hardenPhasesApplied;

    public CombatEncounter(Combatant pet, Foe foe, PetCombatRates rates, ulong seed)
    {
        Pet = pet;
        Foe = foe.MakeCombatant();
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

    /// Opens a round and stops, waiting for the player.
    ///
    /// Ages the timed effects, rolls what the foe is going to do, and publishes
    /// it. A no-op once the fight is already waiting or over.
    public void Step()
    {
        if (!Status.IsOngoing) return;

        Round += 1;
        _log.Add(new CombatEvent.RoundBegan(Round));

        // Age any timed effects (Snare, Blur, Phase, Harden) at the top of the
        // round, before anyone acts, and drop the expired ones.
        Pet.TickStatuses();
        Foe.TickStatuses();

        Intent = RollFoeIntent();
        Status = CombatStatus.AwaitingAction;
        _log.Add(new CombatEvent.AwaitingAction(Intent));
    }

    /// Resolves the open round with the verb the player pressed.
    ///
    /// The pet goes first, always — see the type docs. A Signature asked for
    /// when it is already spent falls back to a Strike rather than wasting the
    /// round, because the bar draws that slot as spent and a player who presses
    /// it anyway has misread the screen, not chosen to skip a turn.
    public void Act(CombatAction action)
    {
        if (!Status.IsAwaitingAction) return;
        Status = CombatStatus.Ongoing;
        if (action == CombatAction.Signature && !_signatureAvailable) action = CombatAction.Strike;

        PerformPet(action);
        if (Status.IsOngoing) PerformFoe(Intent, petIsBracing: action == CombatAction.Brace);
    }

    /// Runs the fight to an ending without further input, choosing for itself.
    /// For headless use, checks, and the unattended capture tool.
    public void RunToCompletion(int maxRounds = 200)
    {
        int safety = 0;
        int limit = maxRounds * 4;
        while (safety < limit)
        {
            if (Status.IsOngoing) Step();
            else if (Status.IsAwaitingAction) Act(AutoAction());
            else return;
            safety += 1;
        }
    }

    /// What an unattended run does with its turn.
    ///
    /// Deliberately a policy a person might actually play rather than "always
    /// Strike": it reads the declared intent, which is the whole point of the
    /// intent existing. Brace into a slam, finish with the Signature when the
    /// foe is inside range, otherwise hit it.
    public CombatAction AutoAction()
    {
        if (Intent.Kind == FoeIntentKind.Slam && Pet.HPFraction < 0.6) return CombatAction.Brace;
        if (_signatureAvailable && Foe.HPFraction <= _rates.CleverFinisherThreshold)
            return CombatAction.Signature;
        if (Pet.HPFraction < _rates.CarefulBraceThreshold && Intent.Kind != FoeIntentKind.WindUp)
            return CombatAction.Brace;
        return CombatAction.Strike;
    }

    /// Whether the foe's declared move can be blunted by bracing. Presentation
    /// asks this to colour the intent as a threat or as a breather.
    public bool IntentThreatens =>
        Intent.Kind is FoeIntentKind.Strike or FoeIntentKind.Slam or FoeIntentKind.Phase;

    // MARK: - Internals

    /// Decides the foe's move for this round, rolling every die it needs.
    ///
    /// **All the randomness for the foe's turn happens here**, so the icon over
    /// its head is a promise rather than a forecast. `PerformFoe` then has no
    /// choices left to make — it only carries out what was declared. The one
    /// thing left out is Harden, which is not a move: it is a passive that fires
    /// when the foe's HP crosses a threshold, and it can be crossed by the pet's
    /// own blow after the intent was rolled.
    private FoeIntent RollFoeIntent()
    {
        switch (_foeBehavior)
        {
            case FoeBehavior.Colossus c:
                if (_slamCountdown > 0)
                {
                    _slamCountdown -= 1;
                    if (_slamCountdown == 0) return new FoeIntent(FoeIntentKind.Slam);
                    return new FoeIntent(FoeIntentKind.WindUp);
                }
                _slamCountdown = System.Math.Max(1, c.TelegraphRounds);
                return new FoeIntent(FoeIntentKind.WindUp);

            case FoeBehavior.Grabber g:
                if (_grabCooldownRemaining > 0)
                {
                    _grabCooldownRemaining -= 1;
                    return new FoeIntent(FoeIntentKind.Strike);
                }
                if (_generator.Chance(g.SnareChance))
                {
                    _grabCooldownRemaining = g.GrabCooldown;
                    return new FoeIntent(FoeIntentKind.Grab);
                }
                return new FoeIntent(FoeIntentKind.Strike);

            case FoeBehavior.Evasive e:
                if (_openingCooldownRemaining > 0)
                {
                    _openingCooldownRemaining -= 1;
                    return new FoeIntent(FoeIntentKind.Strike);
                }
                if (_generator.Chance(e.PhaseChance))
                {
                    _openingCooldownRemaining = e.OpeningCooldown;
                    return new FoeIntent(FoeIntentKind.Phase);
                }
                return new FoeIntent(FoeIntentKind.Strike);

            default:
                return new FoeIntent(FoeIntentKind.Strike);
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

    /// Carries out the move the foe declared at the top of the round. No rolls
    /// here beyond the strike resolution itself — the choice was already made.
    private void PerformFoe(FoeIntent intent, bool petIsBracing)
    {
        // Harden is the exception: a passive that fires on an HP threshold the
        // pet may only just have pushed it past, so it is checked now rather
        // than declared.
        if (_foeBehavior is FoeBehavior.Colossus colossus)
        {
            ApplyHardenIfCrossed(colossus.HardenThresholds, colossus.HardenGuard);
        }

        switch (intent.Kind)
        {
            case FoeIntentKind.WindUp:
                _log.Add(new CombatEvent.Telegraphed(Foe.Name));
                break;

            case FoeIntentKind.Slam:
                ExecuteSlam(((FoeBehavior.Colossus)_foeBehavior).SlamMultiplier, petIsBracing);
                break;

            case FoeIntentKind.Grab:
            {
                var grabber = (FoeBehavior.Grabber)_foeBehavior;
                Pet.Apply(new StatusEffect(
                    StatusEffectKind.AgilityDebuff, grabber.SnareMagnitude,
                    remainingRounds: grabber.SnareDuration));
                _log.Add(new CombatEvent.Grabbed(Foe.Name, Pet.Name, grabber.SnareMagnitude));
                break;
            }

            case FoeIntentKind.Phase:
                FoeStrike(petIsBracing);
                if (Status.IsOngoing || !Foe.IsDefeated)
                {
                    Foe.Apply(new StatusEffect(StatusEffectKind.Phasing, 0, remainingRounds: 2));
                    _log.Add(new CombatEvent.Phased(Foe.Name));
                }
                break;

            default:
                FoeStrike(petIsBracing);
                break;
        }
        ResolveDefeatIfAny();
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
