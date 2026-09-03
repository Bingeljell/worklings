using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Worklings.Core.Progression;

namespace Worklings.Core.Pet;

public sealed class PetStateCodecException : System.Exception
{
    public PetStateCodecException(string message) : base(message) { }
}

/// Reads and writes the save file's JSON, byte-for-byte as Swift's
/// `JSONEncoder`/`JSONDecoder` do it.
///
/// Ported from the `Codable` conformances in
/// Sources/CompanionCore/PetState.swift — which are synthesized for most types
/// and hand-written for PetState, PetNeeds and Loadout. There is no C# analogue
/// of a synthesized conformance, so the whole shape is spelled out here rather
/// than scattered across the types as attributes. That is deliberate: the save
/// format is one contract with one owner, and the field defaults below *are* the
/// migration rules.
///
/// **Byte-identical is the requirement, not a flourish.** The same file has to be
/// readable by both the Swift app and the Godot build for as long as both exist,
/// so the encoder reproduces Foundation's pretty-printing exactly: keys sorted
/// ordinally, two-space indent, `" : "` between key and value, and an empty
/// object written as a blank line between the braces. Anything less and the two
/// implementations can only be compared by eye.
///
/// Dates are Swift's `Date` on the wire: a bare number of seconds since the
/// reference date, 2001-01-01 UTC. That is `JSONEncoder`'s default strategy, not
/// a choice made here.
public static class PetStateCodec
{
    /// Swift's `Date` epoch — 2001-01-01 00:00:00 UTC, not 1970.
    public static readonly System.DateTimeOffset ReferenceDate =
        new System.DateTimeOffset(2001, 1, 1, 0, 0, 0, System.TimeSpan.Zero);

    public static double ToSwiftDate(System.DateTimeOffset date) =>
        (date - ReferenceDate).TotalSeconds;

    public static System.DateTimeOffset FromSwiftDate(double interval) =>
        ReferenceDate.AddTicks((long)System.Math.Round(interval * System.TimeSpan.TicksPerSecond));

    // Encoding

    public static string Encode(PetState state)
    {
        var root = JNode.Object();
        root.Set("schemaVersion", JNode.Int(state.SchemaVersion));
        root.Set("name", JNode.String(state.Name));
        root.Set("family", JNode.String(state.Family.RawValue()));
        root.Set("needs", EncodeNeeds(state.Needs));
        root.Set("preferences", EncodePreferences(state.Preferences));
        root.Set("lastUpdatedAt", JNode.Double(ToSwiftDate(state.LastUpdatedAt)));
        // Swift's synthesized encoder writes optionals with `encodeIfPresent`,
        // so a nil is an absent key rather than a null. Matched here because a
        // written null would decode the same but diff differently.
        if (state.LastWorkLogAt.HasValue)
        {
            root.Set("lastWorkLogAt", JNode.Double(ToSwiftDate(state.LastWorkLogAt.Value)));
        }
        root.Set("workLog", EncodeTally(state.WorkLog, JNode.Int));
        root.Set("totalXP", JNode.Double(state.TotalXP));
        root.Set("petClass", JNode.String(state.PetClass.RawValue()));
        root.Set("stats", EncodeStats(state.Stats));
        root.Set("dailyXP", EncodeTally(state.DailyXP, EncodeDoubleMap));
        root.Set("dailyEventCount", EncodeTally(state.DailyEventCount, EncodeIntMap));
        var items = JNode.Array();
        foreach (var item in state.OwnedItems)
        {
            items.Add(JNode.String(item.RawValue()));
        }
        root.Set("ownedItems", items);
        root.Set("loadout", EncodeLoadout(state.Loadout));

        var builder = new StringBuilder();
        root.Render(builder, 0);
        return builder.ToString();
    }

    private static JNode EncodeNeeds(PetNeeds needs)
    {
        var node = JNode.Object();
        node.Set("hunger", JNode.Double(needs.Hunger));
        node.Set("energy", JNode.Double(needs.Energy));
        node.Set("happiness", JNode.Double(needs.Happiness));
        node.Set("trust", JNode.Double(needs.Trust));
        return node;
    }

