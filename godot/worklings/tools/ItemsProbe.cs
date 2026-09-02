using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the C# gear layer against reference output captured from the Swift
/// original. Slot/stat/tier/attunement is a web of switches where one wrong arm
/// changes what a drop is worth and nothing complains, so every item is checked
/// against every family rather than spot-checked.
public partial class ItemsProbe : Node
{
    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();

        o.AppendLine("== items ==");
        foreach (var i in ItemExtensions.AllCases)
        {
            var att = i.AttunedFamily().HasValue ? i.AttunedFamily()!.Value.RawValue() : "none";
            o.AppendLine($"{i.RawValue()} | {i.DisplayName()} | slot {i.Slot().RawValue()} "
                + $"| stat {i.Stat().RawValue()} | tier {i.Tier().RawValue()} | attuned {att}");
        }

        o.AppendLine("== flavor ==");
        foreach (var i in ItemExtensions.AllCases) o.AppendLine($"{i.RawValue()}: {i.Flavor()}");

        o.AppendLine("== all(in tier) ==");
        foreach (var t in ItemTierExtensions.AllCases)
        {
            o.AppendLine($"{t.RawValue()} [{t.DisplayName()}]: "
                + string.Join(",", System.Array.ConvertAll(ItemExtensions.All(t), i => i.RawValue())));
        }

        o.AppendLine("== all(in slot) ==");
        foreach (var s in ItemSlotExtensions.AllCases)
        {
            o.AppendLine($"{s.RawValue()} [{s.DisplayName()}]: "
                + string.Join(",", System.Array.ConvertAll(ItemExtensions.All(s), i => i.RawValue())));
            o.AppendLine($"  fantasy: {s.Fantasy()}");
        }

        o.AppendLine("== tier ordering ==");
        foreach (var a in ItemTierExtensions.AllCases)
        {
            foreach (var b in ItemTierExtensions.AllCases)
            {
                o.AppendLine($"{a.RawValue()} < {b.RawValue()} = "
                    + (b.IsBetterThan(a) ? "true" : "false"));
            }
        }

        o.AppendLine("== modifiers by family ==");
        var rates = ItemRates.Default;
        foreach (var i in ItemExtensions.AllCases)
        {
            var row = System.Array.ConvertAll(PetFamilyExtensions.AllCases, f =>
                $"{f.RawValue()}={rates.Modifier(i, f)}{(rates.IsAttuned(i, f) ? "*" : "")}");
            o.AppendLine($"{i.RawValue()}: {string.Join(" ", row)}");
        }

        o.AppendLine("== rates clamping ==");
        var cr = new ItemRates(scavengedModifier: -3, solidModifier: -1,
                               primeModifier: -9, attunementBonus: -2);
        o.AppendLine($"s{cr.ScavengedModifier} so{cr.SolidModifier} p{cr.PrimeModifier} a{cr.AttunementBonus}");

        o.AppendLine("== loadout validation ==");
        var wrong = new Loadout(tool: Item.RubberDuck, ward: Item.ChippedFile, charm: Item.BentPotLid);
        o.AppendLine($"wrong-slot loadout: tool {Name(wrong.Tool)} ward {Name(wrong.Ward)} "
            + $"charm {Name(wrong.Charm)} isEmpty {B(wrong.IsEmpty)}");
        var l = Loadout.Empty;
        o.AppendLine($"empty: isEmpty {B(l.IsEmpty)} equipped {l.Equipped.Count}");
        l = l.Equipping(Item.MastersHone);
        l = l.Equipping(Item.DentedBuckler);
        l = l.Equipping(Item.RubberDuck);
        o.AppendLine($"filled: {Names(l)}");
        o.AppendLine($"subscript tool={Name(l[ItemSlot.Tool])} ward={Name(l[ItemSlot.Ward])} "
            + $"charm={Name(l[ItemSlot.Charm])}");
        var rejected = l.Equipping(Item.StickyNote, ItemSlot.Tool);
        o.AppendLine($"reject wrong slot: tool still {Name(rejected[ItemSlot.Tool])}");
        var cleared = l.Clearing(ItemSlot.Ward);
        o.AppendLine($"cleared ward: {Names(cleared)}");

        o.AppendLine("== modifiers dict ==");
        foreach (var fam in PetFamilyExtensions.AllCases)
        {
            var m = l.Modifiers(fam);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var k in PetStatKindExtensions.AllCases)
            {
                if (m.TryGetValue(k, out int v)) parts.Add($"{k.RawValue()}={v}");
            }
            o.AppendLine($"{fam.RawValue()}: {string.Join(" ", parts)}");
        }

        o.AppendLine("== swaps ==");
        var incomings = new[] { Item.ChippedFile, Item.CrackedWhetstone, Item.MastersHone,
                                Item.RootCauseLens, Item.HotpathSigil, Item.RubberDuck };
        var fams = new[] { PetFamily.Relicborn, PetFamily.Elemental, PetFamily.Glitchkin };
        foreach (var incoming in incomings)
        {
            foreach (var fam in fams)
            {
                var s = l.Swap(incoming, fam);
                var lost = s.Lost.HasValue
                    ? $"{s.Lost.Value.Stat.RawValue()}-{s.Lost.Value.Amount}" : "none";
                o.AppendLine($"{incoming.RawValue()}/{fam.RawValue()}: out {Name(s.Outgoing)} "
                    + $"gain {s.Gained.Stat.RawValue()}+{s.Gained.Amount} lost {lost} "
                    + $"net {s.NetOnGainedStat} empty {B(s.FillsEmptySlot)} noop {B(s.IsNoOp)}");
            }
        }
        var onEmpty = Loadout.Empty.Swap(Item.ChippedFile, PetFamily.Wildkin);
        o.AppendLine($"onEmpty: empty {B(onEmpty.FillsEmptySlot)} noop {B(onEmpty.IsNoOp)} "
            + $"net {onEmpty.NetOnGainedStat}");

        o.AppendLine("== effective fold ==");
        var bas = new PetStats(vitality: 12, power: 9, defense: 7, agility: 11, wit: 6);
        foreach (var fam in PetFamilyExtensions.AllCases)
        {
            var e = bas.Effective(l, fam);
            o.AppendLine($"{fam.RawValue()}: V{e.Vitality} P{e.Power} D{e.Defense} A{e.Agility} W{e.Wit}");
        }
        var un = bas.Effective(Loadout.Empty, PetFamily.Wildkin);
        o.AppendLine($"empty loadout: V{un.Vitality} P{un.Power} D{un.Defense} A{un.Agility} W{un.Wit}");

        o.AppendLine("== families ==");
        foreach (var f in PetFamilyExtensions.AllCases)
        {
            o.AppendLine($"{f.RawValue()} | {f.DisplayName()} | hasArt {B(f.HasArt())}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private static string B(bool b) => b ? "true" : "false";
    private static string Name(Item? i) => i.HasValue ? i.Value.RawValue() : "nil";
    private static string Names(Loadout l) =>
        string.Join(",", l.Equipped.ConvertAll(i => i.RawValue()));
}
