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

## Agent handover: reuse and live integration

### What is reusable today

The effects are actual Godot geometry and shaders, not a video overlay. They can run during gameplay. The capture and packaging tools are only how we reviewed them.

`BattleLabEffects` does not load or inspect a monster model. Its current constructor is:

```csharp
new BattleLabEffects(parent, camera, kind, attackerPosition, targetPosition);
```

| Current `kind` | Effect |
|---|---|
| `0` | Thunderfall lightning |
| `1` | Faultline lava |
| `2` | Phantom Rake claws |
| `3` | Briar Prison roots |

The same `kind: 0` works with a Ram, Snag, or a future monster. Model animation and ability effect are separate choices. The lab's `_models` and `_moves` arrays are demonstration pairings, not a requirement of the effect renderer.

The owner calls `Draw(secondsRelativeToContact)` each frame: negative values are anticipation, zero is contact, positive values are aftermath. Start it during wind-up, not only when damage lands. Call `Release()` when finished or cancelled. There is currently no public completion flag; drawing stops outside the effect window, but the owner must still release the nodes. An owner can release at four seconds after contact for the present effects.

Current limitations to address during extraction:

- Kind, palette, dimensions, durations and random seed are hardcoded. Reusing lightning on another model is already possible; independently configuring red lightning, a smaller bolt or a stronger critical strike needs parameters.
- Parent and coordinates assume the lab's identity world transform and a level floor. Convert positions consistently if the live stage is transformed; ground effects need a suitable floor origin for terrain at another height.
- Positions are captured for the strike. Ground scars should stay at the impact location when the victim recoils; moving projectiles and tracking targets need an explicit targeting policy.
- Rebuilding ImmediateMesh geometry every frame is a prototype implementation. Profile representative fights before deciding what to cache or pool.

### Scene extraction

**Do not instance `battle_lab.tscn` as the playable dungeon.** Its `_Ready()` automatically captures every take and quits Godot. It also creates presentation labels and synthetic attack timing.

Extract the environment-building portion of `BattleLab.MakeSet()` into reusable stage scenes or a runtime stage builder. Keep the lab as a consumer of the same stage and effect code so future previews show what gameplay actually uses. Preserve the original stage as an alternative.

| Setting | Camera position | Look-at target | Vertical FOV |
|---|---|---|---|
| Moonlit Ruins | `(17, 24, 27)` | `(0, 0.6, 1.1)` | `36°` |
| Ember Vault | `(12, 31, 22)` | `(0, 0.6, 1.1)` | `36°` |

The lab also enables 4× MSAA on its viewport and configures lighting, fog, tonemapping and glow in `MakeSet()`. Copying the camera alone will not reproduce the look.

Live combat instances `scenes/dungeon_stage.tscn` as `Stage` inside `scenes/cache_warren.tscn`. `CacheWarrenScene._Ready()` looks up `Stage/StageCamera`; preserve that node path or deliberately update its callers. The original stage has `PartySlot` and `FoeSlot` markers, but the actors currently have baked transforms in `cache_warren.tscn`. Updating markers alone does not reposition those actors. Either adopt marker-driven placement or update both consistently. The lab uses attacker `(-3.6, 0, 4.5)` and defender `(2.8, 0, -2)` and rotates them to face one another.

### Effect and timing integration

1. Extract the reviewed renderer into runtime code, retaining a compatibility path for the lab. Replace numeric kinds with a named effect identifier. Introduce effect configuration for palette, scale, intensity, seed and timing; these are proposed additions, not existing APIs.
2. Adapt it to the existing `AbilityVfx.Begin()` / `Tick()` / `Clear()` lifecycle. `CacheWarrenScene.ScheduleImpact()` already calls `Begin()` before contact. That is the correct integration point for anticipation and flight. Replacing only the old `Bolt` class will not reproduce the complete reviewed lightning sequence.
3. Resolve effect identity from ability configuration, with `AbilitySignatures.For(modelName)` as a fallback while migrating. Two different monsters should be able to select the same lightning effect without copying renderer code or adding model checks inside it.
4. Maintain one contact clock for body animation, effect, damage, audio and hit-stop. `CacheWarrenScene._Process()` advances `_vfx` with the hit-stop scale; preserve that behavior. Effects from consecutive attacks may overlap during their aftermath, so keep separate active instances and clean them up on encounter transitions and scene exit.
5. Audit body animation stepping too. The lab explicitly advances `AnimationPlayer` manually and pauses that advancement during hit-stop. Copying just its effects does not reproduce this behavior in the live controller. Choose one animation advancement owner; never combine automatic playback with a second manual advance.
6. Account for the chosen action's clip and playback speed when computing contact. The lab uses the Pangolin's `Signature` rear slam, whereas `StageActor.AttackImpactDelay()` reads its `Attack` clip. Do not time a signature slam with the tail-swipe clip's delay. The lab also shortens wind-ups and reduces travel; those presentation changes need deliberate integration if the goal is to match the previews.
7. Keep damage and miss logic in live combat. A visual effect must not apply damage, award loot, advance a turn or write a save. Ensure successful-hit effects are not accidentally triggered from `ScheduleWhiff()`.

### Audio and render pipeline

`scripts/render-battle-lab.py` contains the original sound synthesis and packaging workflow. Its generated WAVs are entire 6.2-second preview timelines, including lead-in and aftermath; they are not ready-to-trigger attack cues. Extract or generate separate anticipation, impact and tail assets, then trigger them through `CombatAudio` at the appropriate events. Respect the existing mute and volume controls. Repeated cues currently restart a per-sound player, so simultaneous co-op audio will need a deliberate polyphony policy.

Keep the reproduction commands above as the visual regression loop: build → capture from Godot → package → inspect wind-up, contact and aftermath. Also test the playable encounter; a staged clip cannot verify turn progression, targeting or cleanup.

### Acceptance criteria

- The playable Cache Warren can select the new stage while the original remains available.
- All four reviewed effects work through actual combat events, with correct hit/miss behavior and contact timing.
- Lightning can be assigned to two different monster models using configuration, with no duplicated geometry code.
- Preview and gameplay share the stage/effect implementation; capture-only HUD, fixed stepping, file output and automatic quit stay in the tool.
- Repeated turns and encounter changes leave no lingering lights, meshes or audio, and existing save behavior remains intact.
- Godot builds, relevant existing probes pass, and fresh captures plus a playable encounter are reviewed. Record performance on target hardware. A wider camera alone is not proof of four-player readiness.

### Copy-paste task for another agent

> Read `AGENTS.md`, `docs/process/git-workflow.md`, and `docs/engineering/battle-vfx-lab.md`. Integrate the reviewed Moonlit Ruins stage and four BattleLab attack effects into the playable Godot Cache Warren. Use `build/battle-lab/START-HERE-battle-studies.mp4` as the visual reference; rerender it using the documented pipeline if the local build output is missing. Create the runtime scene/effect/configuration files needed for this integration, while retaining all existing scenes and the capture tool. Extract shared code from BattleLab rather than instancing its capture-and-quit scene in gameplay. Make effect selection ability-driven so different monsters can reuse lightning. Follow the handover's contact-clock, animation, coordinates, audio and cleanup guidance. Keep combat rules and persistence behavior unchanged. Start with Moonlit Ruins as the default and retain Ember Vault as an optional variation. Validate the acceptance criteria, update the changelog, and commit through `scripts/committer` on a feature branch. Do not push without asking.
