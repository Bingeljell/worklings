using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Compares the delve chain against reference output captured from the Swift
/// original: the drop-tier curve, four full runs to the boss, an early bank, a
/// retreat, every state guard, an exhausted drop tier, and replay determinism.
///
/// The drop pick is the sharpest edge here — Swift's randomElement(using:) draws
/// one bounded word off a generator seeded per encounter, so a port that reaches
/// for a double instead picks different gear and desynchronises the stream.
public partial class DelveProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    private static readonly System.DateTimeOffset Now = D("2026-09-02T10:00:00Z");
    private static readonly PetCombatRates Rates = new();

    private static string F(double x) => x.ToString("F4");
    private static string B(bool b) => b ? "true" : "false";

    private static string Names(IReadOnlyList<Item> l)
    {
        if (l.Count == 0) return "-";
        var parts = new List<string>();
        foreach (var i in l) parts.Add(i.RawValue());
        return string.Join(",", parts);
    }

    private static string N(Item? i) => i.HasValue ? i.Value.RawValue() : "none";

    private static PetState MkPet(IReadOnlyList<Item>? owned = null) =>
        new PetState(
            name: "Pixel",
            needs: new PetNeeds(20, 85, 80, 75),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: Now,
            family: PetFamily.Relicborn,
            totalXP: 900,
            stats: new PetStats(vitality: 22, power: 17, defense: 13, agility: 11, wit: 8),
            ownedItems: owned ?? new[] { Item.RubberDuck },
            loadout: Loadout.Empty.Equipping(Item.RubberDuck));

    private static string StatusDesc(DelveStatus s) => s.Kind switch
    {
        DelveStatusKind.Briefing => "briefing",
        DelveStatusKind.InEncounter => "inEncounter",
        DelveStatusKind.AwaitingPushChoice => "awaitingPushChoice",
        DelveStatusKind.Completed => $"completed({s.Tier.RawValue()})",
        _ => "retreated",
    };

    private static Delve MakeDelve(PetState pet, ulong seed) =>
        Delve.CacheWarrenDelve(
            Combatant.Pet(pet, Rates),
            Rates.CombatEffectiveness(pet.Needs),
            Rates, seed, pet.OwnedItems);

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();

        o.AppendLine("== drop tiers ==");
        var d0 = MakeDelve(MkPet(), 1);
        for (int i = 0; i < d0.TotalEncounters; i++)
        {
            o.AppendLine($"encounter {i}: {d0.DropTier(i).RawValue()}");
        }
        o.AppendLine($"totalEncounters {d0.TotalEncounters} isBoss@0 {B(d0.IsBossEncounter)}");

        o.AppendLine("== full push to the boss ==");
        foreach (ulong seed in new ulong[] { 1, 5, 12, 77 })
        {
            var pet = MkPet();
            var d = MakeDelve(pet, seed);
            o.AppendLine($"seed {seed}: {StatusDesc(d.Status)} hp {d.CarriedHP}");
            d.Descend();
            while (!d.IsFinished)
            {
                var enc = d.MakeEncounter(Approach.Careful);
                if (enc is null) break;
                string foeName = d.CurrentFoe?.Name ?? "?";
                enc.RunToCompletion();
                d.RecordOutcome(enc);
                o.AppendLine($"  e{d.EncounterNumber} vs {foeName}: {StatusDesc(d.Status)} "
                    + $"hp {d.CarriedHP} xp {F(d.AccumulatedXP)} cleared {d.ClearedCount} "
                    + $"drop {N(d.LastDrop)}");
                if (d.Status.Kind == DelveStatusKind.AwaitingPushChoice)
                {
                    d.PushDeeper();
                    o.AppendLine($"    push -> hp {d.CarriedHP} index {d.Index}");
                }
            }
            var r = d.Resolution(pet);
            if (r is not null)
            {
                o.AppendLine($"  result tier {r.Tier.RawValue()} xp {F(r.XPGained)} "
                    + $"cleared {r.ClearedCount} boss {B(r.BossDefeated)} banked {B(r.Banked)}");
                o.AppendLine($"  drops {Names(r.ItemsDropped)} bossDrop {N(r.BossDrop)} "
                    + $"shallow {Names(r.ShallowDrops)}");
                o.AppendLine($"  owned {Names(r.State.OwnedItems)} "
                    + $"needs H{F(r.State.Needs.Hunger)} E{F(r.State.Needs.Energy)} "
                    + $"totalXP {F(r.State.TotalXP)}");
            }
        }

        o.AppendLine("== bank after the first clear ==");
        var bp = MkPet();
        var bd = MakeDelve(bp, 5);
        bd.Descend();
        var be = bd.MakeEncounter(Approach.Careful);
        if (be is not null) { be.RunToCompletion(); bd.RecordOutcome(be); }
        bd.Bank();
        o.AppendLine($"status {StatusDesc(bd.Status)}");
        var br = bd.Resolution(bp);
        if (br is not null)
        {
            o.AppendLine($"tier {br.Tier.RawValue()} xp {F(br.XPGained)} cleared {br.ClearedCount} "
                + $"boss {B(br.BossDefeated)} banked {B(br.Banked)} drops {Names(br.ItemsDropped)} "
                + $"bossDrop {N(br.BossDrop)}");
        }

        o.AppendLine("== guards ==");
        var gd = MakeDelve(MkPet(), 3);
        gd.Bank(); o.AppendLine($"bank at briefing: {StatusDesc(gd.Status)}");
        gd.PushDeeper(); o.AppendLine($"push at briefing: {StatusDesc(gd.Status)}");
        o.AppendLine($"makeEncounter at briefing: "
            + (gd.MakeEncounter(Approach.Careful) is null ? "nil" : "some"));
        gd.Descend(); o.AppendLine($"descend: {StatusDesc(gd.Status)}");
        gd.Descend(); o.AppendLine($"descend again: {StatusDesc(gd.Status)}");
        gd.Bank(); o.AppendLine($"bank mid-encounter: {StatusDesc(gd.Status)}");
        o.AppendLine($"resolution while running: "
            + (gd.Resolution(MkPet()) is null ? "nil" : "some"));

        o.AppendLine("== retreat ==");
        var rd = MakeDelve(MkPet(), 9);
        rd.Descend();
        rd.RecordOutcome(false, 0);
        o.AppendLine($"status {StatusDesc(rd.Status)}");
        var rr = rd.Resolution(MkPet());
        if (rr is not null)
        {
            o.AppendLine($"tier {rr.Tier.RawValue()} xp {F(rr.XPGained)} cleared {rr.ClearedCount} "
                + $"banked {B(rr.Banked)} drops {Names(rr.ItemsDropped)}");
        }

        o.AppendLine("== retreat keeps earlier drops ==");
        var kp = MkPet();
        var kd = MakeDelve(kp, 5);
        kd.Descend();
        var ke = kd.MakeEncounter(Approach.Careful);
        if (ke is not null) { ke.RunToCompletion(); kd.RecordOutcome(ke); }
        kd.PushDeeper();
        kd.RecordOutcome(false, 0);
        var kr = kd.Resolution(kp);
        if (kr is not null)
        {
            o.AppendLine($"tier {kr.Tier.RawValue()} drops {Names(kr.ItemsDropped)} "
                + $"owned {Names(kr.State.OwnedItems)} banked {B(kr.Banked)}");
        }

        o.AppendLine("== exhausted tier ==");
        var richOwned = new List<Item>(ItemExtensions.All(ItemTier.Scavenged)) { Item.RubberDuck };
        var xd = MakeDelve(MkPet(richOwned), 5);
        xd.Descend();
        var xe = xd.MakeEncounter(Approach.Careful);
        if (xe is not null) { xe.RunToCompletion(); xd.RecordOutcome(xe); }
        o.AppendLine($"lastDrop {N(xd.LastDrop)} drops {Names(xd.Drops)}");

        o.AppendLine("== determinism ==");
        foreach (ulong seed in new ulong[] { 1, 5, 12, 77 })
        {
            var traces = new List<string>();
            foreach (int _ in new[] { 0, 1 })
            {
                var dl = MakeDelve(MkPet(), seed);
                dl.Descend();
                var trace = new List<string>();
                while (!dl.IsFinished)
                {
                    var e = dl.MakeEncounter(Approach.Clever);
                    if (e is null) break;
                    e.RunToCompletion();
                    dl.RecordOutcome(e);
                    trace.Add($"{dl.CarriedHP}/{(dl.LastDrop.HasValue ? dl.LastDrop.Value.RawValue() : "-")}");
                    if (dl.Status.Kind == DelveStatusKind.AwaitingPushChoice) dl.PushDeeper();
                }
                traces.Add(string.Join("|", trace));
            }
            o.AppendLine($"seed {seed} replays equal: {B(traces[0] == traces[1])} trace {traces[0]}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
