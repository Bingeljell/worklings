using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Worklings.Core.Connect;

/// One hook we ask a tool to fire: the tool's event name, the activity kind it
/// means to us, and optionally which sub-events count.
public sealed record HookMapping(string Event, string Kind, string? Matcher = null);

/// How the hook command is written. Both forms **guard** the adapter with an
/// existence test, so if the app is deleted the hook degrades to a silent no-op
/// instead of a launch error — the convention dotfile tools use for lines they
/// inject into files they do not own. Both keep the path shell-safe, so a space
/// or a metacharacter can never break or be reinterpreted.
public enum HookCommandStyle
{
    /// For a tool that accepts an argv array (Claude Code). `/bin/sh -c` runs
    /// the guard and the adapter path arrives as a positional argument, so the
    /// shell never re-parses it.
    ExecForm,

    /// For a tool that accepts only a shell string (Codex). The guard is inline
    /// and the path single-quoted; a missing adapter prints an empty JSON object
    /// — a valid Stop payload — rather than failing.
    ShellForm,
}

public enum HookMergeError
{
    /// The config is present but not a JSON object. Refuse to overwrite it.
    UnparseableConfig,

    /// `hooks`, or a mapped event's value, is present but not the shape we
    /// understand. Refuse rather than erase the user's data.
    UnexpectedStructure,
}

public sealed class HookMergeException : System.Exception
{
    public HookMergeError Error { get; }

    public HookMergeException(HookMergeError error)
        : base(error.ToString()) => Error = error;
}

/// Merges Worklings' command hooks into a tool's JSON hook config.
///
/// Both tools we target use the same shape — a top-level `hooks` object mapping
/// an event name to an array of `{ "hooks": [ { "type": "command", … } ] }` — so
/// one merger serves Claude Code's `settings.json` and Codex's `hooks.json`.
///
/// **Pure and total**, so "never brick an existing config" is provable rather
/// than hoped for. It never erases structure it does not recognise: a config
/// that is not valid JSON, a `hooks` value that is not an object, or a mapped
/// event whose value is not the expected array all make `Connected` throw rather
/// than overwrite.
///
/// **Ownership is matched per hook, by the adapter's exact file name.** That
/// makes reconnecting idempotent, lets disconnecting remove only our hooks while
/// a sibling sharing the entry survives, and lets an app that has been moved
/// still recognise and clean up its own wiring. The adapter names are
/// Worklings-namespaced precisely so matching on a file name cannot accidentally
/// claim someone else's script.
///
/// Ported from Sources/CompanionCore/HookConfigMerger.swift. The JSON layer is
/// `JsonNode` where Swift uses `[String: Any]`; the output is pretty-printed
/// with sorted keys, as Swift's is, though the two writers differ in whitespace.
/// That difference is invisible to both tools and to git, which is why the probe
/// compares parsed structure rather than bytes.
public static class HookConfigMerger
{
    /// Claude Code's full lifecycle. `Notification` fires for many things, and
    /// is matched to only the types that actually mean the user is being awaited
    /// — not auth or elicitation-result notifications.
    public static readonly IReadOnlyList<HookMapping> ClaudeCodeMappings = new[]
    {
        new HookMapping("SessionStart", "workStarted"),
        new HookMapping("Stop", "taskCompleted"),
        new HookMapping("Notification", "awaitingInput",
            "permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input"),
        new HookMapping("SessionEnd", "workEnded"),
    };

    /// Codex's lifecycle. It has no documented "awaiting input" event.
    public static readonly IReadOnlyList<HookMapping> CodexMappings = new[]
    {
        new HookMapping("SessionStart", "workStarted"),
        new HookMapping("Stop", "taskCompleted"),
        new HookMapping("SessionEnd", "workEnded"),
    };

    /// Run the adapter only if it still exists and is executable, passing the
    /// kind through. The path and the kind are positional arguments, never
    /// spliced into the script text, so the shell cannot re-parse or word-split
    /// them.
    private const string ArgvGuardScript = "if [ -x \"$1\" ]; then exec \"$1\" \"$2\"; fi";

