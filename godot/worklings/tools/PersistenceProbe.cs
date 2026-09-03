using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the save file — both halves, the JSON on the wire and the state that
/// comes back off it — against reference output captured from the Swift original.
///
/// This probe is worth more than most. The encoder claims to be byte-identical to
/// Foundation's, which is a claim nothing else in the build can check: a save that
/// merely *round-trips through C#* would pass every test here and still be a file
/// the Swift app cannot read. So the JSON text itself is printed, not just the
/// decoded fields, and the diff is against bytes Swift actually produced.
///
/// The fixtures cover the five migration rules recorded in
/// docs/engineering/godot-port-status.md: the two legacy flat pairs, the absent
/// event counts, the gearless save reading as the starter loadout, and decoding
/// routing through the validating constructor.
public partial class PersistenceProbe : Node
{
    private static readonly System.DateTimeOffset Reference = PetStateCodec.ReferenceDate;

    private static System.DateTimeOffset At(double interval) =>
        PetStateCodec.FromSwiftDate(interval);

    private System.Text.StringBuilder o = new();

    public override void _Ready()
    {
        var fresh = PetState.NewPet(name: "Pixel", family: PetFamily.Wildkin, now: At(800_000_000));
        o.AppendLine("=== ENCODE fresh ===");
        o.AppendLine(PetStateCodec.Encode(fresh));

        var full = new PetState(
            name: "Nimbus",
            family: PetFamily.Relicborn,
            needs: new PetNeeds(hunger: 33.5, energy: 61.25, happiness: 88, trust: 47.75),
            preferences: new PetPreferences(PetFood.Noodles, PetPlayActivity.Dance),
            lastUpdatedAt: At(800_000_123.5),
            lastWorkLogAt: At(799_999_000),
            workLog: new DailyTally<int>(4, At(800_000_000)),
            totalXP: 1234.5,
            petClass: PetClass.Aegis,
            stats: new PetStats(vitality: 9, power: 7, defense: 12, agility: 6, wit: 8),
            dailyXP: new DailyTally<Dictionary<string, double>>(
                new Dictionary<string, double> { ["commit"] = 40.25, ["test"] = 12 },
                At(800_000_000)),
            dailyEventCount: new DailyTally<Dictionary<string, int>>(
                new Dictionary<string, int> { ["commit"] = 3, ["test"] = 1 },
                At(800_000_000)),
            ownedItems: new[] { Item.RubberDuck, Item.MastersHone, Item.DentedBuckler },
            loadout: new Loadout(Item.MastersHone, Item.DentedBuckler, Item.RubberDuck));
        o.AppendLine("=== ENCODE full ===");
        o.AppendLine(PetStateCodec.Encode(full));

        Show("ROUNDTRIP fresh", PetStateCodec.Decode(PetStateCodec.Encode(fresh)));
        Show("ROUNDTRIP full", PetStateCodec.Decode(PetStateCodec.Encode(full)));

        // A v1 save: flat daily fields, no gear, no family, no class, no stats.
        const string legacy = """
        {
          "schemaVersion" : 1,
          "name" : "Legacy",
          "needs" : { "hunger" : 20, "energy" : 70, "happiness" : 60, "trust" : 40 },
          "preferences" : { "favouriteFood" : "biscuit", "favouritePlayActivity" : "chase" },
          "lastUpdatedAt" : 799999999,
          "workLogCountToday" : 3,
          "workLogCountDate" : 799990000,
          "dailyXPBySource" : { "commit" : 25.5 },
          "dailyXPDate" : 799995000
        }
        """;
        Show("DECODE legacy v1", PetStateCodec.Decode(legacy));

        // A save equipping an item it does not own, a duplicate owned entry, and
        // an item in the wrong slot — the three ways a hand-edited file lies.
        const string phantom = """
        {
          "schemaVersion" : 2,
          "name" : "Phantom",
          "family" : "elemental",
          "needs" : { "hunger" : 10, "energy" : 90, "happiness" : 50, "trust" : 50 },
          "preferences" : { "favouriteFood" : "berries", "favouritePlayActivity" : "puzzle" },
          "lastUpdatedAt" : 800000000,
          "totalXP" : 500,
          "petClass" : "maverick",
          "stats" : { "vitality" : 6, "power" : 6, "defense" : 5, "agility" : 9, "wit" : 7 },
          "ownedItems" : [ "stickyNote", "stickyNote", "bentPotLid" ],
          "loadout" : { "tool" : "bentPotLid", "ward" : "failsafePlate", "charm" : "stickyNote" }
        }
        """;
        Show("DECODE phantom", PetStateCodec.Decode(phantom));

        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "worklings-persistence-probe");
        System.IO.Directory.CreateDirectory(directory);
        string Path(string name) => System.IO.Path.Combine(directory, name);