    private static JNode EncodePreferences(PetPreferences preferences)
    {
        var node = JNode.Object();
        node.Set("favouriteFood", JNode.String(preferences.FavouriteFood.RawValue()));
        node.Set(
            "favouritePlayActivity",
            JNode.String(preferences.FavouritePlayActivity.RawValue()));
        return node;
    }

    private static JNode EncodeStats(PetStats stats)
    {
        var node = JNode.Object();
        node.Set("vitality", JNode.Int(stats.Vitality));
        node.Set("power", JNode.Int(stats.Power));
        node.Set("defense", JNode.Int(stats.Defense));
        node.Set("agility", JNode.Int(stats.Agility));
        node.Set("wit", JNode.Int(stats.Wit));
        return node;
    }

    private static JNode EncodeLoadout(Loadout loadout)
    {
        var node = JNode.Object();
        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            var item = loadout[slot];
            if (item.HasValue)
            {
                node.Set(slot.RawValue(), JNode.String(item.Value.RawValue()));
            }
        }
        return node;
    }

    private static JNode EncodeDoubleMap(Dictionary<string, double> map)
    {
        var node = JNode.Object();
        foreach (var pair in map)
        {
            node.Set(pair.Key, JNode.Double(pair.Value));
        }
        return node;
    }

    private static JNode EncodeIntMap(Dictionary<string, int> map)
    {
        var node = JNode.Object();
        foreach (var pair in map)
        {
            node.Set(pair.Key, JNode.Int(pair.Value));
        }
        return node;
    }

    private static JNode EncodeTally<TValue>(
        DailyTally<TValue> tally,
        System.Func<TValue, JNode> encodeValue)
    {
        var node = JNode.Object();
        if (tally.Date.HasValue)
        {
            node.Set("date", JNode.Double(ToSwiftDate(tally.Date.Value)));
        }
        node.Set("value", encodeValue(tally.Value));
        return node;
    }

    // Decoding

    public static PetState Decode(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            throw new PetStateCodecException($"save is not valid JSON: {error.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PetStateCodecException("save is not a JSON object");
            }

            // The legacy pre-v2 flat fields are read here and nowhere else.
            // Folding them in on the way through means nothing downstream ever
            // sees a v1 shape, and the encoder above never writes them back.
            var workLog = DecodeTally(root, "workLog", DecodeInt, 0)
                ?? new DailyTally<int>(
                    OptionalInt(root, "workLogCountToday") ?? 0,
                    OptionalDate(root, "workLogCountDate"));

            var dailyXP = DecodeTally(root, "dailyXP", DecodeDoubleMap, new Dictionary<string, double>())
                ?? new DailyTally<Dictionary<string, double>>(
                    Child(root, "dailyXPBySource") is JsonElement source
                        ? DecodeDoubleMap(source)
                        : new Dictionary<string, double>(),
                    OptionalDate(root, "dailyXPDate"));

            // Introduced after dailyXP; a save without it starts the day's
            // per-source counts empty, so no diminishing-returns history carries
            // over a version bump. There is no legacy flat field to fold.
            var dailyEventCount =
                DecodeTally(root, "dailyEventCount", DecodeIntMap, new Dictionary<string, int>())
                ?? new DailyTally<Dictionary<string, int>>(new Dictionary<string, int>());

            // Gear is additive to the schema, so an older save simply has no item
            // fields. It reads as the *starter* loadout rather than as nothing —
            // exactly what a pet created today would get — so a save predating
            // gear isn't left with an empty inventory it can never fill. Same
            // posture as stats, which defaults to a starting sheet, not zeroes.
            IReadOnlyList<Item>? ownedItems = null;
            if (Child(root, "ownedItems") is JsonElement itemsElement)
            {
                var list = new List<Item>();
                foreach (var element in itemsElement.EnumerateArray())
                {
                    list.Add(ParseItem(element.GetString()));
                }
                ownedItems = list;
            }

            Loadout? loadout = null;
            if (Child(root, "loadout") is JsonElement loadoutElement)
            {
                loadout = new Loadout(
                    tool: OptionalItem(loadoutElement, "tool"),
                    ward: OptionalItem(loadoutElement, "ward"),
                    charm: OptionalItem(loadoutElement, "charm"));
            }

            // Everything routes through the validating constructor rather than
            // stored properties, or a save becomes the one path that can equip a
            // phantom item.
            return new PetState(
                schemaVersion: RequiredInt(root, "schemaVersion"),
                name: RequiredString(root, "name"),
                family: OptionalEnum(
                    root, "family", PetFamilyExtensions.AllCases, PetFamilyExtensions.RawValue)
                    ?? PetFamily.Wildkin,
                needs: DecodeNeeds(Required(root, "needs")),
                preferences: DecodePreferences(Required(root, "preferences")),
                lastUpdatedAt: FromSwiftDate(RequiredDouble(root, "lastUpdatedAt")),
                lastWorkLogAt: OptionalDate(root, "lastWorkLogAt"),
                workLog: workLog,
                totalXP: OptionalDouble(root, "totalXP") ?? 0,
                petClass: OptionalEnum(
                    root, "petClass", PetClassExtensions.AllCases, PetClassExtensions.RawValue)
                    ?? PetClass.Wellspring,
                stats: Child(root, "stats") is JsonElement statsElement
                    ? DecodeStats(statsElement)
                    : PetStats.Starting,
                dailyXP: dailyXP,
                dailyEventCount: dailyEventCount,
                ownedItems: ownedItems,
                loadout: loadout);
        }
    }

    private static PetNeeds DecodeNeeds(JsonElement element) =>
        new PetNeeds(
            hunger: RequiredDouble(element, "hunger"),
            energy: RequiredDouble(element, "energy"),
            happiness: RequiredDouble(element, "happiness"),
            trust: RequiredDouble(element, "trust"));

    private static PetPreferences DecodePreferences(JsonElement element) =>
        new PetPreferences(
            OptionalEnum(
                element, "favouriteFood",
                PetNeedsEnumExtensions.AllFood, PetNeedsEnumExtensions.RawValue)
                ?? throw new PetStateCodecException("preferences.favouriteFood is missing"),
            OptionalEnum(
                element, "favouritePlayActivity",
                PetNeedsEnumExtensions.AllPlayActivities, PetNeedsEnumExtensions.RawValue)
                ?? throw new PetStateCodecException(
                    "preferences.favouritePlayActivity is missing"));

    private static PetStats DecodeStats(JsonElement element) =>
        new PetStats(
            vitality: RequiredInt(element, "vitality"),
            power: RequiredInt(element, "power"),
            defense: RequiredInt(element, "defense"),
            agility: RequiredInt(element, "agility"),
            wit: RequiredInt(element, "wit"));

    private static Dictionary<string, double> DecodeDoubleMap(JsonElement element)
    {
        var map = new Dictionary<string, double>();
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value.GetDouble();
        }
        return map;
    }

    private static Dictionary<string, int> DecodeIntMap(JsonElement element)
    {
        var map = new Dictionary<string, int>();
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value.GetInt32();
        }
        return map;
    }

    private static int DecodeInt(JsonElement element) => element.GetInt32();

    /// Null when the key is absent, so the caller can fall back to the legacy
    /// flat pair rather than to a default it can't tell apart from a real one.
    private static DailyTally<TValue>? DecodeTally<TValue>(
        JsonElement parent,
        string key,
        System.Func<JsonElement, TValue> decodeValue,
        TValue fallbackValue)
    {
        if (Child(parent, key) is not JsonElement element)
        {
            return null;
        }
        return new DailyTally<TValue>(
            Child(element, "value") is JsonElement value ? decodeValue(value) : fallbackValue,
            OptionalDate(element, "date"));
    }

    // Element helpers. A JSON null reads as absent throughout, matching Swift's
    // `decodeIfPresent`.

    private static JsonElement? Child(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var value)
            && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static JsonElement Required(JsonElement parent, string key) =>
        Child(parent, key) ?? throw new PetStateCodecException($"save is missing \"{key}\"");

    private static string RequiredString(JsonElement parent, string key) =>
        Required(parent, key).GetString()
        ?? throw new PetStateCodecException($"\"{key}\" is not a string");

    private static int RequiredInt(JsonElement parent, string key) =>
        Required(parent, key).GetInt32();

    private static double RequiredDouble(JsonElement parent, string key) =>
        Required(parent, key).GetDouble();

    private static int? OptionalInt(JsonElement parent, string key) =>
        Child(parent, key)?.GetInt32();

    private static double? OptionalDouble(JsonElement parent, string key) =>
        Child(parent, key)?.GetDouble();

    private static System.DateTimeOffset? OptionalDate(JsonElement parent, string key) =>
        Child(parent, key) is JsonElement element ? FromSwiftDate(element.GetDouble()) : null;

    private static Item? OptionalItem(JsonElement parent, string key) =>
        Child(parent, key) is JsonElement element ? ParseItem(element.GetString()) : null;

    private static Item ParseItem(string? raw)
    {
        foreach (var item in ItemExtensions.AllCases)
        {
            if (item.RawValue() == raw)
            {
                return item;
            }
        }
        throw new PetStateCodecException($"unknown item \"{raw}\"");
    }

    /// Matches on the Swift `rawValue` rather than on the C# member name, which
    /// differs in case and would silently miss.
    private static TEnum? OptionalEnum<TEnum>(
        JsonElement parent,
        string key,
        TEnum[] cases,
        System.Func<TEnum, string> rawValue)
        where TEnum : struct
    {
        if (Child(parent, key) is not JsonElement element)
        {
            return null;
        }
        string? raw = element.GetString();
        foreach (var value in cases)
        {
            if (rawValue(value) == raw)
            {
                return value;
            }
        }
        throw new PetStateCodecException($"unknown {key} \"{raw}\"");
    }

    /// A minimal JSON value tree, written only so the output can match
    /// Foundation's pretty-printer. Utf8JsonWriter cannot: it puts no spaces
    /// around the colon and writes an empty object as `{}`.
    private sealed class JNode
    {
        private enum Kind { Object, Array, String, Raw }

        private Kind kind;
        private string text = "";
        private readonly List<KeyValuePair<string, JNode>> members = new();
        private readonly List<JNode> elements = new();

        public static JNode Object() => new JNode { kind = Kind.Object };
        public static JNode Array() => new JNode { kind = Kind.Array };
        public static JNode String(string value) => new JNode { kind = Kind.String, text = value };
        public static JNode Int(int value) =>
            new JNode { kind = Kind.Raw, text = value.ToString(CultureInfo.InvariantCulture) };

        /// "R" renders an integral double without a trailing ".0", which is what
        /// Foundation does — `12.0` on the wire is `12`.
        public static JNode Double(double value) =>
            new JNode { kind = Kind.Raw, text = value.ToString("R", CultureInfo.InvariantCulture) };

        public void Set(string key, JNode value) =>
            members.Add(new KeyValuePair<string, JNode>(key, value));

        public void Add(JNode value) => elements.Add(value);

        public void Render(StringBuilder builder, int level)
        {
            switch (kind)
            {
                case Kind.Raw:
                    builder.Append(text);
                    return;
                case Kind.String:
                    AppendQuoted(builder, text);
                    return;
                case Kind.Array:
                    RenderBody(builder, level, "[", "]", elements.Count, index =>
                    {
                        elements[index].Render(builder, level + 1);
                    });
                    return;
                default:
                    // Foundation's `.sortedKeys` is an ordinal sort of the UTF-8
                    // bytes; Ordinal is the same comparison for these keys.
                    members.Sort((lhs, rhs) =>
                        string.CompareOrdinal(lhs.Key, rhs.Key));
                    RenderBody(builder, level, "{", "}", members.Count, index =>
                    {
                        AppendQuoted(builder, members[index].Key);
                        builder.Append(" : ");
                        members[index].Value.Render(builder, level + 1);
                    });
                    return;
            }
        }

        private static void RenderBody(
            StringBuilder builder,
            int level,
            string open,
            string close,
            int count,
            System.Action<int> renderItem)
        {
            builder.Append(open);
            if (count == 0)
            {
                // Foundation writes an empty container as a bare blank line
                // between the brackets, not as `{}`.
                builder.Append('\n').Append('\n').Append(' ', level * 2).Append(close);
                return;
            }
            for (int index = 0; index < count; index++)
            {
                builder.Append(index == 0 ? "\n" : ",\n").Append(' ', (level + 1) * 2);
                renderItem(index);
            }
            builder.Append('\n').Append(' ', level * 2).Append(close);
        }

        private static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