    public static byte[] Connected(
        byte[] configJson,
        string adapterPath,
        IReadOnlyList<HookMapping> mappings,
        HookCommandStyle style)
    {
        var root = Root(configJson);
        var hooks = HooksObject(root);

        foreach (var mapping in mappings)
        {
            var entries = ExistingEntries(hooks, mapping.Event);
            // Our own prior hooks are stripped first, so reconnecting is
            // idempotent rather than additive. A sibling hook sharing an entry
            // is preserved.
            entries = StrippingOurHooks(entries, adapterPath);
            entries.Add(OurEntry(adapterPath, mapping, style));
            hooks[mapping.Event] = ToArray(entries);
        }

        root["hooks"] = hooks;
        return Serialise(root);
    }

    /// The config with only our command hooks removed, at the hook level: a
    /// sibling sharing an entry is kept, an entry left with no hooks is dropped,
    /// and an emptied `hooks` object is removed entirely. Unfamiliar structure is
    /// left alone — nothing of ours could be living in it, because `Connected`
    /// would have refused to write it.
    public static byte[] Disconnected(byte[] configJson, string adapterPath)
    {
        var root = Root(configJson);
        if (root["hooks"] is not JsonObject hooks)
        {
            return Serialise(root);
        }

        var kept = new JsonObject();
        foreach (var (evt, value) in Pairs(hooks))
        {
            if (value is not JsonArray array || !LooksLikeEntries(array))
            {
                // Preserved rather than understood.
                kept[evt] = value?.DeepClone();
                continue;
            }
            var remaining = StrippingOurHooks(Entries(array), adapterPath);
            if (remaining.Count > 0)
            {
                kept[evt] = ToArray(remaining);
            }
        }

        if (kept.Count == 0)
        {
            root.Remove("hooks");
        }
        else
        {
            root["hooks"] = kept;
        }
        return Serialise(root);
    }

    /// Whether any of our command hooks are present.
    public static bool IsConnected(byte[] configJson, string adapterPath)
    {
        JsonObject root;
        try
        {
            root = Root(configJson);
        }
        catch (HookMergeException)
        {
            return false;
        }
        if (root["hooks"] is not JsonObject hooks) return false;

        foreach (var (_, value) in Pairs(hooks))
        {
            if (value is not JsonArray array) continue;
            foreach (var entry in array)
            {
                if (entry is not JsonObject e || e["hooks"] is not JsonArray list) continue;
                foreach (var hook in list)
                {
                    if (hook is JsonObject h && HookIsOurs(h, adapterPath)) return true;
                }
            }
        }
        return false;
    }

    /// Every executable path our command hooks point at.
    ///
    /// Empty when none are ours. This is what lets a caller tell "connected and
    /// pointing at a live adapter" from "connected, but that path is dead
    /// because the app moved" — ownership (the file name) and liveness (does the
    /// path still exist) are deliberately separate questions.
    public static IReadOnlyList<string> OurHookExecutablePaths(
        byte[] configJson, string adapterPath)
    {
        var paths = new List<string>();
        JsonObject root;
        try
        {
            root = Root(configJson);
        }
        catch (HookMergeException)
        {
            return paths;
        }
        if (root["hooks"] is not JsonObject hooks) return paths;

        string target = FileName(adapterPath);
        foreach (var (_, value) in Pairs(hooks))
        {
            if (value is not JsonArray array) continue;
            foreach (var entry in array)
            {
                if (entry is not JsonObject e || e["hooks"] is not JsonArray list) continue;
                foreach (var hook in list)
                {
                    if (hook is not JsonObject h) continue;
                    foreach (string candidate in AdapterCandidatePaths(h))
                    {
                        if (FileName(candidate) == target) paths.Add(candidate);
                    }
                }
            }
        }
        return paths;
    }

    // MARK: - Building our entry

    private static JsonObject OurEntry(
        string adapterPath, HookMapping mapping, HookCommandStyle style)
    {
        JsonObject hook;
        if (style == HookCommandStyle.ExecForm)
        {
            // /bin/sh runs the guard; the path and the kind are positional
            // arguments, so a deleted adapter is a silent no-op and the path
            // needs no quoting at all.
            hook = new JsonObject
            {
                ["type"] = "command",
                ["command"] = "/bin/sh",
                ["args"] = new JsonArray("-c", ArgvGuardScript, "sh", adapterPath, mapping.Kind),
            };
        }
        else
        {
            // An inline guard in one shell string: run the single-quoted path if
            // it exists, else print an empty JSON object, so a deleted adapter
            // still returns a valid content-free success instead of erroring.
            string quoted = SingleQuoted(adapterPath);
            hook = new JsonObject
            {
                ["type"] = "command",
                ["command"] =
                    $"if [ -x {quoted} ]; then {quoted} {mapping.Kind}; else printf '{{}}'; fi",
            };
        }

        var entry = new JsonObject { ["hooks"] = new JsonArray(hook) };
        if (mapping.Matcher is string matcher)
        {
            entry["matcher"] = matcher;
        }
        return entry;
    }

