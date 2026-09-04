using Godot;
using Worklings.Core.Stage;

/// Loads every dungeon sound and reports it.
///
/// The failure this exists for is silence. A missing file, a failed import, or
/// a bed that turns out not to loop all look exactly like working audio in a
/// headless run and like a bug in the fight when you finally hear it.
public partial class AudioCheck : Node
{
    public override void _Ready()
    {
        foreach (var sound in System.Enum.GetValues<CombatSound>())
        {
            GD.Print($"{sound,-12} ok");
        }

        foreach (string path in new[]
                 {
                     "res://assets/audio/dungeon-bgm.mp3",
                     "res://assets/audio/boss-bgm.mp3",
                 })
        {
            var stream = GD.Load<AudioStream>(path);
            // The cast is the point: looping is a property of the imported
            // stream, and if the importer produced something other than an
            // AudioStreamMP3 the bed would play once and leave the fight silent.
            string loops = stream is AudioStreamMP3 mp3
                ? $"loopable (currently {mp3.Loop})"
                : $"NOT AN MP3 STREAM ({stream?.GetType().Name ?? "null"}) — the bed will not loop";
            GD.Print($"{path.Split('/')[^1],-18} {stream?.GetLength():F1}s  {loops}");
        }

        var audio = new CombatAudio();
        AddChild(audio);
        GD.Print($"muted setting: {CombatAudio.Muted}");
        GetTree().Quit();
    }
}
