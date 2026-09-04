using Godot;
using Worklings.Core.Connect;

/// Runs a real connect and disconnect against a real file on disk, so the half
/// `connector_probe` cannot reach — the backup, the atomic write, the adapter
/// check, and where the adapter actually is — is exercised too.
///
/// Writes only to the path in `WORKLINGS_CONNECT_CHECK`. It must never be
/// pointed at a real `~/.claude/settings.json`.
public partial class ConnectCheck : Node
{
    public override void _Ready()
    {
        string config = System.Environment.GetEnvironmentVariable("WORKLINGS_CONNECT_CHECK") ?? "";
        if (config.Length == 0)
        {
            GD.Print("set WORKLINGS_CONNECT_CHECK to a throwaway config path");
            GetTree().Quit();
            return;
        }

        foreach (var tool in new[] { ConnectableTool.ClaudeCode, ConnectableTool.Codex })
        {
            string adapter = tool.Connector().AdapterPath;
            GD.Print($"{tool.DisplayName()} adapter: "
                   + $"{(System.IO.File.Exists(adapter) ? "found" : "MISSING")} "
                   + $"executable {ToolConnector.IsExecutableFile(adapter)}");
            GD.Print($"  config would be {tool.ConfigPath()}");
        }

        var connector = new ToolConnector(
            config,
            AdapterLocator.Path(AdapterLocator.ClaudeCodeAdapter),
            HookConfigMerger.ClaudeCodeMappings,
            HookCommandStyle.ExecForm);

        GD.Print($"before: {connector.State()}");
        try
        {
            string? backup = connector.Connect();
            GD.Print($"connect: ok, backup {(backup is null ? "none" : "written")}");
        }
        catch (System.Exception error)
        {
            GD.Print($"connect: {error.GetType().Name} {error.Message}");
            GetTree().Quit();
            return;
        }
        GD.Print($"after connect: {connector.State()}");

        // Reconnecting must not stack a second copy of our hooks.
        connector.Connect();
        GD.Print($"after reconnect: {connector.State()}");

        string? removedBackup = connector.Disconnect();
        GD.Print($"disconnect: backup {(removedBackup is null ? "none" : "written")}");
        GD.Print($"after disconnect: {connector.State()}");

        GetTree().Quit();
    }
}