    /// POSIX single-quoting: everything inside is literal, and an embedded quote
    /// is closed, escaped and reopened. Neutralises spaces and metacharacters.
    private static string SingleQuoted(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    // MARK: - Reading structure (refuse, never erase)

    private static JsonObject HooksObject(JsonObject root)
    {
        if (root["hooks"] is null) return new JsonObject();
        if (root["hooks"] is not JsonObject hooks) throw Refuse(HookMergeError.UnexpectedStructure);
        return (JsonObject)hooks.DeepClone();
    }

    private static List<JsonObject> ExistingEntries(JsonObject hooks, string evt)
    {
        if (hooks[evt] is null) return new List<JsonObject>();
        if (hooks[evt] is not JsonArray array || !LooksLikeEntries(array))
        {
            throw Refuse(HookMergeError.UnexpectedStructure);
        }
        return Entries(array);
    }

    /// Swift's cast to `[[String: Any]]` fails unless *every* element is an
    /// object, and that failure is what makes the merger refuse. C#'s type test
    /// is per element, so the same question has to be asked explicitly.
    private static bool LooksLikeEntries(JsonArray array)
    {
        foreach (var element in array)
        {
            if (element is not JsonObject) return false;
        }
        return true;
    }

    private static List<JsonObject> Entries(JsonArray array)
    {
        var entries = new List<JsonObject>();
        foreach (var element in array)
        {
            // Cloned out of the source tree: a JsonNode has one parent, and
            // moving a live node into the tree being built detaches it from the
            // one being read.
            entries.Add((JsonObject)element!.DeepClone());
        }
        return entries;
    }

    // MARK: - Ownership (per hook, exact file name)

    private static List<JsonObject> StrippingOurHooks(
        List<JsonObject> entries, string adapterPath)
    {
        var result = new List<JsonObject>();
        foreach (var entry in entries)
        {
            if (entry["hooks"] is not JsonArray list)
            {
                result.Add(entry); // entries we do not understand are left alone
                continue;
            }

            var kept = new JsonArray();
            int total = 0;
            foreach (var hook in list)
            {
                total++;
                if (hook is JsonObject h && HookIsOurs(h, adapterPath)) continue;
                kept.Add(hook!.DeepClone());
            }

            if (kept.Count == total)
            {
                result.Add(entry); // nothing of ours here
            }
            else if (kept.Count > 0)
            {
                entry["hooks"] = kept; // a sibling hook remains
                result.Add(entry);
            }
            // else: the entry held only ours — drop it
        }
        return result;
    }

    private static bool HookIsOurs(JsonObject hook, string adapterPath)
    {
        // Ownership is the adapter's distinctive file name, found anywhere a
        // hook could name it: the command itself, a quoted word inside a command
        // string, or an element of args. A moved or reinstalled bundle keeps the
        // file name and is still recognised; the name is Worklings-namespaced,
        // so a differently-named user script is never claimed.
        string target = FileName(adapterPath);
        foreach (string candidate in AdapterCandidatePaths(hook))
        {
            if (FileName(candidate) == target) return true;
        }
        return false;
    }

    /// Every string in a hook that could be the adapter path: the whole command,
    /// each shell word of it (recovering a single-quoted path), and each `args`
    /// element.
    private static List<string> AdapterCandidatePaths(JsonObject hook)
    {
        var candidates = new List<string>();
        if (hook["command"] is JsonValue commandValue
            && commandValue.TryGetValue(out string? command) && command is not null)
        {
            candidates.Add(command);                 // old exec form: the command is the path
            candidates.AddRange(ShellWords(command)); // shell forms: a quoted word
        }
        if (hook["args"] is JsonArray args)
        {
            foreach (var arg in args)
            {
                if (arg is JsonValue value && value.TryGetValue(out string? text)
                    && text is not null)
                {
                    candidates.Add(text);            // guarded argv form: an argument
                }
            }
        }
        return candidates;
    }

    /// The last path component, without asking the platform.
    ///
    /// `Path.GetFileName` splits on the *host's* separator, so on Windows it
    /// would also split a POSIX path on backslashes — and these paths come out
    /// of a config file written on some other machine as often as from this one.
    private static string FileName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// Splits a shell command into words, honouring single quotes, double quotes
    /// and backslash escapes — enough to recover a single-quoted path, including
    /// one whose value contains an apostrophe.
    private static List<string> ShellWords(string command)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        bool hasWord = false;
        int i = 0;

        while (i < command.Length)
        {
            char c = command[i];
            switch (c)
            {
                case ' ':
                case '\t':
                case '\n':
                    if (hasWord)
                    {
                        words.Add(current.ToString());
                        current.Clear();
                        hasWord = false;
                    }
                    i++;
                    break;
                case '\'':
                    hasWord = true;
                    i++;
                    while (i < command.Length && command[i] != '\'') current.Append(command[i++]);
                    if (i < command.Length) i++; // closing quote
                    break;
                case '"':
                    hasWord = true;
                    i++;
                    while (i < command.Length && command[i] != '"')
                    {
                        if (command[i] == '\\' && i + 1 < command.Length)
                        {
                            i++;
                            current.Append(command[i++]);
                            continue;
                        }
                        current.Append(command[i++]);
                    }
                    if (i < command.Length) i++; // closing quote
                    break;
                case '\\':
                    hasWord = true;
                    i++;
                    if (i < command.Length) current.Append(command[i++]);
                    break;
                default:
                    hasWord = true;
                    current.Append(c);
                    i++;
                    break;
            }
        }
        if (hasWord) words.Add(current.ToString());
        return words;
    }