        o.AppendLine("=== STORE ===");
        System.IO.File.WriteAllText(Path("future.json"), """
        {
          "schemaVersion" : 99,
          "name" : "Future",
          "needs" : { "hunger" : 10, "energy" : 90, "happiness" : 50, "trust" : 50 },
          "preferences" : { "favouriteFood" : "berries", "favouritePlayActivity" : "puzzle" },
          "lastUpdatedAt" : 800000000
        }
        """);
        try
        {
            new PetStateFileStore(Path("future.json")).Load();
            o.AppendLine("future: loaded (WRONG)");
        }
        catch (UnsupportedSchemaException error)
        {
            o.AppendLine($"future: {error.Message}");
        }

        string missingPath = Path("nope.json");
        if (System.IO.File.Exists(missingPath))
        {
            System.IO.File.Delete(missingPath);
        }
        o.AppendLine(
            $"missing: {(new PetStateFileStore(missingPath).Load() is null ? "nil" : "some")}");

        var store = new PetStateFileStore(Path("store.json"));
        store.Save(full);
        o.AppendLine($"save/load equal: {B(full.Equals(store.Load()))}");

        System.IO.File.WriteAllText(Path("legacy.json"), legacy);
        Show("STORE legacy v1", new PetStateFileStore(Path("legacy.json")).Load()!);

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private void Show(string label, PetState s)
    {
        o.AppendLine($"--- {label} ---");
        o.AppendLine($"schemaVersion={s.SchemaVersion}");
        o.AppendLine($"name={s.Name}");
        o.AppendLine($"family={s.Family.RawValue()}");
        o.AppendLine($"needs={F(s.Needs.Hunger)} {F(s.Needs.Energy)} {F(s.Needs.Happiness)} {F(s.Needs.Trust)}");
        o.AppendLine($"prefs={s.Preferences.FavouriteFood.RawValue()} {s.Preferences.FavouritePlayActivity.RawValue()}");
        o.AppendLine($"lastUpdatedAt={D(s.LastUpdatedAt)}");
        o.AppendLine($"lastWorkLogAt={(s.LastWorkLogAt.HasValue ? D(s.LastWorkLogAt.Value) : "nil")}");
        o.AppendLine($"workLog.date={(s.WorkLog.Date.HasValue ? D(s.WorkLog.Date.Value) : "nil")}");
        o.AppendLine($"workLog.value={s.WorkLog.Value}");
        o.AppendLine($"totalXP={F(s.TotalXP)}");
        o.AppendLine($"petClass={s.PetClass.RawValue()}");
        o.AppendLine($"stats={s.Stats.Vitality} {s.Stats.Power} {s.Stats.Defense} {s.Stats.Agility} {s.Stats.Wit}");
        o.AppendLine($"dailyXP.date={(s.DailyXP.Date.HasValue ? D(s.DailyXP.Date.Value) : "nil")}");
        o.AppendLine("dailyXP.value=" + Join(s.DailyXP.Value, F));
        o.AppendLine($"dailyEventCount.date={(s.DailyEventCount.Date.HasValue ? D(s.DailyEventCount.Date.Value) : "nil")}");
        o.AppendLine("dailyEventCount.value=" + Join(s.DailyEventCount.Value, n => n.ToString()));
        o.AppendLine("ownedItems=" + string.Join(",", System.Array.ConvertAll(
            new List<Item>(s.OwnedItems).ToArray(), i => i.RawValue())));
        o.AppendLine("loadout=" + string.Join(",", System.Array.ConvertAll(
            ItemSlotExtensions.AllCases,
            slot => $"{slot.RawValue()}:{(s.Loadout[slot]?.RawValue() ?? "-")}")));
    }

    private static string Join<TValue>(
        Dictionary<string, TValue> map, System.Func<TValue, string> format)
    {
        var keys = new List<string>(map.Keys);
        keys.Sort(string.CompareOrdinal);
        return string.Join(",", keys.ConvertAll(k => $"{k}:{format(map[k])}"));
    }

    private static string F(double value) =>
        value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

    private static string D(System.DateTimeOffset date) => F(PetStateCodec.ToSwiftDate(date));

    private static string B(bool value) => value ? "true" : "false";
}
