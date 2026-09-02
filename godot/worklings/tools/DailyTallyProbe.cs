using Godot;
using Worklings.Core.Pet;

/// Compares DailyTally's same-day check against reference output captured from
/// the Swift original. Swift compares through Calendar.current — the *local*
/// calendar — so a UTC-based port passes every obvious test and quietly rolls
/// the day over at the wrong moment. The fixtures straddle local midnight in
/// both directions to catch that.
public partial class DailyTallyProbe : Node
{
    private static System.DateTimeOffset D(string s) =>
        System.DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    public override void _Ready()
    {
        var o = new System.Text.StringBuilder();
        var anchor = D("2026-09-02T14:30:00Z");

        o.AppendLine("== daily tally ==");
        var cases = new (string Label, System.DateTimeOffset? Stored)[]
        {
            ("same instant", anchor),
            ("same day earlier", D("2026-09-02T01:00:00Z")),
            ("same day later", D("2026-09-02T23:00:00Z")),
            ("previous day", D("2026-09-01T23:59:59Z")),
            ("next day", D("2026-09-03T00:00:01Z")),
            ("a year off", D("2025-09-02T14:30:00Z")),
            ("no date", null),
        };
        foreach (var (label, stored) in cases)
        {
            var t = new DailyTally<int>(42, stored);
            o.AppendLine($"{label}: {t.Current(anchor, -1)}");
        }

        o.AppendLine("== dictionary payload ==");
        var payload = new System.Collections.Generic.Dictionary<string, double>
        {
            ["milestone"] = 80.0,
            ["care"] = 12.0,
        };
        var empty = new System.Collections.Generic.Dictionary<string, double>();
        var ledger = new DailyTally<System.Collections.Generic.Dictionary<string, double>>(
            payload, anchor);
        var today = ledger.Current(anchor, empty);
        var keys = new System.Collections.Generic.List<string>(today.Keys);
        keys.Sort(string.CompareOrdinal);
        o.AppendLine("today: " + string.Join(" ",
            keys.ConvertAll(k => $"{k}={today[k].ToString("0.0###")}")));
        var stale = ledger.Current(D("2026-09-05T09:00:00Z"), empty);
        o.AppendLine($"stale: {(stale.Count == 0 ? "empty" : "leaked")}");

        o.AppendLine("== equality ==");
        var other = D("2026-09-03T14:30:00Z");
        o.AppendLine($"same: {B(new DailyTally<int>(3, anchor).Equals(new DailyTally<int>(3, anchor)))}");
        o.AppendLine($"value differs: {B(new DailyTally<int>(3, anchor).Equals(new DailyTally<int>(4, anchor)))}");
        o.AppendLine($"date differs: {B(new DailyTally<int>(3, anchor).Equals(new DailyTally<int>(3, other)))}");
        o.AppendLine($"nil vs date: {B(new DailyTally<int>(3).Equals(new DailyTally<int>(3, anchor)))}");
        o.AppendLine($"both nil: {B(new DailyTally<int>(3).Equals(new DailyTally<int>(3)))}");

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }

    private static string B(bool b) => b ? "true" : "false";
}
