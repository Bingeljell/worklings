using Godot;
using System.Collections.Generic;
using System.Linq;
using Worklings.Core.Combat;

/// Runs whole encounters and prints the complete event log, for comparison
/// against the same fights resolved by the Swift CombatEncounter.
///
/// This is the real check on the port: not individual draws, but every round of
/// four fights across all four foe archetypes — mindless, grabber, evasive and
/// colossus — including decision points, status effects, initiative order and
/// the Careful hysteresis. A single divergence anywhere reshuffles everything
/// after it, so matching logs end-to-end is strong evidence.
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
        CombatEvent.DecisionPoint x => $"decide({Lower(x.Reason)})",
        CombatEvent.EncounterEnded x => $"end({(x.Victory ? "true" : "false")})",
        _ => "?",
    };

    /// Swift prints enum cases lowerCamelCase; match that so the logs compare
    /// as plain text.
    private static string Lower(DecisionReason r) => r switch
    {
        DecisionReason.Cadence => "cadence",
        DecisionReason.LowHP => "lowHP",
        DecisionReason.Opening => "opening",
        DecisionReason.Telegraph => "telegraph",
        _ => "?",
    };

    public override void _Ready()
    {
        var rates = new PetCombatRates();
        var petStats = new CombatStats(11, 6, 9, 7);

        var cases = new (string Label, Foe Foe, Approach Approach, ulong Seed)[]
        {
            ("scamp-aggr", CacheWarren.Mote, Approach.Aggressive, 1001UL),
            ("snag-careful", CacheWarren.Snag, Approach.Careful, 2002UL),
            ("flicker-clever", CacheWarren.Flicker, Approach.Clever, 3003UL),
            ("monolith-careful", CacheWarren.Monolith, Approach.Careful, 4004UL),
        };

        foreach (var c in cases)
        {
            var pet = new Combatant("Ram", petStats, 41, 41);
            var enc = new CombatEncounter(pet, c.Foe, c.Approach, rates, c.Seed);
            enc.RunToCompletion();
            GD.Print($"CS_FIGHT {c.Label} rounds={enc.Round} petHP={enc.Pet.CurrentHP} "
                   + $"foeHP={enc.Foe.CurrentHP} events={enc.Log.Count}");
            GD.Print($"CS_LOG {c.Label} {string.Join(" ", enc.Log.Select(Describe))}");
        }
        GetTree().Quit();
    }
}
