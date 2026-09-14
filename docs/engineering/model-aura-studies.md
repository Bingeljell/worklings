# Ambient model aura studies

Nine native Godot variations, presented as three synchronized comparison films plus nine individual portrait loops. These are persistent visual identities for idle creatures, independent of their attack effects. The supplied Tempest Ram image is the art reference for horn and body electricity; it is not loaded or modified by the runtime.

| Creature | Variant 1 | Variant 2 | Variant 3 |
|---|---|---|---|
| Tempest Ram | Horn Static: restrained cyan sparks near horns | Storm Veins: more extensive violet surface arcs | Storm Mantle: surface current and airborne filaments |
| Clockwork Pangolin | Orbiting Inscriptions: small amber glyphs | Runic Orrery: gold/teal symbols and a fine orbit | Shell Sigils: pulsing surface glyphs and floating accents |
| Snag | Earth Motes: twenty tiny drifting flecks | Dust Puffs: thirty-two softer dusty particles | Golden Spores: forty-eight finer warm specks |

The original GLBs, their material resources, and gameplay scenes are preserved. No dungeon controller, persistence code or combat rules are used. The isolated worktree branch is `feature/model-aura-studies`; the other agent's dungeon checkout and build cache are not used for these renders.

## Revised direction: internal blue energy

The September 12 correction makes the Ram electrical through its fur and horns and the Pangolin a blue rune-energy creature with light between armor plates. The original orbiting variants above remain available.

Enable `WORKLINGS_AURA_INTERNAL=1` to capture only these two creatures, each in three strengths: Ram Resting Current / Living Lightning / Surging Storm, and Pangolin Runic Embers / Awakened Core / Breathing Energy. Package with `--internal`:

```sh
WORKLINGS_AURA_INTERNAL=1 WORKLINGS_AURA_OUT=/private/tmp/worklings-internal-frames \
  /Applications/Godot.app/Contents/MacOS/Godot \
  --path godot/worklings res://tools/aura_study.tscn --fixed-fps 30 --resolution 1600x900
python3 scripts/render-aura-studies.py /private/tmp/worklings-internal-frames build/internal-energy-studies --internal
```

`InternalEnergyAura`, in `CreatureAuraStudyEffects.cs`, adds a per-instance `MaterialOverlay` to the actual skinned mesh. It samples the original albedo, estimates dark crevices from local contrast, and animates blue emission with explicit time. The Ram combines fast spatial current pulses with short cyan surface arcs; the Pangolin has steady or slowly breathing blue energy. The material layer needs no cached poses and follows any skeletal animation. The Ram’s additional short arcs still use the study’s idle-pose cache: port these to live surface or bone anchors before using them with attack animations. Capture also uses the pose cache for camera bounds.

Runtime handover: construct `new InternalEnergyAura(mesh, creature, variant)` after the model is ready (`creature` 0 Ram / 1 Pangolin; `variant` 0–2), call `Draw(elapsedSeconds)` each frame, and `Release()` before removing/replacing the aura. Release restores the previous overlay. Do not stack multiple instances on one mesh. It currently expects one surface using `StandardMaterial3D` with an albedo texture. Extend to all surfaces for other assets.

The mask is a visual prototype derived from texture darkness, not a semantic fur/plate mask. It may include dark facial or metallic details. For production, replace that approximation with an authored emission mask, tune per creature, and profile overdraw on the target hardware. No source textures, GLBs, or dungeon scenes are changed.

## Code and pipeline

- `godot/worklings/tools/aura_study.tscn`: dedicated capture entry point; automatically renders and exits. Do not use as a gameplay scene.
- `godot/worklings/tools/AuraStudy.cs`: creates three independent portrait worlds per creature, aligns cameras using mesh bounds, manually seeks the same idle pose for each variant, prepares the surface cache and captures 240 frames at 30 fps.
- `godot/worklings/tools/CreatureAuraStudyEffects.cs`: `AuraPoseBank` samples skinned surface points across 24 idle poses; `CreatureAuraStudyEffects` owns the attached arcs, runes or dust and updates them through `Draw(time)`.
- `scripts/render-aura-studies.py`: verifies frame completeness, encodes comparisons, crops individual portrait clips, creates a 24-second reel and a local gallery, and verifies frame counts and full reel decoding.

Example from this checkout's root:

```sh
dotnet build godot/worklings/Worklings.csproj
WORKLINGS_AURA_OUT=/private/tmp/worklings-aura-frames \
  /Applications/Godot.app/Contents/MacOS/Godot \
  --path godot/worklings res://tools/aura_study.tscn \
  --fixed-fps 30 --resolution 1600x900
python3 scripts/render-aura-studies.py /private/tmp/worklings-aura-frames build/model-aura-studies
```

`WORKLINGS_AURA_SELECT=tempest_ram` (or `clockwork_pangolin`, `snag`) captures one creature. Use fresh frame/output directories when retaining iterations. Preview files are build output and are not committed.

## Reuse notes for the dungeon agent

The study is intentionally separate from dungeon integration. Import the files from the feature branch when useful, and extract the selected aura into runtime code rather than instancing the capture scene. Construct an aura under the actor root, advance it while the creature is present, and release its nodes when the actor leaves.

Current numeric selectors are creature `0=Ram, 1=Pangolin, 2=Snag`, variant `0..2`. Replace these with named configuration resources when integrating. Expose color, density, width/size, speed and intensity instead of copying the renderer for additional creatures.

The surface arcs and shell glyphs use an **idle-animation cache**, interpolated between 24 sampled poses. GPU mesh readback happens only during setup. This is useful for reproducible preview work but is not a general attachment solution for arbitrary clips, blended animations, attacks or locomotion. Production options include authored skeleton attachments, skin-weighted surface anchors, or a shader layer driven by the live skin. Do not reuse the idle cache during an attack and claim it follows that pose. Orbiting runes and dust already move in actor-local space and do not require surface skinning.

The surface glyphs are separate geometry, not an edit to the Pangolin's texture. Palette and dimensions are art-study defaults. Dust intentionally remains small relative to the Snag. The Ram comparison explores understated, stronger and airborne electricity, not a damage cue or constant attack flash.

Before shipping, verify visibility at the game's actual camera distance, transformed actor scales, pause behavior, multiple actors, cleanup and frame cost. The comparison camera is deliberately close enough to judge small details. No audio was added to the ambient loops.

## Validation and review

The isolated Godot C# build passed with only the existing `ItemsProbe.Name` warning. All three native captures completed without reported runtime errors. The packager checked 240 source frames per creature, 30 fps and frame counts for all thirteen MP4s, and fully decoded the 720-frame combined reel. Comparison renders were visually inspected for all three creatures. Main comparisons are 1600 × 900; individual portraits are 510 × 752.

Suggested starting points for review: Ram 02 (Storm Veins) is the closest to the supplied electrical reference; Pangolin 01 gives a restrained readable orbit; Snag 02 makes the tiny dusty balls easiest to see. These are art-direction preferences, not choices wired into the game.
