using System.Collections.Generic;
using System.Text.Json;

namespace Worklings.Core.Pet;

/// Why a dropped file was refused. Named precisely rather than folded into one
/// "bad event", because these are the only diagnostics an adapter author ever
/// gets — the app cannot tell them what they wrote, only what was wrong with it.
public enum ActivityInboxRejection
{
    PayloadTooLarge,
    UnreadablePayload,
    UnknownKind,
    KindNotAccepted,
    InvalidSourceId,
    ReservedSourceId,
    InvalidTimestamp,
    StaleTimestamp,
    FutureTimestamp,
}

public static class ActivityInboxRejectionExtensions
{
    /// The Swift case name, which is what the probe diffs against.
    public static string RawValue(this ActivityInboxRejection rejection)
    {
        string s = rejection.ToString();
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}

/// Swift's `Result<ActivityEvent, Rejection>`. Exactly one of the two is set.
public sealed class ActivityInboxResult
{
    public ActivityEvent? Event { get; }
    public ActivityInboxRejection? Rejection { get; }

    private ActivityInboxResult(ActivityEvent? evt, ActivityInboxRejection? rejection)
    {
        Event = evt;
        Rejection = rejection;
    }

    public bool IsAccepted => Event is not null;

    public static ActivityInboxResult Accepted(ActivityEvent evt) => new(evt, null);

    public static ActivityInboxResult Refused(ActivityInboxRejection rejection) =>
        new(null, rejection);
}

/// The provider-neutral boundary external adapters write into: one small JSON
/// file per event, dropped into a local spool directory the app watches. The
/// contract has no fields for prompts, code, or any other content, so the
/// privacy promise is **structural rather than a policy** — an adapter
/// physically cannot hand the pet more than what happened and when.
///
/// This type owns decoding and validation only, so the trust boundary is pure
/// and deterministic; file watching and delivery live in the app.
///
/// Ported from Sources/CompanionCore/ActivityInbox.swift.
public static class ActivityInbox
{
    /// Whether an external adapter may emit this kind. The app-owned lifecycle
    /// kinds stay internal: `DailyWake` belongs to `DailyWakeTracker`, the
    /// presence kinds to the presence source, and `WorkLogged` to the user's own
    /// hand. A file claiming one of those would be an adapter dressing itself up
    /// as the app.
    public static bool IsAdapterEmittable(ActivityEventKind kind) => kind switch
    {
        ActivityEventKind.WorkStarted or ActivityEventKind.WorkEnded
            or ActivityEventKind.TaskCompleted or ActivityEventKind.TaskFailed
            or ActivityEventKind.AwaitingInput or ActivityEventKind.Milestone => true,
        ActivityEventKind.DailyWake or ActivityEventKind.UserIdle
            or ActivityEventKind.UserReturned or ActivityEventKind.WorkLogged => false,
        // Swift's switch is exhaustive, so adding a kind there refuses to
        // compile until it has been classified. C# cannot make that demand, so
        // the default is the safe answer rather than the convenient one.
        _ => false,
    };

    /// Kinds an external adapter may emit, derived from the classification above.
    public static readonly IReadOnlySet<ActivityEventKind> AcceptedKinds = Build();

    private static HashSet<ActivityEventKind> Build()
    {
        var set = new HashSet<ActivityEventKind>();
        foreach (var kind in ActivityEventKindExtensions.AllCases)
        {
            if (IsAdapterEmittable(kind)) set.Add(kind);
        }
        return set;
    }

    /// Ids the app itself emits under. A file claiming one could impersonate a
    /// self-reported or internal signal, so they are rejected outright.
    public static readonly IReadOnlySet<string> ReservedSourceIds = new HashSet<string>
    {
        SystemActivitySource.SourceId,
        ManualActivitySource.SourceId,
        SimulatedActivitySource.SourceId,
    };

    /// An event older than the activity context's own expiry window could only
    /// ever be discarded downstream, so it is rejected at the boundary. This is
    /// what stops a backlog written while the app was closed from replaying onto
    /// the pet at launch.
    public const double MaxEventAge = ActivityContext.DefaultExpiryInterval;

    /// A small allowance for clock skew between a writer and the app; anything
    /// further into the future is a broken clock, not a signal.
    public const double MaxFutureSkew = 2 * 60;

    /// A valid payload is tens of bytes; anything larger is not a good-faith
    /// event and is refused before it is parsed.
    public const int MaxPayloadBytes = 4096;

    public const int MaxSourceIdLength = 64;

    private const string Allowed = "abcdefghijklmnopqrstuvwxyz0123456789._-";

