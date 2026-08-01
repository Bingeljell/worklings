import AVFoundation
import Foundation

/// The one-shot combat sound cues. Raw values are the bundled resource names
/// (the audio-lab `v01` deliveries); swap a name here to promote a different
/// audition variant.
enum CombatSound: String, CaseIterable {
    case hit = "combat-hit__v01"
    case crit = "combat-crit__v01"
    case slam = "combat-slam__v01"
    case dodge = "combat-dodge__v01"
    case unleash = "combat-unleash__v01"
    case brace = "combat-brace__v01"
    case tick = "countdown-tick__v01"
    case victory = "victory-fanfare__v01"
    case defeat = "defeat-sting__v01"
    case snare = "foe-snare__v01"
    case phase = "foe-phase__v01"
    case telegraph = "foe-telegraph__v01"
    case harden = "foe-harden__v01"
    case poof = "foe-poof__v01"
    case enter = "encounter-enter__v01"
    case returnChime = "return-chime__v01"
}

/// Plays the dungeon arena's audio — a looping BGM bed plus one-shot combat
/// cues — from the bundled WAVs. Deliberately defensive: a missing file logs and
/// no-ops rather than crashing, and everything honours the mute setting. The
/// player is combat-scoped; nothing sounds outside a delve.
@MainActor
final class CombatAudio {
    static let shared = CombatAudio()

    private var sfxPlayers: [CombatSound: AVAudioPlayer] = [:]
    private var bgmPlayer: AVAudioPlayer?
    private var currentBGMResource: String?
    private let bgmResource = "dungeon-bgm__v01"
    private let bossBGMResource = "boss-bgm__v01"

    private static let muteKey = "worklings.combatAudioMuted"
    private static let volumeKey = "worklings.combatAudioVolume"

    /// BGM sits under the one-shot cues; both are then scaled by `masterVolume`.
    private let bgmRelativeVolume: Float = 0.5
    private var bgmVolume: Float { bgmRelativeVolume * masterVolume }

    /// Whether combat audio is silenced. Persisted, off by default (combat is an
    /// opt-in delve, so the bed only ever plays inside a fight the player chose).
    var isMuted: Bool {
        get { UserDefaults.standard.bool(forKey: Self.muteKey) }
        set {
            UserDefaults.standard.set(newValue, forKey: Self.muteKey)
            if newValue { stopBGM() }
        }
    }

    /// Master combat-audio level, 0…1. Persisted; defaults to 0.8. Scales every
    /// cue and the BGM, and updates a playing bed live.
    var masterVolume: Float {
        get { (UserDefaults.standard.object(forKey: Self.volumeKey) as? Float) ?? 0.8 }
        set {
            let clamped = min(max(newValue, 0), 1)
            UserDefaults.standard.set(clamped, forKey: Self.volumeKey)
            bgmPlayer?.volume = bgmVolume
        }
    }

    private init() {
        for sound in CombatSound.allCases {
            if let player = makePlayer(sound.rawValue) {
                player.prepareToPlay()
                sfxPlayers[sound] = player
            }
        }
    }

    /// Fires a one-shot cue, restarting it if it's already ringing. `volume` is
    /// the cue's relative level; the master volume scales it.
    func play(_ sound: CombatSound, volume: Float = 0.8) {
        guard !isMuted, let player = sfxPlayers[sound] else { return }
        player.volume = volume * masterVolume
        player.currentTime = 0
        player.play()
    }

    /// Starts (or resumes) the looping BGM bed so it sits under the cues. The
    /// mini-boss gets its own heavier theme.
    func startBGM(boss: Bool = false) {
        guard !isMuted else { return }
        let resource = boss ? bossBGMResource : bgmResource
        if bgmPlayer == nil || currentBGMResource != resource {
            bgmPlayer?.stop()
            bgmPlayer = makePlayer(resource)
            currentBGMResource = resource
        }
        guard let bgm = bgmPlayer else { return }
        bgm.numberOfLoops = -1
        bgm.volume = bgmVolume
        if !bgm.isPlaying {
            bgm.currentTime = 0
            bgm.play()
        }
    }

    func stopBGM() {
        bgmPlayer?.stop()
    }

    private func makePlayer(_ resource: String) -> AVAudioPlayer? {
        guard let url = Bundle.main.url(forResource: resource, withExtension: "wav")
            ?? Bundle.module.url(forResource: resource, withExtension: "wav") else {
            NSLog("Worklings could not find audio %@.", resource)
            return nil
        }
        return try? AVAudioPlayer(contentsOf: url)
    }
}
