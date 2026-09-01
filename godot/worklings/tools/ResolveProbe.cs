using Godot;
using Worklings.Core.Combat;

/// Compares resolved strikes against reference output captured from the Swift
/// CombatResolver. A stronger check than the RNG probe: it exercises hit rolls,
/// crit rolls, the damage swing, rounding, mitigation and HP bookkeeping
/// together, in the order the real fight draws them.
public partial class ResolveProbe : Node
{
    public override void _Ready()
    {
        var rates = new PetCombatRates();
        var attacker = new CombatStats(11, 6, 9, 7);

        var g = new SeededGenerator(4242);
        var defender = Combatant.Foe("Snag", 60, new CombatStats(8, 5, 4, 3));
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 12; i++)
        {
            var o = CombatResolver.ResolveStrike(attacker, defender, rates, ref g);
            string tag = o.DidHit ? (o.DidCrit ? "C" : "H") : "M";
            parts.Add($"{tag}{o.Damage}/{defender.CurrentHP}");
        }
        GD.Print("CS_STRIKES ", string.Join(" ", parts));

        var g2 = new SeededGenerator(77);
        var d2 = Combatant.Foe("Mote", 40, new CombatStats(5, 2, 6, 2));
        var s = CombatResolver.ResolveSignature(attacker, d2, rates, ref g2);
        GD.Print($"CS_SIG {s.Damage}/{d2.CurrentHP}");

        var g3 = new SeededGenerator(909);
        var d3 = Combatant.Foe("Wall", 100, new CombatStats(3, 9, 2, 1));
        var braced = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 6; i++)
        {
            var o = CombatResolver.ResolveStrike(
                attacker, d3, rates, ref g3, damageMultiplier: rates.BraceMitigation);
            braced.Add($"{(o.DidHit ? "H" : "M")}{o.Damage}");
        }
        GD.Print("CS_BRACED ", string.Join(" ", braced));

        GD.Print($"CS_RATES hp={rates.MaxHP(7)} dmg={rates.StrikeDamage(11, 5)} "
               + $"hit={rates.HitChance(9, 4)} crit={rates.CritChance(9)} "
               + $"regen={rates.BraceRegenAmount(43)}");
        GetTree().Quit();
    }
}
