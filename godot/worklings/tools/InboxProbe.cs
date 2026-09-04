using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;

/// Compares the inbox's trust boundary against reference output captured from
/// the Swift original.
///
/// This is the one type in the pipeline that reads bytes written by something
/// outside the app, so every fixture here is a way that could go wrong: a
/// payload too large to be good faith, malformed JSON, a field of the wrong
/// type, a kind the app reserves for itself, a source id impersonating one the
/// app emits under, and timestamps on both sides of both time limits. The rule
/// that keeps a backlog from replaying onto the pet at launch is a single `<=`,
/// so it is checked at the boundary and one second either side of it.
public partial class InboxProbe : Node
{
    private static readonly System.DateTimeOffset Now = System.DateTimeOffset.Parse(
        "2026-09-04T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind);

    private readonly System.Text.StringBuilder o = new();

    private static string B(bool b) => b ? "true" : "false";

    private static string F(double v) =>
        v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private void Decode(string label, string json)
    {
        var result = ActivityInbox.Decode(System.Text.Encoding.UTF8.GetBytes(json), Now);
        if (result.Event is ActivityEvent evt)
        {
            // The offset from `now` in seconds, so the line says what the rule
            // actually cares about rather than restating the fixture.
            double delta = (evt.Timestamp - Now).TotalSeconds;
            o.AppendLine($"{label}: ok kind={evt.Kind.RawValue()} source={evt.SourceId} "
                       + $"at={delta.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else
        {
            o.AppendLine($"{label}: refused {result.Rejection!.Value.RawValue()}");
        }
    }

    private static string Payload(string kind, string sourceId, string? timestamp = null) =>
        timestamp is null
            ? $"{{\"kind\":\"{kind}\",\"sourceId\":\"{sourceId}\"}}"
            : $"{{\"kind\":\"{kind}\",\"sourceId\":\"{sourceId}\",\"timestamp\":\"{timestamp}\"}}";

    public override void _Ready()
    {
        o.AppendLine("== who may emit what ==");
        foreach (var kind in ActivityEventKindExtensions.AllCases)
        {
            o.AppendLine($"{kind.RawValue()}: {B(ActivityInbox.IsAdapterEmittable(kind))}");
        }
        o.AppendLine($"accepted kinds: {ActivityInbox.AcceptedKinds.Count}");
        foreach (string id in new[] { "system", "manual", "simulated", "git", "codex" })
        {
            o.AppendLine($"reserved {id}: {B(ActivityInbox.ReservedSourceIds.Contains(id))}");
        }
        // Formatted explicitly: Swift prints a Double as "1800.0" and C# as
        // "1800", which is a diff with nothing behind it.
        o.AppendLine($"limits: age={F(ActivityInbox.MaxEventAge)} skew={F(ActivityInbox.MaxFutureSkew)} "
                   + $"bytes={ActivityInbox.MaxPayloadBytes} idLength={ActivityInbox.MaxSourceIdLength}");

        o.AppendLine("== source ids ==");
        foreach (string id in new[]
                 {
                     "codex", "claude-code", "a", "0", "a.b_c-d", "",
                     "-leading", ".leading", "_leading", "trailing-",
                     "Upper", "has space", "has/slash", "has:colon", "emoji✅",
                     new string('a', 64), new string('a', 65),
                 })
        {
            o.AppendLine($"\"{id}\": {B(ActivityInbox.IsValidSourceId(id))}");
        }

        o.AppendLine("== decode, the shape of the file ==");
        Decode("valid, no timestamp", Payload("taskCompleted", "codex"));
        Decode("valid, explicit timestamp", Payload("milestone", "codex", "2026-09-04T11:59:00Z"));
        Decode("uppercase source id", Payload("milestone", "CODEX", "2026-09-04T11:59:00Z"));
        Decode("null timestamp", "{\"kind\":\"milestone\",\"sourceId\":\"codex\",\"timestamp\":null}");
        Decode("extra fields ignored",
               "{\"kind\":\"milestone\",\"sourceId\":\"codex\",\"prompt\":\"secret\"}");
        Decode("not json", "this is not json");
        Decode("json array", "[]");
        Decode("json string", "\"milestone\"");
        Decode("empty object", "{}");
        Decode("missing sourceId", "{\"kind\":\"milestone\"}");
        Decode("missing kind", "{\"sourceId\":\"codex\"}");
        Decode("kind is a number", "{\"kind\":7,\"sourceId\":\"codex\"}");
        Decode("timestamp is a number",
               "{\"kind\":\"milestone\",\"sourceId\":\"codex\",\"timestamp\":7}");
        Decode("unknown kind", Payload("deployedToProd", "codex"));
        Decode("reserved kind, dailyWake", Payload("dailyWake", "codex"));
        Decode("reserved kind, workLogged", Payload("workLogged", "codex"));
        Decode("reserved kind, userIdle", Payload("userIdle", "codex"));
        Decode("reserved source, system", Payload("milestone", "system"));
        Decode("reserved source, manual", Payload("milestone", "manual"));
        Decode("reserved source, simulated", Payload("milestone", "simulated"));
        Decode("git is not reserved", Payload("milestone", "git"));
        Decode("bad source id", Payload("milestone", "has space"));
        // A payload larger than the cap is refused before it is parsed, so a
        // huge but perfectly valid file is still refused.
        Decode("oversized but valid",
               "{\"kind\":\"milestone\",\"sourceId\":\"codex\",\"pad\":\""
               + new string('x', 4096) + "\"}");

        o.AppendLine("== decode, the clock ==");
        foreach (var (label, timestamp) in new (string, string)[]
                 {
                     ("now", "2026-09-04T12:00:00Z"),
                     ("a minute old", "2026-09-04T11:59:00Z"),
                     ("exactly the age limit", "2026-09-04T11:30:00Z"),
                     ("a second past it", "2026-09-04T11:29:59Z"),
                     ("exactly the skew limit", "2026-09-04T12:02:00Z"),
                     ("a second past that", "2026-09-04T12:02:01Z"),
                     ("fractional seconds", "2026-09-04T11:59:59.500Z"),
                     ("an offset zone", "2026-09-04T17:29:00+05:30"),
                     ("an offset with no colon", "2026-09-04T17:29:00+0530"),
                     ("no zone at all", "2026-09-04T11:59:00"),
                     ("a date only", "2026-09-04"),
                     ("not a date", "yesterday"),
                     ("empty", ""),
                 })
        {
            Decode(label, Payload("milestone", "codex", timestamp));
        }

        o.AppendLine("== delivery order ==");
        var events = new List<ActivityEvent>
        {
            new(ActivityEventKind.WorkEnded, Now.AddSeconds(-100), "codex"),
            new(ActivityEventKind.Milestone, Now.AddSeconds(-500), "git"),
            new(ActivityEventKind.WorkStarted, Now.AddSeconds(-900), "codex"),
            // Two at the same instant: a stable sort leaves them in this order.
            new(ActivityEventKind.TaskCompleted, Now.AddSeconds(-500), "codex"),
            new(ActivityEventKind.AwaitingInput, Now, "codex"),
        };
        foreach (var evt in ActivityInbox.Ordered(events))
        {
            o.AppendLine($"  {evt.Kind.RawValue()} {evt.SourceId} "
                       + $"{(evt.Timestamp - Now).TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        o.AppendLine($"empty: {ActivityInbox.Ordered(new List<ActivityEvent>()).Count}");

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
