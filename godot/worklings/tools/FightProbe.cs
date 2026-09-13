using Godot;
using System.Collections.Generic;
using System.Linq;
using Worklings.Core.Combat;

/// Runs whole encounters and prints the complete event log.
///
/// Every round of four fights across all four foe archetypes — mindless,
/// grabber, evasive and colossus — including the declared intents, the status
/// effects and the resolutions. A single divergence anywhere reshuffles
/// everything after it, so a matching log end-to-end is strong evidence.
///
/// **This stopped being a port check on 2026-09-13.** It existed to compare the
/// C# port against `Sources/CompanionCore/CombatEncounter.swift` line for line,
/// and the C# rules have since deliberately moved ahead of the Swift ones — the
/// player picks a move every round, the foe declares its intent up front, and
/// the pet always acts first. Swift is legacy; the engine is Godot. So the
/// reference is now a regression baseline against this file's own past, which is
/// still worth having and is a weaker claim than it used to be.
public partial class FightProbe : Node
{
    private static string Describe(CombatEvent e) => e switch
    {
        CombatEvent.EncounterBegan x => $"begin({x.Pet},{x.Foe})",
        CombatEvent.RoundBegan x => $"round({x.Round})",
        CombatEvent.Struck x =>
            $"struck({x.Attacker}->{x.Defender},{(x.Outcome.DidHit ? (x.Outcome.DidCrit ? "crit" : "hit") : "miss")},{x.Outcome.Damage})",
        CombatEvent.Signature x => $"sig({x.Attacker}->{x.Defender},{x.Outcome.Damage})",
        CombatEvent.Braced x => $"brace({x.Who},{x.Regen})",
        CombatEvent.Grabbed x => $"grab({x.Attacker}->{x.Target},{x.AgilityLoss})",
        CombatEvent.Phased x => $"phase({x.Who})",
        CombatEvent.Telegraphed x => $"tele({x.Who})",
        CombatEvent.Slammed x => $"slam({x.Attacker}->{x.Defender},{x.Outcome.Damage})",
        CombatEvent.Hardened x => $"harden({x.Who},{x.GuardGain})",
        CombatEvent.Defeated x => $"dead({x.Who})",
        CombatEvent.AwaitingAction x => $"intends({Lower(x.Intent.Kind)})",
        CombatEvent.EncounterEnded x => $"end({(x.Victory ? "true" : "false")})",
        _ => "?",
    };

    private static string Lower(FoeIntentKind k) => k switch
    {
        FoeIntentKind.Strike => "strike",
        FoeIntentKind.WindUp => "windUp",
        FoeIntentKind.Slam => "slam",
        FoeIntentKind.Grab => "grab",
        FoeIntentKind.Phase => "phase",
        _ => "?",
    };

    public override void _Ready()
    {
        var rates = new PetCombatRates();
        var petStats = new CombatStats(11, 6, 9, 7);

        var cases = new (string Label, Foe Foe, ulong Seed)[]
        {
            ("scamp", CacheWarren.Mote, 1001UL),
            ("snag", CacheWarren.Snag, 2002UL),
            ("flicker", CacheWarren.Flicker, 3003UL),
            ("monolith", CacheWarren.Monolith, 4004UL),
        };

        foreach (var c in cases)
        {
            var pet = new Combatant("Ram", petStats, 41, 41);
            var enc = new CombatEncounter(pet, c.Foe, rates, c.Seed);
            enc.RunToCompletion();
            GD.Print($"CS_FIGHT {c.Label} rounds={enc.Round} petHP={enc.Pet.CurrentHP} "
                   + $"foeHP={enc.Foe.CurrentHP} events={enc.Log.Count}");
            GD.Print($"CS_LOG {c.Label} {string.Join(" ", enc.Log.Select(Describe))}");
        }
        GetTree().Quit();
    }
}
