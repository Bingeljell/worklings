using Godot;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the reward layer against reference output captured from the Swift
/// original: exit tiers at their boundaries, the condition deltas, the delve
/// gate, and the write-back that turns a finished fight into a changed pet.
///
/// The write-back cases run real seeded encounters, so a divergence here would
/// also mean the ported combat loop had drifted.
public partial class CombatRewardsProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static string F(double x) => x.ToString("F4");
    private static string B(bool b) => b ? "true" : "false";
    private static string L(ExitTier t) => t.RawValue();

    private static readonly System.DateTimeOffset Now = D("2026-09-02T10:00:00Z");

    private static PetState Mk(double xp, double h, double e, double hp, double t) =>
        new PetState("T", new PetNeeds(h, e, hp, t),
            new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle), Now,
            totalXP: xp,
            stats: new PetStats(vitality: 18, power: 14, defense: 11, agility: 9, wit: 7));

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        var rates = new PetCombatRates();

        o.AppendLine("== exit tier ==");
        foreach (bool victory in new[] { true, false })
        {
            foreach (double frac in new[] { 1.0, 0.9, 0.89, 0.5, 0.4, 0.39, 0.0 })
            {
                o.AppendLine($"victory {B(victory)} frac {F(frac)} -> "
                    + L(ExitTierExtensions.ForOutcome(victory, frac)));
            }
        }

        o.AppendLine("== condition deltas ==");
        foreach (var t in new[] { ExitTier.Flawless, ExitTier.Solid, ExitTier.Barely, ExitTier.Downed })
        {
            var d = rates.ExitConditionDelta(t);
            o.AppendLine($"{L(t)}: full {F(d.Fullness)} energy {F(d.Energy)} "
                + $"happy {F(d.Happiness)} trust {F(d.Trust)}");
        }

        o.AppendLine("== delve gate ==");
        o.AppendLine($"gateLevel {rates.DelveGateLevel} refusalThreshold {F(rates.RefusalNeedThreshold)}");
        var gateCases = new (string Label, PetState State)[]
        {
            ("level 1, healthy", Mk(0, 20, 80, 80, 80)),
            ("gate level, healthy", Mk(400, 20, 80, 80, 80)),
            ("gate level, starving", Mk(400, 95, 80, 80, 80)),
            ("gate level, exhausted", Mk(400, 20, 3, 80, 80)),
            ("gate level, miserable", Mk(400, 20, 80, 2, 80)),
            ("gate level, distrustful", Mk(400, 20, 80, 80, 1)),
            ("high level, healthy", Mk(5000, 10, 90, 90, 90)),
        };
        foreach (var (label, state) in gateCases)
        {
            var block = rates.DelveBlockFor(state);
            string desc = block is null ? "none"
                : block.Value.Kind == DelveBlockKind.BelowGateLevel
                    ? $"belowGateLevel({block.Value.Required})"
                    : "needsCare";
            o.AppendLine($"{label} [L{state.Level}]: {desc} canEnter {B(rates.CanEnterDelve(state))}");
        }

        o.AppendLine("== applying outcome ==");
        var fights = new (string Label, ulong Seed, Foe Foe)[]
        {
            ("scamp", 1, CacheWarren.Mote),
            ("snag", 7, CacheWarren.Snag),
            ("flicker", 4, CacheWarren.Flicker),
            ("monolith", 3, CacheWarren.Boss),
        };
        foreach (var (label, seed, foe) in fights)
        {
            var pet = Mk(500, 30, 70, 70, 70);
            var enc = new CombatEncounter(
                Combatant.Pet(pet, rates), foe, Approach.Careful, rates, seed);
            enc.RunToCompletion();
            var r = pet.ApplyingOutcome(enc, foe, rates);
            o.AppendLine($"{label}: status {Status(enc)} hpFrac {F(enc.Pet.HPFraction)} "
                + $"tier {L(r.Tier)} xp {F(r.XPGained)}");
            o.AppendLine($"  needs H{F(r.State.Needs.Hunger)} E{F(r.State.Needs.Energy)} "
                + $"Hp{F(r.State.Needs.Happiness)} T{F(r.State.Needs.Trust)} "
                + $"totalXP {F(r.State.TotalXP)}");
        }

        o.AppendLine("== clamping on write-back ==");
        var fragile = Mk(500, 95, 5, 5, 3);
        var enc2 = new CombatEncounter(
            Combatant.Pet(fragile, rates), CacheWarren.Boss, Approach.Aggressive, rates, 99);
        enc2.RunToCompletion();
        var r2 = fragile.ApplyingOutcome(enc2, CacheWarren.Boss, rates);
        o.AppendLine($"tier {L(r2.Tier)} needs H{F(r2.State.Needs.Hunger)} "
            + $"E{F(r2.State.Needs.Energy)} Hp{F(r2.State.Needs.Happiness)} T{F(r2.State.Needs.Trust)}");

        o.AppendLine("== gear survives the write-back ==");
        var geared = Mk(500, 30, 70, 70, 70)
            .Acquiring(Item.MastersHone).Equipping(Item.MastersHone);
        var enc3 = new CombatEncounter(
            Combatant.Pet(geared, rates), CacheWarren.Mote, Approach.Careful, rates, 11);
        enc3.RunToCompletion();
        var r3 = geared.ApplyingOutcome(enc3, CacheWarren.Mote, rates);
        var owned = new System.Collections.Generic.List<string>();
        foreach (var i in r3.State.OwnedItems) owned.Add(i.RawValue());
        o.AppendLine($"owned {string.Join(",", owned)} tool "
            + (r3.State.Loadout.Tool.HasValue ? r3.State.Loadout.Tool.Value.RawValue() : "nil"));

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private static string Status(CombatEncounter e) =>
        e.Status.Kind switch
        {
            CombatStatus.StatusKind.PetVictory => "petVictory",
            CombatStatus.StatusKind.PetDefeat => "petDefeat",
            CombatStatus.StatusKind.AwaitingDecision => "awaitingDecision",
            _ => "ongoing",
        };
}
