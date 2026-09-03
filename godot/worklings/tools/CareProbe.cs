using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the care simulation against reference output captured from the Swift
/// original: needs decaying over time, what each action does to them, and the XP
/// the action earns after the caps and the condition multiplier have had their
/// say.
///
/// The fixtures that matter are the ones where two plausible implementations
/// diverge: a **negative** elapsed time (a clock change), the **offline cap** at
/// a week, the **distress** terms that only switch on past their thresholds, the
/// energy **boundary** either side of too-tired-to-play, the **daily cap** that
/// silently stops paying at 60, a grant that crosses a **level** threshold, and
/// gear surviving a care action — which it did not, once.
public partial class CareProbe : Node
{
    private static readonly System.DateTimeOffset Base = PetStateCodec.FromSwiftDate(800_000_000);
    private System.Text.StringBuilder o = new();

    private static string F(double v) =>
        v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

    private void Show(string label, PetState s) =>
        o.AppendLine($"{label}: needs={F(s.Needs.Hunger)} {F(s.Needs.Energy)} "
                   + $"{F(s.Needs.Happiness)} {F(s.Needs.Trust)} xp={F(s.TotalXP)} lv={s.Level} "
                   + $"stats={s.Stats.Vitality} {s.Stats.Power} {s.Stats.Defense} "
                   + $"{s.Stats.Agility} {s.Stats.Wit} items={s.OwnedItems.Count}");

    private static PetState Pet(
        double hunger = 20, double energy = 80, double happiness = 70, double trust = 50,
        double xp = 0, System.DateTimeOffset? at = null) =>
        new PetState(
            name: "Fixture",
            family: PetFamily.Wildkin,
            needs: new PetNeeds(hunger, energy, happiness, trust),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: at ?? Base,
            totalXP: xp);

    public override void _Ready()
    {
        var brain = new PetBrain();

        o.AppendLine("== advance ==");
        foreach (double hours in new double[] { 0, -3, 0.5, 3, 12, 48, 720 })
        {
            Show($"+{F(hours)}h", brain.Advance(Pet(), Base.AddSeconds(hours * 3600)));
        }
        foreach (double hours in new double[] { 1, 6, 24 })
        {
            Show($"distressed +{F(hours)}h",
                 brain.Advance(Pet(80, 10, 60, 40), Base.AddSeconds(hours * 3600)));
        }

        o.AppendLine("== perform ==");
        foreach (var (label, action) in new (string, PetAction)[]
                 {
                     ("feed favourite", PetAction.Feed(PetFood.Berries)),
                     ("feed other", PetAction.Feed(PetFood.Noodles)),
                     ("play favourite", PetAction.Playing(PetPlayActivity.Puzzle)),
                     ("play other", PetAction.Playing(PetPlayActivity.Chase)),
                     ("pet", PetAction.Pet),
                     ("sleep", PetAction.Sleep),
                 })
        {
            var r = brain.Perform(action, Pet(), Base);
            Show($"{label} [{r.Reaction.RawValue()}]", r.State);
        }
        foreach (double energy in new double[] { 14, 15, 16 })
        {
            var r = brain.Perform(PetAction.Playing(PetPlayActivity.Puzzle), Pet(20, energy), Base);
            Show($"play at energy {F(energy)} [{r.Reaction.RawValue()}]", r.State);
        }
        var drifted = brain.Perform(PetAction.Pet, Pet(), Base.AddSeconds(5 * 3600));
        Show($"pet after 5h [{drifted.Reaction.RawValue()}]", drifted.State);

        o.AppendLine("== care XP, caps and condition ==");
        foreach (var (label, p) in new (string, PetState)[]
                 {
                     ("healthy", Pet(0, 100, 100, 100)),
                     ("poor", Pet(95, 5, 5, 5)),
                 })
        {
            Show($"{label} one pet", brain.Perform(PetAction.Pet, p, Base).State);
        }
        var capped = Pet(0, 100, 100, 100);
        for (int i = 1; i <= 25; i++)
        {
            capped = brain.Perform(PetAction.Pet, capped, Base).State;
            if (i % 5 == 0) Show($"pet x{i}", capped);
        }
        var nearLevel = Pet(0, 100, 100, 100, xp: 99);
        Show("before", nearLevel);
        Show("after", brain.Perform(PetAction.Pet, nearLevel, Base).State);

        o.AppendLine("== gear survives a care action ==");
        var geared = new PetState(
            name: "Geared",
            needs: new PetNeeds(20, 80, 70, 50),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: Base,
            ownedItems: new[] { Item.RubberDuck, Item.MastersHone },
            loadout: new Loadout(tool: Item.MastersHone, charm: Item.RubberDuck));
        var afterCare = brain.Perform(PetAction.Feed(PetFood.Berries), geared, Base).State;
        Show("geared after feed", afterCare);
        o.AppendLine("loadout: " + string.Join(",", System.Array.ConvertAll(
            ItemSlotExtensions.AllCases,
            slot => $"{slot.RawValue()}:{(afterCare.Loadout[slot]?.RawValue() ?? "-")}")));

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
