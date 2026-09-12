# Battle VFX lab — 12 September 2026

Standalone native Godot art studies for one-on-one turn-based battles. Existing scenes, effects, imported models and gameplay remain intact. The lab never instantiates the live combat controller or accesses a pet save.

## Watch

The generated review lives in `build/battle-lab/` (ignored build output):

- `START-HERE-battle-studies.mp4`: all eight studies, with sound.
- `index.html`: local gallery with individual looping players.
- `11`–`14`: Moonlit Ruins, a cool stone arena with warm perimeter lights.
- `21`–`24`: Ember Vault, a higher camera over basalt hexagons and molten seams.

Each take is 6.2 seconds, 1280 × 720, 60 fps. The cameras frame the full attack and its ground effects. They leave more room than the current close combat view, but four-player placement and readability are not yet tested.

| Creature | Study | Visual idea |
|---|---|---|
| Tempest Ram | Thunderfall | Thin branching sky discharge, a second electrical pulse, charged fractures, flying sparks and dust |
| Clockwork Pangolin | Faultline | Rear-slam clip, spreading molten fractures, staggered uplift of stone, small jets and debris |
| Forest Flicker | Phantom Rake | Three staggered spectral claw sweeps, flight streaks and fading ground cuts |
| Snag | Briar Prison | Tapered thorned roots rise around the target, arch inward and withdraw, with earth and leaves |

The prototype deliberately reduces whole-frame flashing and long model ghost trains. Contrast, silhouette, directional motion and aftermath carry the identity. The original animation clips are reused with lab-only playback timing; this does not re-export or alter any GLB.

## Implementation

- `godot/worklings/tools/BattleLab.cs` builds both settings and stages fixed-delta attacks. It manually advances each AnimationPlayer, pauses the fight during hit-stop, and captures PNG frames. Frame-save failures terminate the run with an error.
- `godot/worklings/tools/BattleLabEffects.cs` draws animated ribbons, branched paths, tapered root geometry, debris and textured dust. Seeded paths and analytic motion make repeat captures comparable. The effects are self-contained under a disposable lab node.
- `godot/worklings/tools/battle_lab.tscn` is the separate entry point.
- `scripts/render-battle-lab.py` verifies complete frame sequences, synthesizes original audio with Python's standard library, and packages H.264/AAC MP4s, stills, a reel and an HTML gallery using the installed ffmpeg.

Audio is a preview soundtrack generated during packaging. It is not wired to the game's CombatAudio. It uses electrical transients, low percussion, stone rumble, short claw sweeps and woody cracks, with no external recordings or new dependencies.

## Reproduce

From the repository root:

```sh
dotnet build godot/worklings/Worklings.csproj --no-restore
WORKLINGS_LAB_OUT=/private/tmp/worklings-battle-lab-frames \
  /Applications/Godot.app/Contents/MacOS/Godot \
  --path godot/worklings res://tools/battle_lab.tscn \
  --fixed-fps 60 --resolution 1280x720
python3 scripts/render-battle-lab.py /private/tmp/worklings-battle-lab-frames
```

Set `WORKLINGS_LAB_TAKE=11` to capture just Thunderfall in Moonlit Ruins, or `WORKLINGS_LAB_TAKE=2` for all Ember Vault takes. Use a fresh output directory to retain earlier iterations. Running the scene in the editor also performs a capture; it is a capture tool, not an interactive arena.

## Scope and next decision

Validation: the Godot C# build passed with only the pre-existing `ItemsProbe.Name` warning. All eight captures completed without reported runtime errors. Packaging verified 372 contiguous frames per take; ffprobe verified every MP4 at 1280 × 720, 60 fps with AAC audio. The combined 2,976-frame reel passed a full ffmpeg decode. Impact frames were visually inspected for each attack in both settings.

These are visual prototypes, not production-ready combat integration. They establish shapes, timing and scene direction for review. After selecting a direction, port the chosen effects into the live ability scheduler, connect event-driven audio, profile on target hardware, and test a four-member formation. The current prototypes rebuild procedural meshes each frame and have not been performance-budgeted for simultaneous multiplayer effects.

Pangolin uses its special rear slam in this lab; its contact fraction is an art-direction estimate and should be refined with the final gameplay timing. Root meshes are procedural thorned tubes rather than final sculpted assets. The scene masonry is procedural blockout geometry with noise texture, not final dungeon environment art.