    /// Swift's `ISO8601DateFormatter` is built twice there — once demanding
    /// fractional seconds and once refusing them — and tried in that order.
    /// `FFFFFFF` makes them optional, which is the same union in one pass.
    /// A timestamp with no zone offset at all is refused by both.
    private static readonly string[] TimestampFormats =
    {
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFzzz",
    };

    /// Turns one dropped file's bytes into a normalized event, or the precise
    /// reason it was refused. A missing timestamp means "just now" from the
    /// writer's point of view and resolves to `now`.
    public static ActivityInboxResult Decode(byte[] data, System.DateTimeOffset now)
    {
        if (data.Length > MaxPayloadBytes)
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.PayloadTooLarge);
        }

        string? rawKind = null;
        string? rawSourceId = null;
        string? rawTimestamp = null;
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("sourceId", out var sourceElement)
                || sourceElement.ValueKind != JsonValueKind.String)
            {
                return ActivityInboxResult.Refused(ActivityInboxRejection.UnreadablePayload);
            }
            rawKind = kindElement.GetString();
            rawSourceId = sourceElement.GetString();

            // Absent and null are both "no timestamp" — a Swift optional decodes
            // the same either way.
            if (root.TryGetProperty("timestamp", out var timestampElement)
                && timestampElement.ValueKind != JsonValueKind.Null)
            {
                if (timestampElement.ValueKind != JsonValueKind.String)
                {
                    return ActivityInboxResult.Refused(ActivityInboxRejection.UnreadablePayload);
                }
                rawTimestamp = timestampElement.GetString();
            }
        }
        catch (JsonException)
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.UnreadablePayload);
        }

        if (ActivityEventKindExtensions.FromRawValue(rawKind!) is not ActivityEventKind kind)
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.UnknownKind);
        }
        if (!AcceptedKinds.Contains(kind))
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.KindNotAccepted);
        }

        // Lowercased before validation, so "Codex" and "codex" are one adapter
        // rather than two.
        string sourceId = rawSourceId!.ToLowerInvariant();
        if (!IsValidSourceId(sourceId))
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.InvalidSourceId);
        }
        if (ReservedSourceIds.Contains(sourceId))
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.ReservedSourceId);
        }

        System.DateTimeOffset timestamp;
        if (rawTimestamp is not null)
        {
            if (ParseTimestamp(rawTimestamp) is not System.DateTimeOffset parsed)
            {
                return ActivityInboxResult.Refused(ActivityInboxRejection.InvalidTimestamp);
            }
            timestamp = parsed;
        }
        else
        {
            timestamp = now;
        }

        if ((now - timestamp).TotalSeconds > MaxEventAge)
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.StaleTimestamp);
        }
        if ((timestamp - now).TotalSeconds > MaxFutureSkew)
        {
            return ActivityInboxResult.Refused(ActivityInboxRejection.FutureTimestamp);
        }

        return ActivityInboxResult.Accepted(new ActivityEvent(kind, timestamp, sourceId));
    }

    /// Delivery order for a drained batch: by event timestamp, oldest first.
    ///
    /// Filenames carry no ordering contract, so without this a `workEnded` file
    /// that happens to sort before its `workStarted` sibling would be reduced
    /// first — dropping the session and leaving the context stuck "working".
    public static IReadOnlyList<ActivityEvent> Ordered(IReadOnlyList<ActivityEvent> events)
    {
        var sorted = new List<ActivityEvent>(events);
        // A stable sort, like Swift's, so two events sharing a timestamp keep
        // the order they arrived in. List.Sort is not stable; OrderBy is.
        sorted = new List<ActivityEvent>(
            System.Linq.Enumerable.OrderBy(sorted, e => e.Timestamp));
        return sorted;
    }

    /// Lowercase alphanumerics plus `.`, `_` and `-`, starting alphanumeric, at
    /// most `MaxSourceIdLength` characters.
    public static bool IsValidSourceId(string sourceId)
    {
        // Graphemes, not UTF-16 units, because that is what Swift's `.count`
        // measures. Nothing outside ASCII can survive the allowed-set check
        // below, so this cannot change an answer — it keeps the two ports
        // saying the same thing for the same reason.
        if (sourceId.Length == 0 || GraphemeCount(sourceId) > MaxSourceIdLength)
        {
            return false;
        }

        foreach (char c in sourceId)
        {
            if (Allowed.IndexOf(c) < 0) return false;
        }

        char first = sourceId[0];
        return char.IsLetter(first) || char.IsDigit(first);
    }

    private static int GraphemeCount(string s)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        int count = 0;
        while (enumerator.MoveNext()) count++;
        return count;
    }

    private static System.DateTimeOffset? ParseTimestamp(string raw)
    {
        if (System.DateTimeOffset.TryParseExact(
                raw, TimestampFormats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }
}
