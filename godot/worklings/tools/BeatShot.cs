using Godot;
using System.Collections.Generic;

/// Photographs the dungeon **at the moments it changes**, not on a timer.
///
/// `SliceShot` samples every sixth frame of a whole run, which is the honest way
/// to review pacing and a terrible way to review anything else: a beat the
/// player sees for half a second gets one or two frames somewhere in six hundred
/// PNGs of two creatures standing still, and finding it means scrubbing. The
/// intent badge, the countdown and the frame a blow lands on were each found
/// that way, one at a time, and each cost a full run plus a hunt.
///
/// This listens to `CacheWarrenScene.Beat` instead, so the scene says when it
/// has become worth looking at. A four-encounter delve produces on the order of
/// forty labelled frames — `e2-r3-hit-Fren.png`, `e4-r1-intends-WindUp.png` —
/// and every one of them is a state somebody wanted to check.
///
/// Runs with `WORKLINGS_FAST=1` by default, because pacing is exactly the thing
/// this tool is NOT reviewing. Set `WORKLINGS_BEATSHOT_REALTIME=1` to keep the
/// shipped timings.
///
/// The frame is grabbed one frame *after* the signal, because the signal fires
/// while the scene is still building the state — the label is written, the badge
/// is placed, and none of it has been drawn yet.
public partial class BeatShot : Node
{
    private string _out = "";
    private int _saved;
    private readonly Queue<string> _due = new();
    private CacheWarrenScene _dungeon = null!;

    /// A ceiling, so a delve that loops forever cannot fill a disk.
    private const int MaxShots = 240;

    /// A hard stop, in frames. `Finished` is the normal exit and this is the
    /// one that saves an unattended run from hanging a terminal for seven
    /// minutes when it does not fire — which is exactly what happened the first
    /// time this tool was run.
    private const int MaxFrames = 60 * 60 * 6;
    private int _frames;

    public override void _Ready()
    {
        _out = OS.GetEnvironment("WORKLINGS_BEAT_OUT");
        if (_out.Length == 0) _out = ProjectSettings.GlobalizePath("user://beat-shots");

        if (OS.GetEnvironment("WORKLINGS_SAVE").Length == 0)
        {
            GD.PrintErr("BeatShot refuses to run without WORKLINGS_SAVE set. "
                      + "The dungeon writes a Workling back when a run resolves, and "
                      + "the real pet file is not a thing a capture tool may touch.");
            GetTree().Quit(1);
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(_out);
        foreach (var stale in DirAccess.GetFilesAt(_out))
        {
            if (stale.EndsWith(".png")) DirAccess.RemoveAbsolute($"{_out}/{stale}");
        }

        var scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn");
        _dungeon = scene.Instantiate<CacheWarrenScene>();
        _dungeon.AutoPlay = true;
        _dungeon.Loop = false;
        if (OS.GetEnvironment("WORKLINGS_BEATSHOT_REALTIME").Length == 0)
        {
            _dungeon.FastMode = true;
        }
        _dungeon.Beat += label => _due.Enqueue(label);
        _dungeon.Finished += () =>
        {
            GD.Print($"BeatShot wrote {_saved} frames to {_out}");
            GetTree().Quit();
        };
        AddChild(_dungeon);

        GD.Print($"BeatShot -> {_out}");
    }

    public override void _Process(double _)
    {
        if (++_frames > MaxFrames)
        {
            GD.PrintErr($"BeatShot gave up after {_frames} frames with {_saved} shots — "
                      + "the delve never reported finishing.");
            GetTree().Quit(1);
            return;
        }
        if (_due.Count == 0) return;
        var label = _due.Dequeue();
        if (_saved >= MaxShots) return;

        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null) return;
        // Numbered as well as named, so the sheet reads in the order it happened
        // and two identical beats in one run do not overwrite each other.
        var safe = label.Replace(" ", "-").Replace("/", "-");
        var error = image.SavePng($"{_out}/{_saved:D3}_{safe}.png");
        if (error != Error.Ok)
        {
            GD.PrintErr($"BeatShot could not write {label}: {error}");
            GetTree().Quit(1);
            return;
        }
        _saved++;
    }
}
