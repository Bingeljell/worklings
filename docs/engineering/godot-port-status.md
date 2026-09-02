# Godot Port — Status

> Evolving doc, not a frozen spec — see [docs/README](../README.md).
>
> **The living answer to "where are we?"** Update it when a slice lands rather
> than reconstructing the state from git log. Last updated 2026-09-02.

## The one-line answer

**The Swift app is still the product.** Godot has a working dungeon prototype —
a real encounter, resolved by ported combat logic, with a HUD and impact
frames — and nothing else. Roughly **59% of `CompanionCore` is ported and none
of the app around it is.**

As of 2026-09-02 the **whole dungeon-facing half is ported**. A Workling with a
level, gear and condition can be built, fight a four-encounter delve, bank or
push deeper, take drops, and have the result written back. What remains is the
desktop pet and the activity pipeline — real work, but not work the dungeon
waits on.

This is deliberate: the engine decision (see
[rendering engine fork](rendering-engine-fork.md)) was taken on the condition
that it would not be a stop-the-world rewrite. The Swift codebase keeps shipping
until the Godot side can replace a mode outright.

## What is ported

| Swift | Lines | C# | Verified |
| --- | --- | --- | --- |
| `SeededGenerator` | 37 | `core/combat/SeededGenerator.cs` | words, doubles, closed ranges, interleaved streams, **bounded draws over 17 bounds** — exact |
| `StatusEffect` | 45 | `core/combat/StatusEffect.cs` | by `CombatEncounter`'s fights |
| `Bestiary` | 82 | `core/combat/Bestiary.cs` | by `CombatEncounter`'s fights |
| `CombatResolver` | 99 | `core/combat/CombatResolver.cs` | 12 strikes, signature, braced, rate formulas — exact |
| `Combat` | 175 | `core/combat/CombatStats.cs`, `Combatant.cs` | as above |
| `PetCombat` | 176 | `core/combat/PetCombatRates.cs` | as above |
| `CombatEncounter` | 456 | `core/combat/CombatEncounter.cs`, `CombatTypes.cs` | **4 fights, 107 events, logs identical** |
| `PetProgression` | 288 | `core/progression/PetProgression.cs` | 69 lines: curve to L25, every level boundary, per-class growth, caps, clamping |
| `PetFamily` | — | `core/pet/PetFamily.cs` | retired the stage's stand-in enum, which led with the wrong case |
| `Items` | 496 | `core/pet/Items.cs` | 117 lines: every item priced against all five families, both swap directions, the fold |
| `DailyTally` | 34 | `core/pet/DailyTally.cs` | 17 lines, fixtures straddling local midnight in both directions |
| `PetState` | 541 | `core/pet/PetState.cs`, `core/pet/PetNeeds.cs` | 82 lines: mood ladder, dedupe, phantom-gear rejection, gear ops, rename |
| — (bridge) | — | `Combatant.Pet(state, rates)` | 31 lines: gear folded in ahead of condition, rounding at the half |
| `CombatRewards` | 130 | `core/combat/CombatRewards.cs` | 42 lines, including four real seeded fights written back |
| `Delve` | 364 | `core/combat/Delve.cs` | 74 lines: four full runs, bank, retreat, guards, determinism |
| `CharacterSheet` | 123 | `core/pet/CharacterSheet.cs` | 70 lines over seven pets, all three rungs of the ladder |

**~3,166 of 5,351 lines.** Verification is against reference output captured
from the running Swift implementation, not against expectations — see
"Why verification mattered" below. **461 reference lines across ten probes**,
all diffing clean.

## What is not ported

Everything else in `CompanionCore`, in rough order of how much the Godot side
will want it:

| Swift | Lines | Why it matters next |
| --- | --- | --- |
| `PetBrain` | 543 | Desktop-pet behaviour — not needed for the dungeon at all. Also holds `grantingXP`, where the daily caps and milestone decay actually live. |
| `PetCareStatus`, `PetPresentation`, `PetStateFileStore`, `DailyTally` | 520 | Condition, presentation, saves. |
| `ActivityEvent`, `ActivityInbox`, `ActivitySources`, `ToolConnector`, `HookConfigMerger` | 1,096 | The activity pipeline. Also needs Windows/Linux equivalents under **any** engine — a cross-platform cost, not a Godot one. |
| `ScreenPlacement` | 180 | Desktop-pet window placement. |

Plus **persistence**, deliberately. Swift's `PetState.init(from decoder:)` folds
the pre-v2 flat daily fields into the unified tallies and defaults every field
added since. That is the file store's job, `PetStateFileStore` is unported, and
porting decode logic with no JSON layer to verify it against would be writing
untested migration code. The rules it has to honour, recorded here so they are
not lost with the decision:

