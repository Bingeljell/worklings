using Godot;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Worklings.Core.Connect;

/// Compares the hook merger against reference output captured from the Swift
/// original.
///
/// This is the one type in the port that edits a file the user did not give us
/// and cannot afford to lose, so the fixtures are all about what it refuses and
/// what it leaves alone: a config that is not JSON, a `hooks` value of the wrong
/// shape, a sibling hook sharing an entry with ours, a user script whose name
/// merely resembles our adapter, and top-level keys that have nothing to do with
/// us. Every one of those must survive a connect and a disconnect untouched.
///
/// **Structure is compared, not bytes.** Swift's `JSONSerialization` and .NET's
/// `Utf8JsonWriter` both pretty-print with sorted keys and disagree about
/// whitespace, which no tool reading these files can see. So both sides re-parse
/// what they wrote and dump it one leaf per line — which also puts the exact
/// command strings in the diff, where the actual risk lives.
public partial class ConnectorProbe : Node
{
    private const string Adapter = "/Applications/Worklings.app/Contents/Resources/adapters/worklings-claude-code-activity-hook";
    private const string CodexAdapter = "/Applications/Worklings.app/Contents/Resources/adapters/worklings-codex-activity-hook";

    private readonly System.Text.StringBuilder o = new();

    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    /// One leaf per line, path-sorted. Unambiguous, and it shows the commands.
    private void Dump(string label, byte[] json)
    {
        o.AppendLine($"-- {label} --");
        var lines = new List<string>();
        Walk(JsonNode.Parse(json), "", lines);
        lines.Sort(System.StringComparer.Ordinal);
        foreach (string line in lines) o.AppendLine(line);
        if (lines.Count == 0) o.AppendLine("  (empty)");
    }

