using Godot;
using System.Collections.Generic;

namespace Worklings.Core.Stage;

/// The one-shot cues a fight makes.
///
/// Named for the beat rather than the file, so swapping an audition variant is a
/// change to one path table rather than to every call site.
public enum CombatSound
{
    Hit,
    Crit,
    Slam,
    Dodge,
    Unleash,
    Brace,
    Tick,
    Victory,
    Defeat,
    Snare,
    Phase,
    Telegraph,
    Harden,
    Poof,
    Enter,
    ReturnChime,
}

/// The dungeon's audio: a looping bed under a set of one-shot cues.
///
/// **Combat-scoped on purpose.** Nothing here sounds outside a delve. The pet on
/// your desktop is silent, because a companion that chirps while you are working
/// is a companion people quit.
///
/// Rebuilt against Sources/Worklings/CombatAudio.swift — app code rather than
/// `CompanionCore`, so the decisions carry over and the implementation does not.
/// Defensive in the same way: a missing file warns once and no-ops rather than
/// taking a fight down with it.
///
/// One player per cue, kept alive for the scene's lifetime, so a hit that lands
/// while the last one is still ringing restarts it instead of allocating. That
/// also means two hits cannot overlap — which is the right trade for combat,
/// where a doubled cue reads as a glitch rather than as two blows.
public sealed partial class CombatAudio : Node
{
    /// The bed sits under the cues, and both are then scaled by the master.
    private const float BgmRelativeVolume = 0.5f;
    private const string SettingsPath = "user://audio.cfg";
    private const string Section = "dungeon";

    private static readonly Dictionary<CombatSound, string> Files = new()
    {
        [CombatSound.Hit] = "combat-hit.wav",
        [CombatSound.Crit] = "combat-crit.wav",
        [CombatSound.Slam] = "combat-slam.wav",
        [CombatSound.Dodge] = "combat-dodge.wav",
        [CombatSound.Unleash] = "combat-unleash.wav",
        [CombatSound.Brace] = "combat-brace.wav",
        [CombatSound.Tick] = "countdown-tick.wav",
        [CombatSound.Victory] = "victory-fanfare.wav",
        [CombatSound.Defeat] = "defeat-sting.wav",
        [CombatSound.Snare] = "foe-snare.wav",
        [CombatSound.Phase] = "foe-phase.wav",
        [CombatSound.Telegraph] = "foe-telegraph.wav",
        [CombatSound.Harden] = "foe-harden.wav",
        [CombatSound.Poof] = "foe-poof.wav",
        [CombatSound.Enter] = "encounter-enter.wav",
        [CombatSound.ReturnChime] = "return-chime.wav",
    };

    private const string DungeonBgm = "res://assets/audio/dungeon-bgm.mp3";
    private const string BossBgm = "res://assets/audio/boss-bgm.mp3";

    private readonly Dictionary<CombatSound, AudioStreamPlayer> _players = new();
    private AudioStreamPlayer _bgm = null!;
    private string _currentBgm = "";

    private bool _muted;
    private float _master = 0.8f;

    /// The stored mute flag, readable and writable without a live instance.
    ///
    /// The menu that toggles this is on the desktop pet, and `CombatAudio` only
    /// exists while a delve is running — the two are never alive at the same
    /// time. So the setting is the file, and an instance reads it when it starts.
    public static bool Muted
    {
        get
        {
            var config = new ConfigFile();
            return config.Load(SettingsPath) == Error.Ok
                   && (bool)config.GetValue(Section, "muted", false);
        }
        set
        {
            var config = new ConfigFile();
            config.Load(SettingsPath);
            config.SetValue(Section, "muted", value);
            config.Save(SettingsPath);
        }
    }

    /// Whether the dungeon is silenced. Persisted, and off by default — a delve
    /// is something the player chose to enter, so the bed only ever plays inside
    /// a fight they asked for.
    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            Save();
            if (_muted) StopBgm();
        }
    }

    /// Master level, 0 to 1. Scales every cue and the bed, and updates a playing
    /// bed live rather than at the next encounter.
    public float MasterVolume
    {
        get => _master;
        set
        {
            _master = Mathf.Clamp(value, 0, 1);
            Save();
            if (_bgm is not null) _bgm.VolumeDb = Db(BgmRelativeVolume * _master);
        }
    }

    public override void _Ready()
    {
        Load();

        foreach (var (sound, file) in Files)
        {
            string path = $"res://assets/audio/{file}";
            if (GD.Load<AudioStream>(path) is not AudioStream stream)
            {
                GD.PushWarning($"missing audio {path}; that cue will be silent.");
                continue;
            }
            var player = new AudioStreamPlayer { Stream = stream };
            AddChild(player);
            _players[sound] = player;
        }

        _bgm = new AudioStreamPlayer();
        AddChild(_bgm);
    }

    /// Fires a one-shot, restarting it if it is already ringing. `volume` is the
    /// cue's own level; the master scales it.
    public void Play(CombatSound sound, float volume = 0.8f)
    {
        if (_muted || !_players.TryGetValue(sound, out var player)) return;
        player.VolumeDb = Db(volume * _master);
        player.Play();
    }

    /// Starts or resumes the bed. The mini-boss gets its own heavier theme.
    public void StartBgm(bool boss = false)
    {
        if (_muted) return;
        string path = boss ? BossBgm : DungeonBgm;
        if (_currentBgm != path)
        {
            _bgm.Stop();
            _bgm.Stream = GD.Load<AudioStream>(path);
            _currentBgm = path;
            // Looping is a property of the imported stream, not of the player,
            // and an MP3 imported with default settings does not loop. Setting
            // it here is what makes the bed a bed rather than a one-shot that
            // leaves the fight in silence after ninety seconds.
            if (_bgm.Stream is AudioStreamMP3 mp3) mp3.Loop = true;
        }
        if (_bgm.Stream is null) return;
        _bgm.VolumeDb = Db(BgmRelativeVolume * _master);
        if (!_bgm.Playing) _bgm.Play();
    }

    public void StopBgm() => _bgm?.Stop();

    /// Godot takes volume in decibels, and a linear zero is negative infinity.
    /// Clamped to a floor that is inaudible but finite, because -inf propagates
    /// into the mixer as a NaN rather than as silence.
    private static float Db(float linear) =>
        linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);

    private void Load()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok) return;
        _muted = (bool)config.GetValue(Section, "muted", false);
        _master = (float)config.GetValue(Section, "volume", 0.8f);
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue(Section, "muted", _muted);
        config.SetValue(Section, "volume", _master);
        config.Save(SettingsPath);
    }
}
