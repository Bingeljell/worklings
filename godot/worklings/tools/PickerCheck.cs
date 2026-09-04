using Godot;
using Worklings.Core.Host;
using Worklings.Core.Pet;

/// Opens the repository picker and drives its signal, without a person clicking
/// anything.
///
/// This exists because the previous picker — the OS native one — opened
/// correctly and its callback never arrived, and there was no way to find that
/// out except by asking someone to try it. A Godot `FileDialog` emits an
/// ordinary signal, so the whole path from "a folder was chosen" to "the pet
/// says it is watching" can be checked here.
public partial class PickerCheck : Node
{
    public override async void _Ready()
    {
        string repo = System.Environment.GetEnvironmentVariable("WORKLINGS_GIT_CHECK") ?? "";

        // The pet window's setting, mirrored. Without it a 900x600 dialog is
        // drawn INSIDE a 320-pixel window and clipped to it — the same trap the
        // right-click menu fell into, and the reason this line matters more than
        // it looks.
        GetWindow().GuiEmbedSubwindows = false;
        GetWindow().Size = new Vector2I(320, 320);

        var picker = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = false,
            Title = "Connect a repository",
        };

        string chosen = "";
        picker.DirSelected += path => chosen = path;
        picker.FileSelected += path => chosen = path;
        AddChild(picker);

        var frame = DesktopWindow.UsableFrame(0);
        var size = new Vector2I(900, 600);
        picker.Popup(new Rect2I(
            new Vector2I((int)(frame.X + (frame.Width - size.X) / 2),
                         (int)(frame.Y + (frame.Height - size.Y) / 2)),
            size));
        await ToSignal(GetTree().CreateTimer(0.5), "timeout");
        GD.Print($"dialog visible: {picker.Visible}  size {picker.Size}  "
               + $"native {picker.UseNativeDialog}");
        // Its own OS window rather than something drawn inside ours, which is
        // what makes it usable at all from a 320-pixel pet.
        GD.Print($"embedded in the parent viewport: {picker.IsEmbedded()}");

        // The signal a real click produces. If this does not reach the handler,
        // nothing else in the chain matters.
        picker.EmitSignal(FileDialog.SignalName.DirSelected, repo);
        await ToSignal(GetTree().CreateTimer(0.1), "timeout");
        GD.Print($"signal delivered: {(chosen == repo ? "yes" : $"NO — got \"{chosen}\"")}");

        var session = new PetSession(
            System.DateTimeOffset.Now,
            save: new SaveLocation(
                ProjectSettings.GlobalizePath("user://picker-check/pet-state.json"),
                IsShared: false, Reason: "picker check"));
        var watcher = new GitCommitWatcher(
            session, new ConnectedRepoRegistry("user://picker-check/connected-repos.json"));
        AddChild(watcher);
        GD.Print($"connecting it: {watcher.Connect(chosen) ?? "connected"}");
        foreach (var r in watcher.Connected) GD.Print($"  now watching {PetMenu.ShortPath(r.Path)}");

        GetTree().Quit();
    }
}