    // MARK: - JSON

    private static HookMergeException Refuse(HookMergeError error) => new(error);

    /// A blank file is an empty object, not a parse failure — a tool that has
    /// been installed but never configured leaves one behind.
    private static JsonObject Root(byte[] configJson)
    {
        bool blank = true;
        foreach (byte b in configJson)
        {
            if (b is not (0x20 or 0x0A or 0x0D or 0x09)) { blank = false; break; }
        }
        if (configJson.Length == 0 || blank) return new JsonObject();

        try
        {
            if (JsonNode.Parse(configJson) is JsonObject root)
            {
                return (JsonObject)root.DeepClone();
            }
        }
        catch (JsonException)
        {
            // Falls through to the same refusal as a non-object.
        }
        throw Refuse(HookMergeError.UnparseableConfig);
    }

    private static JsonArray ToArray(List<JsonObject> entries)
    {
        var array = new JsonArray();
        foreach (var entry in entries) array.Add(entry.DeepClone());
        return array;
    }

    /// A snapshot of an object's pairs, so a caller can rebuild it while reading.
    private static List<KeyValuePair<string, JsonNode?>> Pairs(JsonObject o)
    {
        var pairs = new List<KeyValuePair<string, JsonNode?>>();
        foreach (var pair in o) pairs.Add(pair);
        return pairs;
    }

    /// Pretty-printed with **sorted keys**, matching Swift's
    /// `[.prettyPrinted, .sortedKeys]`. Sorted so a reconnect produces the same
    /// file rather than a reshuffled one — the diff a user sees in their own
    /// config should be only what actually changed.
    private static byte[] Serialise(JsonObject root)
    {
        var stream = new System.IO.MemoryStream();
        // The relaxed encoder, deliberately. .NET's default escapes `'`, `\"`,
        // `<`, `>` and `&` as \uXXXX for HTML safety — which is correct for a
        // web payload and wrong for a config file a person opens and reads. A
        // shell-form hook is full of apostrophes, and `\u0027` everywhere would
        // make the user's own settings.json unreadable to defend against a
        // browser that is never going to see it.
        using (var writer = new Utf8JsonWriter(
                   stream, new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder
                           .UnsafeRelaxedJsonEscaping,
                   }))
        {
            WriteSorted(writer, root);
        }
        return stream.ToArray();
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                writer.WriteStartObject();
                var keys = new List<string>();
                foreach (var pair in o) keys.Add(pair.Key);
                // Ordinal, so the order does not depend on the machine's locale.
                keys.Sort(System.StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    writer.WritePropertyName(key);
                    WriteSorted(writer, o[key]);
                }
                writer.WriteEndObject();
                break;
            }
            case JsonArray a:
                writer.WriteStartArray();
                foreach (var element in a) WriteSorted(writer, element);
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
