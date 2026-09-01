# Godot Port — Status

> Evolving doc, not a frozen spec — see [docs/README](../README.md).
>
> **The living answer to "where are we?"** Update it when a slice lands rather
> than reconstructing the state from git log. Last updated 2026-09-01.

## The one-line answer

**The Swift app is still the product.** Godot has a working dungeon prototype —
a real encounter, resolved by ported combat logic, with a HUD and impact
frames — and nothing else. Roughly **22% of `CompanionCore` is ported and none
of the app around it is.**

This is deliberate: the engine decision (see
[rendering engine fork](rendering-engine-fork.md)) was taken on the condition
that it would not be a stop-the-world rewrite. The Swift codebase keeps shipping
until the Godot side can replace a mode outright.

## What is ported

| Swift | Lines | C# | Verified |
| --- | --- | --- | --- |
| `SeededGenerator` | 37 | `core/combat/SeededGenerator.cs` | words, doubles, closed ranges, interleaved streams — exact |
| `StatusEffect` | 45 | `core/combat/StatusEffect.cs` | by `CombatEncounter`'s fights |
| `Bestiary` | 82 | `core/combat/Bestiary.cs` | by `CombatEncounter`'s fights |
| `CombatResolver` | 99 | `core/combat/CombatResolver.cs` | 12 strikes, signature, braced, rate formulas — exact |
| `Combat` | 175 | `core/combat/CombatStats.cs`, `Combatant.cs` | as above |
| `PetCombat` | 176 | `core/combat/PetCombatRates.cs` | as above |
| `CombatEncounter` | 456 | `core/combat/CombatEncounter.cs`, `CombatTypes.cs` | **4 fights, 107 events, logs identical** |

**~1,190 of 5,351 lines.** Verification is against reference output captured
from the running Swift implementation, not against expectations — see
"Why verification mattered" below.

## What is not ported

Everything else in `CompanionCore`, in rough order of how much the Godot side
will want it:

| Swift | Lines | Why it matters next |
| --- | --- | --- |
| `PetState` | 541 | The Ram in the dungeon has **hardcoded stats**. No level, gear, condition, or identity until this lands. |
| `Items` | 496 | Gear folds into effective stats before condition; combat currently sees neither. |
| `PetProgression` | 288 | XP and levels — nothing persists from a fight yet. |
| `Delve` | 364 | The encounter chain, banking, press-your-luck. The dungeon is one fight on a loop. |
| `CombatRewards` | 130 | Drops and XP from a win. |
| `CharacterSheet` | 123 | Character-screen readout; needs `PetState` + `Items` first. |
| `PetBrain` | 543 | Desktop-pet behaviour — not needed for the dungeon at all. |
| `PetCareStatus`, `PetPresentation`, `PetStateFileStore`, `DailyTally` | 520 | Condition, presentation, saves. |
| `ActivityEvent`, `ActivityInbox`, `ActivitySources`, `ToolConnector`, `HookConfigMerger` | 1,096 | The activity pipeline. Also needs Windows/Linux equivalents under **any** engine — a cross-platform cost, not a Godot one. |
| `ScreenPlacement` | 180 | Desktop-pet window placement. |

And none of the **app**: menubar host, character screen, inventory, care UI,
desktop pet. Those are SwiftUI and have no Godot counterpart yet.

## What exists on the Godot side that has no Swift original

New code, written for the renderer rather than ported:

- `core/stage/StageActor`, `ActorAnimations` — a combat beat mapped to an exact
  animation clip, verified against the loaded model at startup.
- `core/stage/AttackLunge` — approach / wait / hold / recover, with contact
  timed off the animation.
- `core/stage/ImpactFrames` — hit-stop, camera shake, knockback, impact flash,
  family-coloured sparks.
- `core/stage/FamilyEnergy` — the five family colours, driving every visual.
- `core/stage/CombatHud`, `HealthPlate`, `DamageNumbers`, `StageType` — the HUD.
- `scenes/dungeon_stage.tscn`, `scenes/cache_warren.tscn` — the room and the fight.

## Why verification mattered

Two bugs were caught by comparing numbers against the Swift original that
nothing else would have found. Both produced completely plausible fights that
silently diverged:

1. **`Double.random(in: 0..<1)` takes the LOW 53 bits of the word.** I took the
   high bits — a perfectly uniform double, different on every draw.
2. **Closed ranges take the HIGH bits.** Having learned the first, I assumed
   closed ranges matched. They are the opposite, and every strike draws its
   damage swing from a closed range.

Neither is visible in a build log or in a fight that looks reasonable. This is
the argument for keeping `tools/RngProbe`, `ResolveProbe` and `FightProbe`
around: they are the regression tests for a class of bug with no other symptom.

**The pattern to keep:** capture reference output from the Swift implementation,
diff against it, and only then move on.

## Open, in priority order

1. **`PetState` port** — the largest single unblock. Everything about the Ram
   being a real Workling waits on it.
2. **Animation timing.** The Ram's attack clip is 2.0s; with a 3s countdown each
   exchange runs ~5s. Nikhil is revising the actions to be quicker and more
   impactful; contact points are stored as fractions (Ram 0.86, Flicker 0.82) so
   they survive a re-time.
3. **`AttackersTravel`.** Defaults to false because travelling reads as sliding
   — the mesh translates while playing a *stationary* attack animation. A walk
   cycle underneath during the approach is the real fix.
4. **The impact flash reads as invisible in motion** despite showing clearly in
   stills. Not diagnosed; may need more than a tint change.
5. **Tuning nobody has judged yet** — lag hold, catch-up speed, damage number
   sizes, hit-stop duration. All exported.
6. **The combat panel** — narration and round/Approach are placeholder labels,
   not a designed panel.
7. **Multi-combatant HUD.** Screen-space edge plates work for two; they break at
   3–4 bodies (multiple foes, multiplayer). Nikhil has an idea.
8. **Action trimming.** The Ram ships 17 actions, most of them iteration
   history; the Flicker has a clean five. Needs a human call on which variants
   are the keepers — `keep_actions` takes the set.

## Traps worth remembering

- **`scripts/committer` runs `git add --force`**, which bypasses `.gitignore`.
  Pass explicit file paths, never directories.
- **Re-exporting a `.glb` invalidates its `.import`.** The hash is derived from
  the source; a stale one makes the model fail to load entirely, and it presents
  as a scene-graph error rather than an import one.
- **Godot strips default values from `project.godot`.** An explicit
  `window/stretch/aspect="keep"` disappears on every open, because `keep` is the
  Godot 4 default.
- **Blender 5.2 removed the Collada exporter**, moved actions to slots
  (`action.layers[...]`, not `action.fcurves`), and renamed operator keywords.
- **A hidden rig cannot be selected**, and glTF export then drops it silently —
  producing a mesh with no skeleton and no animations, reported as success.
