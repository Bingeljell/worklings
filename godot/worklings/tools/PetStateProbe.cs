using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using System.Collections.Generic;

/// Compares PetState against reference output captured from the Swift original.
///
/// The constructor is the risky part: it dedupes owned items, rejects equipped
/// items the Workling doesn't own, floors XP, and defaults five fields. Every
/// one of those is a place a port can silently disagree — and a Workling that
/// quietly loses its gear on the next tick is exactly the bug this codebase has
/// already shipped once.
public partial class PetStateProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static string F(double x) => x.ToString("F4");
    private static string B(bool b) => b ? "true" : "false";

    private static string Items(IReadOnlyList<Item> l)
    {
        if (l.Count == 0) return "-";
        var parts = new List<string>();
        foreach (var i in l) parts.Add(i.RawValue());
        return string.Join(",", parts);
    }

    private static string N(Item? i) => i.HasValue ? i.Value.RawValue() : "nil";

    private static string Lo(Loadout l) =>
        $"tool={N(l.Tool)} ward={N(l.Ward)} charm={N(l.Charm)}";

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        var now = D("2026-09-02T10:00:00Z");

        o.AppendLine("== needs clamping ==");
        var needCases = new[]
        {
            (50.0, 50.0, 50.0, 50.0),
            (-20.0, 130.0, 0.0, 100.0),
            (double.NaN, 50.0, double.PositiveInfinity, double.NegativeInfinity),
        };
        foreach (var (h, e, hp, t) in needCases)
        {
            var n = new PetNeeds(h, e, hp, t);
            o.AppendLine($"H{F(n.Hunger)} E{F(n.Energy)} Hp{F(n.Happiness)} T{F(n.Trust)} full {F(n.Fullness)}");
        }

        o.AppendLine("== xp multiplier ==");
        foreach (double floor in new[] { 0.0, 0.2, 0.9 })
        {
            var n = new PetNeeds(40, 30, 20, 10);
            o.AppendLine($"floor {F(floor)} -> {F(n.XPMultiplier(floor))}");
        }

        o.AppendLine("== new pet ==");
        var p = PetState.NewPet(now: now);
        o.AppendLine($"name {p.Name} family {p.Family.RawValue()} class {p.PetClass.RawValue()} "
            + $"schema {p.SchemaVersion} level {p.Level} xp {F(p.TotalXP)}");
        o.AppendLine($"stats V{p.Stats.Vitality} P{p.Stats.Power} D{p.Stats.Defense} "
            + $"A{p.Stats.Agility} W{p.Stats.Wit}");
        o.AppendLine($"owned {Items(p.OwnedItems)} | {Lo(p.Loadout)}");
        var eff = p.EffectiveStats();
        o.AppendLine($"effective V{eff.Vitality} P{eff.Power} D{eff.Defense} A{eff.Agility} W{eff.Wit}");
        o.AppendLine($"mood {p.Mood.RawValue()} prefs {p.Preferences.FavouriteFood.RawValue()}"
            + $"/{p.Preferences.FavouritePlayActivity.RawValue()}");

        o.AppendLine("== mood ladder ==");
        var moods = new (string Label, double H, double E, double Hp, double T)[]
        {
            ("hungry wins", 80, 10, 10, 10),
            ("sleepy next", 10, 15, 10, 10),
            ("wary next", 10, 50, 10, 15),
            ("sad next", 10, 50, 25, 50),
            ("happy", 30, 50, 80, 70),
            ("happy blocked by hunger", 45, 50, 80, 70),
            ("content", 50, 50, 50, 50),
        };
        foreach (var (label, h, e, hp, t) in moods)
        {
            var s = new PetState("T", new PetNeeds(h, e, hp, t), p.Preferences, now);
            o.AppendLine($"{label}: {s.Mood.RawValue()}");
        }

        o.AppendLine("== phantom gear rejected ==");
        var phantom = new PetState("T", p.Needs, p.Preferences, now,
            ownedItems: new[] { Item.StickyNote },
            loadout: new Loadout(Item.MastersHone, Item.FailsafePlate, Item.StickyNote));
        o.AppendLine($"owned {Items(phantom.OwnedItems)} | {Lo(phantom.Loadout)}");

        o.AppendLine("== duplicates collapsed ==");
        var dupes = new PetState("T", p.Needs, p.Preferences, now,
            ownedItems: new[] { Item.RubberDuck, Item.StickyNote, Item.RubberDuck,
                                Item.MastersHone, Item.StickyNote });
        o.AppendLine($"owned {Items(dupes.OwnedItems)}");

        o.AppendLine("== negative xp floored ==");
        o.AppendLine(F(new PetState("T", p.Needs, p.Preferences, now, totalXP: -500).TotalXP));

        o.AppendLine("== acquiring ==");
        var g = p;
        foreach (var item in new[] { Item.ChippedFile, Item.MastersHone, Item.RubberDuck,
                                     Item.HotpathSigil, Item.FrayedLanyard, Item.ChippedFile })
        {
            int before = g.OwnedItems.Count;
            g = g.Acquiring(item);
            o.AppendLine($"acquire {item.RawValue()}: {before} -> {g.OwnedItems.Count}");
        }
        o.AppendLine($"owned {Items(g.OwnedItems)}");

        o.AppendLine("== equipping ==");
        o.AppendLine($"equip mastersHone: {Lo(g.Equipping(Item.MastersHone).Loadout)}");
        o.AppendLine($"equip unowned failsafePlate: {Lo(g.Equipping(Item.FailsafePlate).Loadout)}");
        o.AppendLine($"equip wrong slot (stickyNote in tool): "
            + $"{Lo(g.Equipping(Item.StickyNote, ItemSlot.Tool).Loadout)}");
        o.AppendLine($"clear charm: {Lo(g.ClearingSlot(ItemSlot.Charm).Loadout)}");
        o.AppendLine($"clear empty tool is same instance: {B(g.ClearingSlot(ItemSlot.Tool).Equals(g))}");
        var full = g.Equipping(Item.MastersHone).Equipping(Item.HotpathSigil);
        o.AppendLine($"full: {Lo(full.Loadout)}");

        o.AppendLine("== available items best-first ==");
        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            o.AppendLine($"{slot.RawValue()}: {Items(g.AvailableItems(slot))}");
        }

        o.AppendLine("== effective with gear ==");
        foreach (var fam in PetFamilyExtensions.AllCases)
        {
            var e = full.SelectingFamily(fam).EffectiveStats();
            o.AppendLine($"{fam.RawValue()}: V{e.Vitality} P{e.Power} D{e.Defense} A{e.Agility} W{e.Wit}");
        }

        o.AppendLine("== forgetting ==");
        var forgotten = full.Applying(addingXP: 900)
            .SelectingClass(PetClass.Maverick).ForgettingAcquiredItems();
        o.AppendLine($"owned {Items(forgotten.OwnedItems)} | {Lo(forgotten.Loadout)} "
            + $"| xp {F(forgotten.TotalXP)} class {forgotten.PetClass.RawValue()} level {forgotten.Level}");

        o.AppendLine("== applying xp ==");
        var x = p;
        foreach (double add in new[] { 50.0, -40, 0, 260, 1000 })
        {
            x = x.Applying(addingXP: add);
            o.AppendLine($"+{F(add)} -> xp {F(x.TotalXP)} level {x.Level}");
        }
        o.AppendLine("applying needs: "
            + F(p.Applying(needs: new PetNeeds(90, 5, 5, 5)).Needs.Hunger));

        o.AppendLine("== withers preserve gear ==");
        var kept = full.SelectingFamily(PetFamily.Glitchkin)
            .SelectingClass(PetClass.Aegis).Applying(addingXP: 10);
        o.AppendLine($"owned {Items(kept.OwnedItems)} | {Lo(kept.Loadout)}");

        o.AppendLine("== rename ==");
        var names = new[] { "Sparky", "  Padded  ", "", "   ",
            new string('a', 24), new string('a', 25), "👨‍👩‍👧‍👦👨‍👩‍👧‍👦" };
        foreach (var candidate in names)
        {
            var r = p.Renamed(candidate);
            string shown = candidate.Length > 30 ? candidate.Substring(0, 30) : candidate;
            o.AppendLine($"[{shown}] valid {B(PetState.IsValidName(candidate))} -> {r.Name}");
        }

        o.AppendLine("== identity ==");
        o.AppendLine($"family {p.SelectingFamily(PetFamily.Relicborn).Family.RawValue()} "
            + $"class {p.SelectingClass(PetClass.Tinkerer).PetClass.RawValue()}");

        o.AppendLine("== reactions ==");
        foreach (var r in new[] { PetReaction.LikedFood, PetReaction.TooTiredToPlay,
                                  PetReaction.ProudOfMilestone, PetReaction.NoticedYouAreAway })
        {
            o.AppendLine(r.RawValue());
        }

        o.AppendLine("== equality ==");
        o.AppendLine($"same: {B(p.Equals(PetState.NewPet(now: now)))}");
        o.AppendLine($"differs: {B(p.Equals(PetState.NewPet(name: "Other", now: now)))}");

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