- `workLog` falls back to the legacy `workLogCountToday` / `workLogCountDate` pair.
- `dailyXP` falls back to `dailyXPBySource` / `dailyXPDate`.
- `dailyEventCount` has no legacy equivalent and starts empty — no
  diminishing-returns history carries over a version bump.
- A save predating gear reads as the **starter** loadout, not as nothing, so it
  isn't left with an empty inventory it can never fill.
- Decoding routes through the validating initialiser, never the stored
  properties, or a save becomes the one path that can equip a phantom item.

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
3. **`next(upperBound:)` is Lemire's multiply-shift, not modulo-with-rejection.**
   Swift multiplies the word by the bound into 128 bits and returns the *high*
   half — the word's position in the unit interval scaled to the bound, not its
   remainder. Seed 1, bound 5: the raw word mod 5 is 0, Swift returns 2.
4. **`PetStats()` is not `new PetStats()`.** Swift's memberwise initialiser
   applies its defaults; C#'s implicit parameterless *struct* constructor ignores
   them and zeroes every field, as does `default(PetStats)`. A fresh Workling
   came out with 0 in all five stats, built cleanly, and fought quite happily.
   `PetStats` is a class now: a null reference throws where a zeroed struct
   silently fights.

Neither is visible in a build log or in a fight that looks reasonable. This is
the argument for keeping `tools/RngProbe`, `ResolveProbe` and `FightProbe`
around: they are the regression tests for a class of bug with no other symptom.

The third of those adds a lesson about the *suite* rather than any one method:
**a ported method with no caller is not verified by the suite passing.**
`NextBelow` was written when `SeededGenerator` was ported, ahead of any caller,
and nothing in combat draws a bounded integer — so 107 identical fight events
and a green RNG probe sat on top of a broken method for the whole port. `Delve`
was its first real caller, and it surfaced as four delves matching Swift on HP,
XP, exit tiers and replay determinism while awarding entirely different gear.

**The pattern to keep:** capture reference output from the Swift implementation,
diff against it, and only then move on. Three of the four bugs above came from
reasoning about the Swift stdlib; all four were caught by reading captured
values instead.

### OPEN: the probes are not regression tests yet

They print. The diffing was done by hand against reference files that live
outside the repo, so they verify a port once and catch nothing afterwards —
exactly the failure mode the Lemire bug demonstrates. Committing the ten
reference outputs next to the probes, with a runner that diffs them, would turn
"verified once" into "stays verified". Small, and not yet done.

## Open, in priority order

1. **Wire the ported layers into the scene.** `CacheWarrenScene` still builds its
   combatants by hand. Everything it needs now exists — `Combatant.Pet(state,
   rates)` folds gear in ahead of condition — so the dungeon can run a real
   `Delve` against a real `PetState` instead of one fight on a loop. This is the
   payoff for the whole port and nothing else blocks it.
2. **Store the probe references**, above, so the suite catches regressions.
3. **Animation timing.** The Ram's attack clip is 2.0s; with a 3s countdown each
   exchange runs ~5s. Nikhil is revising the actions to be quicker and more
   impactful; contact points are stored as fractions (Ram 0.86, Flicker 0.82) so
   they survive a re-time.
4. **`AttackersTravel`.** Defaults to false because travelling reads as sliding
   — the mesh translates while playing a *stationary* attack animation. A walk
   cycle underneath during the approach is the real fix.
5. **The impact flash reads as invisible in motion** despite showing clearly in
   stills. Not diagnosed; may need more than a tint change.
6. **Tuning nobody has judged yet** — lag hold, catch-up speed, damage number
   sizes, hit-stop duration. All exported.
7. **The combat panel** — narration and round/Approach are placeholder labels,
   not a designed panel.
8. **Multi-combatant HUD.** Screen-space edge plates work for two; they break at
   3–4 bodies (multiple foes, multiplayer). Nikhil has an idea.
9. **Action trimming.** The Ram ships 17 actions, most of them iteration
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
- **C# structs ignore constructor default arguments.** `new T()` on a struct runs
  the implicit parameterless constructor and zeroes every field. A ported Swift
  struct with meaningful defaults wants to be a class.
- **Swift's `String.count` counts grapheme clusters**, C#'s `.Length` counts
  UTF-16 units. Anything validating a length — names, capped at 24 — needs
  `StringInfo.GetTextElementEnumerator`, or the two disagree about one save.
- **Swift's `Calendar.current` is the *local* calendar.** A day-scoped value
  compared in UTC passes every obvious test and rolls the day over at the wrong
  hour.