    private static void Walk(JsonNode? node, string path, List<string> lines)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.Count == 0) { lines.Add($"  {path} = {{}}"); return; }
                foreach (var pair in obj) Walk(pair.Value, $"{path}.{pair.Key}", lines);
                break;
            case JsonArray array:
                if (array.Count == 0) { lines.Add($"  {path} = []"); return; }
                for (int i = 0; i < array.Count; i++) Walk(array[i], $"{path}[{i}]", lines);
                break;
            case null:
                lines.Add($"  {path} = null");
                break;
            default:
                // The raw value, not re-encoded. Swift and .NET escape
                // different characters on the way back out — slashes, quotes,
                // apostrophes — and none of that is a difference in what was
                // merged. Printing the string itself keeps the diff about the
                // structure and the commands.
                if (node is JsonValue value && value.TryGetValue(out string? text))
                {
                    lines.Add($"  {path} = \"{text}\"");
                }
                else
                {
                    lines.Add($"  {path} = {node.ToJsonString()}");
                }
                break;
        }
    }

    private void Refused(string label, System.Func<byte[]> attempt)
    {
        try
        {
            attempt();
            o.AppendLine($"-- {label} --");
            o.AppendLine("  NOT REFUSED");
        }
        catch (HookMergeException error)
        {
            o.AppendLine($"-- {label} --");
            o.AppendLine($"  refused {Lower(error.Error.ToString())}");
        }
    }

    private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);

    /// Swift prints a Bool lowercase.
    private static string Bool(bool b) => b ? "true" : "false";

    public override void _Ready()
    {
        var claude = HookConfigMerger.ClaudeCodeMappings;
        var codex = HookConfigMerger.CodexMappings;

        o.AppendLine("== mappings ==");
        foreach (var m in claude)
        {
            o.AppendLine($"claude {m.Event} -> {m.Kind} matcher {m.Matcher ?? "-"}");
        }
        foreach (var m in codex)
        {
            o.AppendLine($"codex {m.Event} -> {m.Kind} matcher {m.Matcher ?? "-"}");
        }

        o.AppendLine("== connecting ==");
        var fresh = HookConfigMerger.Connected(B(""), Adapter, claude, HookCommandStyle.ExecForm);
        Dump("claude, from nothing", fresh);
        Dump("codex, from nothing",
             HookConfigMerger.Connected(B("   \n\t "), CodexAdapter, codex, HookCommandStyle.ShellForm));

        // Reconnecting must be idempotent, not additive.
        Dump("claude, reconnected",
             HookConfigMerger.Connected(fresh, Adapter, claude, HookCommandStyle.ExecForm));

        // Everything that is not ours has to come out the other side unchanged.
        const string busy = """
        {
          "model": "opus",
          "permissions": { "allow": ["Bash(ls:*)"] },
          "hooks": {
            "SessionStart": [
              { "hooks": [ { "type": "command", "command": "/usr/local/bin/my-own-hook" } ] }
            ],
            "PreToolUse": [
              { "matcher": "Bash", "hooks": [ { "type": "command", "command": "/usr/local/bin/audit" } ] }
            ]
          }
        }
        """;
        var merged = HookConfigMerger.Connected(B(busy), Adapter, claude, HookCommandStyle.ExecForm);
        Dump("a config with the user's own hooks", merged);

        o.AppendLine("== disconnecting ==");
        Dump("ours removed, theirs kept", HookConfigMerger.Disconnected(merged, Adapter));
        // A config holding only ours loses the whole hooks object.
        Dump("nothing left behind", HookConfigMerger.Disconnected(fresh, Adapter));
        Dump("disconnecting what was never connected",
             HookConfigMerger.Disconnected(B(busy), Adapter));

        o.AppendLine("== refusing ==");
        Refused("not json",
                () => HookConfigMerger.Connected(B("{ nope"), Adapter, claude, HookCommandStyle.ExecForm));
        Refused("json, but not an object",
                () => HookConfigMerger.Connected(B("[1,2,3]"), Adapter, claude, HookCommandStyle.ExecForm));
        Refused("hooks is not an object",
                () => HookConfigMerger.Connected(B("{\"hooks\": 7}"), Adapter, claude, HookCommandStyle.ExecForm));
        Refused("an event is not an array",
                () => HookConfigMerger.Connected(B("{\"hooks\": {\"SessionStart\": \"x\"}}"),
                                                 Adapter, claude, HookCommandStyle.ExecForm));
        Refused("an event holds something that is not an entry",
                () => HookConfigMerger.Connected(B("{\"hooks\": {\"SessionStart\": [1]}}"),
                                                 Adapter, claude, HookCommandStyle.ExecForm));

        o.AppendLine("== ownership ==");
        foreach (var (label, json) in new (string, string)[]
                 {
                     ("our guarded argv form",
                      $"{{\"hooks\":{{\"Stop\":[{{\"hooks\":[{{\"type\":\"command\",\"command\":\"/bin/sh\",\"args\":[\"-c\",\"g\",\"sh\",\"{Adapter}\",\"taskCompleted\"]}}]}}]}}}}"),
                     ("our guarded shell form",
                      $"{{\"hooks\":{{\"Stop\":[{{\"hooks\":[{{\"type\":\"command\",\"command\":\"if [ -x '{Adapter}' ]; then '{Adapter}' taskCompleted; else printf '{{}}'; fi\"}}]}}]}}}}"),
                     ("an older bare-command form",
                      $"{{\"hooks\":{{\"Stop\":[{{\"hooks\":[{{\"type\":\"command\",\"command\":\"{Adapter}\"}}]}}]}}}}"),
                     ("the same adapter somewhere else on disk",
                      "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"/opt/old/worklings-claude-code-activity-hook\"}]}]}}"),
                     ("a user script with a similar name",
                      "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"/usr/local/bin/my-worklings-claude-code-activity-hook-wrapper\"}]}]}}"),
                     ("nothing of ours", "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"/bin/true\"}]}]}}"),
                     ("no hooks at all", "{\"model\":\"opus\"}"),
                     ("not json", "{ nope"),
                 })
        {
            var paths = HookConfigMerger.OurHookExecutablePaths(B(json), Adapter);
            o.AppendLine($"{label}: connected {Bool(HookConfigMerger.IsConnected(B(json), Adapter))} "
                       + $"paths {paths.Count}");
        }

        o.AppendLine("== awkward adapter paths ==");
        foreach (string path in new[]
                 {
                     "/Users/a b/Worklings.app/adapters/worklings-codex-activity-hook",
                     "/Users/o'brien/Worklings.app/adapters/worklings-codex-activity-hook",
                     "/Users/x;rm -rf ~/adapters/worklings-codex-activity-hook",
                 })
        {
            var written = HookConfigMerger.Connected(
                B(""), path, new[] { new HookMapping("Stop", "taskCompleted") },
                HookCommandStyle.ShellForm);
            Dump($"shell form for {path}", written);
            // The path has to be recoverable from what we wrote, or disconnect
            // could never find its own hook again.
            o.AppendLine($"  recognised: {Bool(HookConfigMerger.IsConnected(written, path))}");
        }

        GD.Print(o.ToString().TrimEnd());
        GetTree().Quit();
    }
}
