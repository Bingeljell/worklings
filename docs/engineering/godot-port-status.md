# Godot Port — Status

> Evolving doc, not a frozen spec — see [docs/README](../README.md).
>
> **The living answer to "where are we?"** Update it when a slice lands rather
> than reconstructing the state from git log. Last updated 2026-09-04 (third slice).

## Picking this up cold

If this session is gone, start here.

### Where the work is

`main` has the full loop: a pet on the desktop, care, and a delve it walks into
and back out of (PRs #49, #50, #51).

**Up as [PR #52](https://github.com/Bingeljell/worklings/pull/52)** from
`feature/godot-character-and-activity`: the character window finished, model bay
included, and the whole activity pipeline's logic ported and probed.

**#52 is merged**, as a merge commit rather than a squash, so `main` carries
those nine commits as themselves. `feature/godot-activity-wiring` has been
rebased onto it and is five commits ahead.

**`CompanionCore` is ported in full**, and the pipeline runs end to end: a
session that owns the live Workling, hooks installed into Claude Code and Codex,
a spool watcher, a presence poll and a git watcher.

**Next is slice C, making it presentable** — and before an alpha ships off this
build, the list in
[What still stands between here and deleting Swift](#what-still-stands-between-here-and-deleting-swift),
of which **audio is the largest and most obvious.**

### Run it

```bash
scripts/godot-pet      # the desktop pet — this is the app
scripts/godot-export   # a real Worklings.app in dist/godot/
scripts/godot-probe    # every probe with a stored reference, diffed
/Applications/Godot.app/Contents/MacOS/Godot --path godot/worklings res://scenes/cache_warren.tscn   # the dungeon alone
/Applications/Godot.app/Contents/MacOS/Godot --path godot/worklings res://tools/character_shot.tscn   # two frames of the character window, to user://
/Applications/Godot.app/Contents/MacOS/Godot --headless --path godot/worklings res://tools/presence_check.tscn   # the idle clock and both crossings
WORKLINGS_CONNECT_CHECK=/tmp/settings.json /Applications/Godot.app/Contents/MacOS/Godot --headless --path godot/worklings res://tools/connect_check.tscn   # a real connect/disconnect — NEVER point this at ~/.claude/settings.json
WORKLINGS_GIT_CHECK=<repo> /Applications/Godot.app/Contents/MacOS/Godot --headless --path godot/worklings res://tools/git_check.tscn   # the git source against a real repo
```

**Feeding the pet an event by hand**, which is how the inbox was tested:

```bash
WORKLINGS_INBOX_DIR=/tmp/inbox scripts/godot-pet &
echo '{"kind":"milestone","sourceId":"codex"}' > /tmp/inbox/a.json
```

`WORKLINGS_INBOX_DIR` and `WORKLINGS_SAVE` both point a run away from the real
thing. For the inbox that matters more than for the save: the inbox is
drain-and-delete, so a test run on the real directory would *eat* the app's
events rather than merely read them.

`character_shot` is the sibling of `fight_shot`: the model bay either renders or
comes up as a black rectangle, and no text probe can tell the difference. It also
prints the idle's playhead, because a still model and a live one are identical in
a screenshot.

**Right-click** the pet for the menu, **click** it to pet it. Developer keys:
**Esc** quit · **Tab** next monitor · **C** click-through · **R** roaming ·
**W** the Warren · **S** the character sheet.

### The plan, in order

Agreed 2026-09-04. **The next alpha ships from the Godot build, not the Swift
one** — deferrable if it comes to it, but that is the target.

**A. The character window** — done.

1. ~~The window and its tabs.~~ Done.
2. ~~The Character tab.~~ Done.
3. ~~The Inventory tab, with equip and unequip.~~ Done.
4. ~~Wire "Character sheet…" in the menu.~~ Done.
5. ~~The model bay~~ — done. A `SubViewport` inside the Character tab rather
   than a camera hung off the window: the bay has to sit in the tab's layout,
   and only a viewport inside a `Control` does that. It carries the desktop
   pet's light rig and lens, pulled in to 80% of its distance because the bay is
   a letterbox where the pet's window is a square. Drag it to turn the Workling.

**B. The activity pipeline** — **done, product hook included.** The pet notices
you working: hooks it installs into Claude Code and Codex, its own idle clock,
and commits in any repository you point it at.

6. ~~`ActivityEvent` + `ActivityContext`~~ — done, and byte-identical to Swift
   over 44 reference lines. The subtlety was where it was expected: `awaySince`
   and `workingSince` are tracked *separately* from `lastEventAt`, so a repeated
   "still away" ping refreshes expiry without erasing how long the absence has
   been, and a return slides an open work block's start forward by the time
   spent away rather than counting it as focus.
7. ~~`PetBrain.observe`~~ — done, the activity half probed against Swift like
   the care half was, byte-identical over 62 reference lines. `Advance` takes
   an `ActivityContext` again, so working burns hunger and energy faster and an
   absence drains trust at one of two rates. `PetSimulationRates.Scaled` and
   `PetActionAvailability` came across with it. **The pet can now notice — but
   nothing delivers it an event yet, which is commits 8 and 9.**
8. ~~`ActivitySources`~~ — done. Ported **before** the inbox rather than after,
   because the inbox's reserved-id list is built out of these source ids and
   would otherwise have been written twice. All four decisions are pure: which
   HEAD movements are forward progress, when a burst of events collapses to one
   emote, when a new day has started, and where the idle threshold is crossed.
9. ~~`ActivityInbox`~~ — done. The trust boundary: one small JSON file per
   event, decoded and validated, with **no field for content of any kind**, so
   the privacy promise is structural rather than a policy.
10. ~~Wire it into the pet~~ — done, in four commits. `PetSession` owns the
    live Workling and is the one place that decides when to save and when the
    pet may speak. `ActivityInboxWatcher` drains the spool directory,
    `PresenceWatcher` reads the machine's idle clock, and `GitCommitWatcher`
    turns forward commits into milestones. The pet also ages on a timer now,
    which it did not before — its needs only moved when it was interacted with.

    **Verified by driving it, not by reading it.** Seven files into a running
    pet's inbox: two landed, five refused by name, the directory came back
    empty. Five milestones at once emoted once and paid all five. A repo with
    three commits paid nothing at connect; one made while watching paid once;
    an amend paid nothing. And `away` stayed pinned while `last` advanced,
    which is the entire reason those two clocks are kept apart.
11. ~~`ToolConnector` + `HookConfigMerger`~~ — done, and it did **not** break the
    estimate. The flag was about Windows and Linux; the merger turned out to be
    platform-agnostic JSON, and the connector's only platform question is what
    "executable" means, which is one branch. Nothing was written for Windows or
    Linux beyond that, and nothing has been run there.

    The user's own config is what this is really about, and the probe is mostly
    about leaving it alone: their `model`, their `permissions` and their own
    hooks all survive a connect and a disconnect byte for byte. Verified against
    a real file too, not just in memory — connect, reconnect, disconnect, and
    the file comes back identical to what it started as.

> **The estimate did not break at commit 11.** It was flagged because these two
> write into other tools' config files and Windows and Linux have no precedent
> here in any language. In the event the merger was platform-agnostic JSON and
> the connector's only platform question was what "executable" means. The
> cross-platform cost is still real and still unpaid — it just was not in these
> 583 lines.

**C. Making it presentable** — 4 commits.

12. **The dungeon's surfaces get the theme.** `WorklingsTheme` exists and the
    prep screen is the natural first, since it already has a layout.
13. **The three surface-less beats** — steer prompt, bank-or-push, summary. They
    currently share the fight's narration label.
14. **The remaining nine probe references**, so the suite catches regressions
    rather than only the newest slices.
15. **Export, sign, and cut alpha.11** off the Godot build.

### Decisions already taken — read before relitigating

- [Two windows, and why not one](#two-windows-and-why-not-one) — the pet is the
  main window; the dungeon and the character screen are ordinary ones.
- [Which file, and who is allowed to write it](#which-file-and-who-is-allowed-to-write-it)
  — **never point a test run at the real save**, and the pet owns the state while
  a delve is running.
- [Exporting](#exporting) — four requirements invisible in the editor, the first
  of which fails silently.
- **Godot sizes windows and popups in physical pixels.** This has caused four
  bugs now. It is in [Traps worth remembering](#traps-worth-remembering); read
  that list before building any new surface.

### Not on the critical path, but promised

- **An effects vocabulary** for the desktop and the fight, built in Godot. See
  "what the shell still does not do".
- **The dungeon's UI is text**, not game UI.
- **Windows and Linux are claimed, not demonstrated.** Nothing has ever run on
  either.

## The one-line answer

**The Swift app is still the product.** Godot has a working dungeon prototype —
a real four-encounter delve, resolved by ported combat logic, with a HUD and
impact frames — and nothing else. Roughly **59% of `CompanionCore` is ported and
none of the app around it is.**

As of 2026-09-02 the **whole dungeon-facing half is ported**. A Workling with a
level, gear and condition can be built, fight a four-encounter delve, bank or
push deeper, take drops, and have the result written back. What remains is the
desktop pet and the activity pipeline — real work, but not work the dungeon
waits on.

As of 2026-09-03 the scene **runs that delve** rather than one hand-built fight
on a loop: `CacheWarrenScene` builds its fighter from a `PetState` through
`Combatant.Pet`, drives `Delve` through briefing, the encounter chain, the
bank-or-push choice and the closing summary, and writes the result back through
`Delve.Resolution`. The encounter is **stepped rather than pre-resolved**, so the
fight pauses at its decision points and the player steers it — the Approach and
the Unleash, beat four of the design's five.

As of 2026-09-04 that Workling **persists**. `PetStateCodec` and
`PetStateFileStore` are ported, the scene loads a save on open and writes it back
when a run resolves, and XP, gear and condition survive the window closing. The
save is byte-identical to the Swift app's and the shipped build reads **the same
file**, so there is one Workling across both apps — while a test run works on a
copy, so it can never overwrite a real pet. See
[The save file](#the-save-file) below.

Also as of 2026-09-04, **the desktop shell exists as a window**. Transparent,
borderless, always on top, click-through, placed across monitors, with a Workling
standing in it and nothing else — see
[The desktop shell](#the-desktop-shell) below. `ScreenPlacement` is ported
alongside it.

**All five of [the delve's beats](../design/dungeons.md#the-delve-as-a-journey--encounter--delve-ux)
now exist** — briefing and prep on one screen, the fight, the steer, and
bank-or-push. Prep is the only one with a designed surface; the other three
share the fight's narration label.

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
| `PetStateFileStore` + the `Codable` conformances | 50 + ~90 | `core/pet/PetStateFileStore.cs`, `core/pet/PetStateCodec.cs` | 176 lines: both encodings byte-identical, every migration rule, a phantom-gear save, a future schema |
| `ScreenPlacement` | 180 | `core/host/ScreenPlacement.cs` | 60 lines: a negative-origin second monitor, a window larger than its screen, the roaming cycle and its minimum-travel flip |
| `ActivityEvent` — kinds, the event, `ActivityContext`, the simulated source | 195 | `core/pet/ActivityEvent.cs` | 44 lines: every kind's reducer, the idle heartbeat that must not restart the absence, the return that slides an open work block forward, both sides of every expiry boundary |
| `PetBrain` — the **care half** | ~250 of 543 | `core/pet/PetBrain.cs` | 110 lines: a negative elapsed time, the week-long offline cap, the distress thresholds, the too-tired-to-play boundary, the daily cap, a level-crossing grant, gear surviving a care action |
| `PetBrain` — the **activity half** | ~290 of 543 | `core/pet/PetBrain.cs` | 62 lines: every kind observed on a healthy and on a tired pet, the working multipliers, both away tiers and the grace boundary exactly, a focus session either side of its minimum and one truncated by an absence, the Log Work cooldown and daily cap, scaled rates |
| `HookConfigMerger` | 359 | `core/connect/HookConfigMerger.cs` | 165 lines with `ToolConnector`: what it refuses, what it leaves alone, ownership across four hook spellings, and adapter paths containing a space, an apostrophe and a shell metacharacter |
| `ToolConnector` | 224 | `core/connect/ToolConnector.cs` | as above, plus a real connect/reconnect/disconnect against a file on disk |
| `ActivitySources` | 153 | `core/pet/ActivitySources.cs` | 54 lines: every HEAD movement a git watcher can see, the emote window either side of its interval, the day rollover, both idle-threshold crossings |
| `PetCareStatus` | 237 | `core/pet/PetCareStatus.cs` | 92 lines with `PetPresentation`: every threshold on its boundary and one either side, the rank when several needs are true at once, and what the menu may offer |
| `PetPresentation` | 199 | `core/pet/PetPresentation.cs` | as above — every mood, every reaction's face and thought, the learning-rate rounding, the transition's obscuring frame |
| `ActivityInbox` | 165 | `core/pet/ActivityInbox.cs` | 81 lines: every kind's emittability, seventeen source ids, malformed and mistyped payloads, the reserved kinds and ids, and both time limits one second either side |

**All 5,351 lines.** Verification is against reference output captured
from the running Swift implementation, not against expectations — see
"Why verification mattered" below. **1,233 reference lines across nineteen probes**,
all diffing clean.

## What still stands between here and deleting Swift

Asked directly on 2026-09-04, and worth keeping current: **what would have to be
true before the Swift app could be removed and nothing would be missed?**
`CompanionCore` is not the answer — that is ported in full. Everything below is
app code, which was always going to be rebuilt rather than ported.

**Audio is done** (2026-09-04): the bed, the boss theme, and all sixteen cues.
Signing is deliberately last — it moves with the alpha-to-beta line, not with
this list.

| Missing | Where Swift does it | Size |
| --- | --- | --- |
| **Renaming a Workling.** The menu item is there and disabled. | `AppDelegate`, `PetSession.rename` | small |
| **Choosing a family or a class.** `PetState` supports both and no surface offers either, so a Godot-only player is stuck with what they were born as. | `CharacterScreenView` | part of 977 |
| **The ambient status layer.** `PetCareStatus.HoverSummary` is computed and shown nowhere; hovering the pet says nothing. | `HoverSummaryPanelController` | 84 lines |
| **Signing and notarization.** The export is unsigned, so it runs locally and Gatekeeper refuses it anywhere else. **Deferred to beta on purpose** — an alpha tester can be told it is unsigned, and the repo is public if they would rather build it. | `build_app_bundle`, `verify_release` | — |

Two more that are not parity gaps but would be felt: the dungeon's surfaces are
still text rather than game UI (slice C), and **Windows and Linux have still
never been run**, under either codebase.

## What is not ported

**`CompanionCore` is ported in full.** Every file, every line, each one verified
against reference output from the running Swift implementation.

What remains is the SwiftUI app around it, which has no Godot counterpart and is
being rebuilt rather than ported. See
[What still stands between here and deleting Swift](#what-still-stands-between-here-and-deleting-swift).

## The save file

Persistence was the last thing deliberately left unported, and it was the one
thing the desktop pet genuinely cannot run without: a delve is a session, a pet
is a continuity. It landed on 2026-09-04.

Swift gets its save format from `Codable` — mostly synthesized, hand-written for
`PetState`, `PetNeeds` and `Loadout`. C# has no analogue of a synthesized
conformance, so the whole shape is spelled out in one file, `PetStateCodec`. That
turned out to be the right shape anyway: the save format is one contract with one
owner, and the field defaults in the decoder *are* the migration rules.

**The encoder is byte-identical to Foundation's, and that is a requirement rather
than a flourish.** The same file has to be readable by the Swift app and the Godot
build for as long as both exist. Matching to the byte means the two can be
compared by `diff` instead of by eye — which is how the whole port has been
verified, and the only reason a claim like "Swift can open this" is checkable at
all. It cost reproducing three of Foundation's pretty-printing habits:

- keys sorted ordinally, two-space indent, and `" : "` — with spaces — between a
  key and its value;
- an empty object written as a blank line between the braces, not as `{}`;
- an integral `Double` written as `12`, not `12.0`. (`ToString("R")` does this.)

`Utf8JsonWriter` can do none of them, so the encoder walks a small value tree of
its own. Two other details are Swift's rather than choices made here: a `Date` is
a bare number of seconds since **2001-01-01 UTC**, not 1970; and a nil optional
is an **absent key**, not a `null`, because the synthesized encoder uses
`encodeIfPresent`.

The migration rules the decoder honours — recorded here when they were deferred,
and now each one a fixture in `tools/persistence_probe`:

- `workLog` falls back to the legacy `workLogCountToday` / `workLogCountDate` pair.
- `dailyXP` falls back to `dailyXPBySource` / `dailyXPDate`.
- `dailyEventCount` has no legacy equivalent and starts empty — no
  diminishing-returns history carries over a version bump.
- A save predating gear reads as the **starter** loadout, not as nothing, so it
  isn't left with an empty inventory it can never fill.
- Decoding routes through the validating initialiser, never the stored
  properties, or a save becomes the one path that can equip a phantom item.

`PetStateFileStore` takes an absolute filesystem path and knows nothing about
Godot, which is what keeps the save format testable without a running engine. It
writes through a temp file and moves it into place, because .NET has no
equivalent of Swift's `Data.write(options: .atomic)`.

### Which file, and who is allowed to write it

**A Workling is one pet across every build that can open it.** The exported
`.app` reads and writes exactly the file the Swift app does:

| Platform | Path |
| --- | --- |
| macOS | `~/Library/Application Support/Worklings/pet-state.json` |
| Windows | `%APPDATA%\Worklings\pet-state.json` |
| Linux | `$XDG_DATA_HOME/Worklings/pet-state.json` |

The macOS path has to match `WorklingsDirectories.applicationSupport()` in the
Swift app exactly, filename included, or the two builds hold separate pets while
appearing to share a directory. On Windows and Linux the Godot build is the first
thing to write there.

**But only the shipped app writes it.** A run from the editor, from a terminal,
or headless is a *test*, and a test that can rewrite a real pet with 9,000 XP on
it is one stray autoplay loop away from being a data-loss bug — the delve is
routinely driven at hacked timings with `AutoPlay` on, which is not a state
anything should be saving from.

So a test run **reads the real save and writes to a copy** of it, seeded on first
use. Real stats to play against, the whole load/resolve/save chain still
exercised, the file itself untouched. Delete the copy to re-seed it.

`SaveLocation` makes the call, and the distinction is one Godot can actually
answer: `OS.HasFeature("template")` is true only in an exported build — false in
the editor, and false when the editor binary runs a project from a terminal. The
headless check is the second half, since an exported build driven by a script is
still a test. `WORKLINGS_SAVE` overrides both, because naming a file explicitly
is a statement of intent.

It lives in `core/host/` rather than `core/pet/`: it is the one thing in `core/`
that asks the engine a question, and "how was I launched" is a property of the
shell around the game, not of the Workling.

**The path is printed every launch**, with whether it is the real save. Silence
about which file is being written is exactly how a test run overwrites a real pet
without anyone noticing until it is gone.

One more posture worth keeping: **a save this build cannot read locks writing off
for the session** rather than being replaced. An unreadable file is far more
likely a newer save or a real pet than junk, and overwriting it to recover is the
one move that cannot be undone.

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
- `core/host/SaveLocation` — which save file a build is allowed to touch, and the
  first thing to live under `host/`: the shell around the game rather than the
  game.
- `core/host/DesktopWindow`, `scenes/desktop_pet.tscn` — the desktop pet's
  window. The one piece with no Swift original at all; see
  [The desktop shell](#the-desktop-shell).
- `scenes/dungeon_stage.tscn`, `scenes/cache_warren.tscn` — the room and the
  delve: the phase machine around the fight (briefing, chain, bank-or-push,
  summary) and the event-to-animation mapping, both renderer-side by design.

## The desktop shell

**The one piece with no Swift original.** Everything else on the Godot side is a
port with a reference implementation to diff against; the desktop pet's window is
new work, which made it the biggest unknown and the cheapest thing to prove. It
was done before `PetBrain` deliberately: if Godot could not be a desktop pet
window, that is worth finding out against an empty window rather than after 543
lines of behaviour have been ported to live inside it.

`scenes/desktop_pet.tscn` is that proof. Run it directly — it is not the main
scene, and the dungeon is untouched:

```bash
/Applications/Godot.app/Contents/MacOS/Godot --path godot/worklings res://scenes/desktop_pet.tscn
```

**Esc** quits · **Tab** next monitor · **C** toggles click-through · **R** toggles
roaming · **drag** the pet to move it. A borderless window has no chrome to click,
so every affordance is a key.

### The four traits, and that they are four mechanisms

- **Borderless** and **always on top** are single flags.
- **Transparent** is three things at once: `display/window/per_pixel_transparency/allowed=true`
  in `project.godot`, which is a *project* setting and cannot be turned on at
  runtime; the `Transparent` window flag; and the viewport's own `TransparentBg`.
  The flag asks the OS for an alpha channel, `TransparentBg` stops the renderer
  filling it in. A `WorldEnvironment` with a sky or a background colour paints
  over all of it, which is why the pet's environment uses Clear Color.
- **Click-through** reads backwards from the obvious. `DisplayServer.WindowSetMousePassthrough`
  takes a polygon of the window that **keeps** mouse events; everything outside it
  passes through. An *empty* polygon means the window keeps them all — the
  default, and exactly wrong for a pet. A rectangle around the body stands in for
  a silhouette: Godot wants a polygon, not an alpha test, so a per-pixel hit
  region would mean generating a hull from the mesh every frame it moves.

The body is the Tempest Ram rather than a coloured square. A square would prove
the window is transparent and nothing about the hard part — a 3D viewport with
per-pixel alpha, lit well enough to read against an arbitrary desktop behind it.
It idles when parked and **walks when it moves**, turned partway toward where it
is going: not all the way to profile, because the face is the point.

**It only ever walks left or right.** The roaming pattern carries small vertical
offsets (0.04 and -0.03 of the available height) and they are the reason a walk
reads as a drift — there is one walk cycle, it walks sideways, and any vertical
component is the model sliding rather than stepping. The pattern is flattened
renderer-side rather than edited, so the ported intent stays faithful to Swift.

**Walking speed replaces the roaming pattern's travel durations**, which is a
deliberate divergence from the port. Those durations are fixed per leg — 2.2 to 3
seconds regardless of distance — which is right for a pet that slides and wrong
for one with legs: the same walk cycle would have to play at two speeds to keep
its feet on the ground. The duration comes from the distance and one speed
instead. The pattern still owns *where* the pet goes and how long it rests.

Travel is linear rather than eased, for the same reason. An eased position under
a constant-speed walk cycle slides the feet at both ends; accelerating properly
needs start and stop variants of the walk clip, which the Ram does not have.

### What is proven and what is not

Seen working, by screenshot: **transparency** (terminal text is legible straight
through the window up to the animal's outline), **borderless**, **placement**,
**roaming**, and the **walk** — the Ram crossed 229px of desktop in five seconds
at the 44 px/s it was set to, turned toward where it was going, legs moving, and
with its vertical position unchanged between captures.

**Always-on-top and click-through are confirmed too**, by Nikhil on 2026-09-04 —
neither is judgeable from a screenshot. Click-through behaves as designed: clicks
land on whatever is behind everywhere except the pet's own box.

So the shell is proven on all four traits. Nothing found here argues against
porting `PetBrain` into it.

Enabling per-pixel transparency is a project-wide setting, so the dungeon was
re-run after: it boots and renders unchanged.

### Traps found here

- **The project's 1920x1080 render size letterboxes the pet window — in opaque
  black.** `window/stretch/mode="canvas_items"` with the aspect kept is right for
  the dungeon and wrong for a square 320x320 companion window: the 16:9 content
  is fitted inside it and the leftover is filled with black, so a perfectly
  transparent window arrives wearing two bars. Content scaling is disabled for
  this window. This one is worth remembering because it looks like a
  transparency failure and is not — the alpha was working the whole time.
- **Godot reports screen rects in physical pixels; macOS `screencapture -R` takes
  points.** On a scale-2 display the two differ by a factor of two, which presents
  as "rect does not intersect any displays" rather than as an offset.
- **A headless run reports zero screens** and answers `-1` for the current one, so
  an unclamped screen index asks `DisplayServer` for a monitor that does not
  exist.
- **Roaming produces fractional origins.** Truncating each one biases every move a
  fraction of a pixel toward the top-left — invisible in a step, a visible drift
  over an afternoon. Round.
- **Dragging has to work in screen coordinates.** The window moves underneath the
  pointer, so a delta read from the window's own mouse position chases itself and
  the pet slides away from the cursor.

### Two windows, and why not one

**Decided and proven 2026-09-04.** The pet is the **main** window. The dungeon
opens as an **ordinary second OS window** and closes when the delve ends.

The first instinct was one window switching modes, on the strength of the
experience it buys: the pet *leaves* when a delve starts — a puff of smoke, then
the dungeon — rather than shrinking into a corner or sitting beside the fight.
That experience is right and is kept. It simply does not need one window: play the
puff, empty the pet, show the dungeon. The pet still goes somewhere.

What one window would have cost is that the window must **mutate mid-session** —
transparency, always-on-top, borderless, 320px to 1280px, and content scaling,
five live state changes across three operating systems. All five are proven when
set *at launch* and none are proven when toggled. Per-pixel transparency is the
one Godot's own documentation hedges about.

Two windows configures each once, at creation, and never touches it again. The
pet window stays exactly the thing already proven, and the new window is the most
ordinary kind there is. Three further things push the same way:

- **The letterboxing trap disappears rather than being managed.** Each window
  owns its own content scaling: the pet disables it, the dungeon keeps 16:9.
- **The character screen is a third scaling regime** — freely resizable, where the
  dungeon is capped fixed-aspect and the pet is unscaled. One window would juggle
  three.
- **The pet survives the dungeon.** Closing or crashing a delve leaves the
  companion standing.

The cost: an extra entry in Mission Control, a second viewport's memory, and
closing the dungeon must not quit the app.

#### What `tools/two_window_probe` found

Run it — it drives the whole cycle on a timer, no hands: pet alone, dungeon open
with the pet gone, pet back. It loads the real `cache_warren.tscn` rather than a
coloured rectangle, deliberately, and each of these is a thing a rectangle would
not have surfaced:

- **Godot embeds child windows *inside* the parent viewport by default.** A
  1280x720 dungeon was drawn inside the 320x320 pet window and clipped to
  nothing. It becomes a real OS window only with `GuiEmbedSubwindows` off on the
  parent.
- **A child window shares the parent's 3D world unless given its own.** Without an
  explicit `World3D` the dungeon renders the *pet's* empty room, lit by the pet's
  lights, with its own scene invisible inside it.
- **Godot refuses to hide the main window** — "Can't change visibility of main
  window". So the pet leaves by being **emptied**, not hidden, which for a
  transparent window is the same thing and still needs no flag toggled.
- **A new window in an app that is not frontmost opens behind everything.**
  Entering a delve is a deliberate act, so it has to come forward and take focus.

Verified by screenshot across the full cycle, including a real titled "The Cache
Warren" window running Fren's actual prep screen while the desktop pet was gone.

### The menu, and care

**The first piece of app rather than of port.** A borderless window has no chrome,
so **right-clicking the pet is the only way in**, and everything the Swift app
keeps in a menubar item hangs off it:

```
Fren  ·  Lv 14          (disabled header, rebuilt every open)
Hungry
────────────────
Feed             ▸      Berries / Biscuit / Noodles
Play             ▸      Chase / Dance / Puzzle
Pet
Let it sleep
────────────────
☐ Stay put
────────────────
Character sheet…        (disabled — designed, unbuilt)
Enter the Warren…
────────────────
Rename…                 (disabled — designed, unbuilt)
Quit
```

**Clicking the animal pets it.** A drag check is what stops every reposition also
petting it, with a few pixels of slop because a click is never perfectly still.

Feed and Play are **submenus rather than flat items** because *which* food and
*which* activity are real mechanics — a Workling has a favourite of each and pays
roughly double for it. Flattening them would hide the only choice in the
interaction.

**Stay put** is a checkbox rather than two items, so the current state is visible
without opening anything. Wandering is charming until you are trying to work
under it.

Care **saves immediately** rather than on a timer: a desktop pet has no natural
moment to close, so anything not written at once is written never. The pet also
**advances on load**, which is what makes a Workling left overnight hungry when
the app opens rather than frozen where it was left.

#### Three bugs, one cause, and two keepers

The menu arrived tiny, clipped at every edge, with submenus opening on top of
their own parent. All three were **one cause**: Godot embeds child windows inside
the parent viewport by default, so the menu was rendered *inside* a 320x320
window and clipped to it. Its reported position was `(6, 0)` — the top-left of
the pet, not of the screen. `tools/two_window_probe` had found and documented
this exact trap two hours earlier and it was not applied here.

Two fixes made while chasing the wrong cause are keepers regardless:

- **The menu is sized by font, not by content scale.** A popup's `Size` is in
  *physical pixels*, so on a 2x display a menu built at the default font is half
  the size of every other menu on the machine. Overriding the font makes
  `PopupMenu` measure itself bigger, and row height, padding and separators all
  follow from it. Scaling the *window* is not the fix — that brings the
  letterbox bars back.
- **It is held on screen by `ScreenPlacement.ClampedOrigin`** — the same
  arithmetic that places the pet's own window, negative-origin monitors included.
  The pet's default spot is the top-right corner, which is the worst case for a
  menu that opens down and to the right.

### Pet to Warren and back

**The loop closes 2026-09-04.** Right-click the pet, choose *Enter the Warren…*,
and the pet leaves the desktop in a puff of smoke while the dungeon opens in its
own window. When the run resolves, the Workling that walked out comes back up and
the pet writes it.

**The pet owns the save.** The state is handed *across* into
`CacheWarrenScene.HostedState` rather than re-read from disk, and handed back
through `Resolved`, so exactly one live copy exists while a run is on. Two owners
writing the same file is how a run's XP gets silently rolled back by a needs tick
that started from a stale copy — the same shape of bug as the gear reset. Run on
its own, the dungeon still loads and saves for itself, because that is still how
it gets worked on in isolation.

Three details that are not obvious and each cost a bug:

- **The pet vanishes on the smoke's densest frame**, not at the start of it. The
  puff covers the cut, and that is the difference between reading as *left* and
  reading as *switched off*.
- **While away, the pet window drops mouse events outright.** It is still there
  and still on top, just empty, so without this a click on whatever is behind it
  would pet an animal that is not on the desktop.
- **The dungeon fires `Finished` when the summary has had its time** and there is
  no next run. Without it a finished delve sat on its summary until closed by
  hand — fine for a scene you are iterating on, wrong for a run you are waiting
  to get back from.

The window is **sized against the screen** — 90% of the usable area in 16:9,
which on a 3600x2080 display is 3240x1822 — rather than to a pixel count. Asking
for "1920x1080" gives a window occupying 960x540 *points* on a 2x display, half
the apparent size of a 1080p window and barely bigger than the 720p first guess.
The stage still **renders** at 1920x1080 and scales that frame up, which is what
a fixed-aspect scaling stage means: bigger window, same bake ceiling.

**Seen working:** Fren handed in at Lv 14, and his prep screen reading
*"condition 50% — it is not at its best"* with max HP down from 68 to 59 because
he was hungry. The care state genuinely feeds the delve.

### What the shell still does not do

Not blockers for the next slice, but the list before this is a pet rather than a
demo:

- **One window, one mode.** The pet and the dungeon are separate scenes. Which one
  the app opens as, and how you get from the pet to a delve, is undecided — and it
  is the question that decides whether the pet is the main window with the dungeon
  as a second one, or a mode switch on a single window.
- **No hit region from the silhouette.** The click box is a rectangle.
- **The theme covers the menu and nothing else yet.** `WorklingsTheme` exists,
  taken from `LoadoutPanel`; the dungeon's own surfaces still predate it.
- **Character sheet and Rename sit in the menu, disabled.** Both are designed
  and unbuilt. Shown rather than hidden, so the menu says what is coming.
- **No hit region from the silhouette**, still. The click box is a rectangle, so
  clicking near the pet rather than on it still pets it.
- **Effects are placeholder or absent, in two places.** Raised 2026-09-04 and
  parked as one job, because they are the same job:
  - **On the desktop.** The smoke is legacy pixel art from the direction the
    project has left. It works and it is charming, and a Godot-built effect —
    particles tinted from `FamilyEnergy` — is the intended replacement. Beyond
    the puff, the pet has *no* effects at all: nothing when it is fed, levels up,
    is petted, or comes back from a delve.
  - **In the dungeon.** `ImpactFrames` does hit-stop, shake, knockback, a flash
    and family-coloured sparks, and that is the whole vocabulary — every attack
    looks like every other attack. Signature moves, the Unleash, statuses like
    Snare and Blur, and the Monolith's telegraphed Slam all read identically.
    The impact flash is also still [invisible in motion](#open-in-priority-order).

  These want doing together: one effects vocabulary, built in Godot, used on the
  desktop and in the fight, rather than two unrelated piles of particles.
- **The pet notices you working, and nothing installs the hooks that tell it.**
  The pipeline is live end to end — an adapter that drops a file into the spool
  directory reaches the pet today, and presence and local git need no adapter at
  all. What is missing is `ToolConnector` and `HookConfigMerger`, which write
  the hook configuration into Claude Code and Codex so that dropping the file
  happens without the user doing it by hand.
- **Presence is macOS only.** The idle clock is CoreGraphics; Windows has
  `GetLastInputInfo` and Linux has the X11/Wayland idle extensions, and neither
  exists here in any language. The watcher says so and stays inert.
- **A connected repository cannot be disconnected from the menu.** It means
  editing `user://connected-repos.json` by hand.
- **Nothing about notifications or a dock/menu-bar presence.**
- **Multi-monitor is placement only.** Nothing reacts to a monitor being unplugged
  while the pet is standing on it.

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

### How to run a probe, and how a reference is captured

Not obvious from the files, and rediscovering it costs half an hour.

**Run one probe.** Each lives in `godot/worklings/tools/` as a C# script plus a
one-node scene. Build first — Godot runs the compiled assembly, not the source:

```bash
cd godot/worklings
dotnet build
/Applications/Godot.app/Contents/MacOS/Godot --headless --path . res://tools/delve_probe.tscn
```

The probes, in dependency order: `rng_probe`, `bounded_draw_probe`,
`resolve_probe`, `fight_probe`, `progression_probe`, `items_probe`,
`daily_tally_probe`, `pet_state_probe`, `combatant_bridge_probe`,
`combat_rewards_probe`, `delve_probe`, `character_sheet_probe`,
`activity_probe`, `observe_probe`, `sources_probe`, `inbox_probe`,
`status_probe`, `connector_probe`.

**Capture the Swift side.** `CompanionCore` is a library with no runnable entry
point, and SPM leaves no linkable archive to build against, so the reference
generator is compiled *alongside the sources*. The file must be named
`main.swift` — Swift only allows top-level statements there — and must not
`import CompanionCore`, since it is being compiled into the same module:

```bash
swiftc -O Sources/CompanionCore/*.swift /tmp/scratch/main.swift -o /tmp/scratch/ref
/tmp/scratch/ref > /tmp/scratch/ref.txt
```

Then diff the probe's output against `ref.txt`. Print the same labels in the same
order from both sides and the diff points straight at the diverging value.

**Format the numbers explicitly.** Swift's default `Double` description and C#'s
differ (`12.0` vs `12`), so both sides format through `%.4f` / `"F4"` or the diff
fills with false positives.

### The probes as regression tests

They print, and the diffing used to be done by hand against reference files kept
outside the repo — which verifies a port once and catches nothing afterwards,
exactly the failure mode the Lemire bug demonstrates. `scripts/godot-probe`
closes that: it builds, runs a probe, and diffs its output against a reference
committed next to it.

```bash
scripts/godot-probe                 # every probe that has a reference
scripts/godot-probe persistence     # just that one
scripts/godot-probe --record persistence
```

**`activity`, `observe`, `sources`, `inbox`, `status`, `connector`,
`persistence`, `placement` and `care` have stored references**; the other nine
want the same treatment, which is a re-capture from Swift each, not a rename. `--record` is
only correct once the new output has been checked against the Swift original —
recording a regression is exactly as easy as recording a fix.

## Open, in priority order

1. **Store the remaining nine probe references**, above, so the whole suite
   catches regressions rather than only the newest three slices.
2. **The dungeon's surfaces are text, not game UI.** `LoadoutPanel` is a real
   screen in the sense that it has a layout and reads correctly; it is still a
   wall of rows and labels, and it looks like a debug readout rather than
   something you equip a Workling in. Raised 2026-09-04 and deferred to after
   this batch of work. The same is true of the three beats below, more so.
3. **Three of the five beats are still one line of placeholder text.** The steer
   prompt, bank-or-push and the summary share the fight's narration label and
   the round readout. Prep at least has a screen; the two moments the player
   actually plays still look like a debug line.
4. ~~No audio.~~ Done — `core/stage/CombatAudio.cs`, the bed plus sixteen cues,
   fired from the same beats the Swift panel fires them from.
5. **Foe bodies.** The Snag's mesh exists but is not rigged; the Scamp and the
   Monolith have no model at all. Today the Flicker stands in for the first
   three at different sizes and the Pangolin — a pet model — stands in for the
   Monolith. The stand-in scales in `CacheWarrenScene.PresenceFor` are eyeballed
   and want a look.
6. **Animation timing.** The Ram's attack clip is 2.0s; with a 3s countdown each
   exchange runs ~5s. That was a long fight; it is now a long *delve* — four of
   them back to back — so the re-time matters more than it did. Nikhil is
   revising the actions to be quicker and more impactful; contact points are
   stored as fractions (Ram 0.86, Flicker 0.82, Pangolin 0.85) so they survive a
   re-time.
7. **`AttackersTravel`.** Defaults to false because travelling reads as sliding
   — the mesh translates while playing a *stationary* attack animation. A walk
   cycle underneath during the approach is the real fix.
8. **The impact flash reads as invisible in motion** despite showing clearly in
   stills. Not diagnosed; may need more than a tint change.
9. **Tuning nobody has judged yet** — lag hold, catch-up speed, damage number
   sizes, hit-stop duration, the summary dwell. All exported.
10. **Multi-combatant HUD.** Screen-space edge plates work for two; they break at
   3–4 bodies (multiple foes, multiplayer). Nikhil has an idea.
11. **Action trimming.** The Ram ships 17 actions, most of them iteration
    history; the Flicker has a clean five. Needs a human call on which variants
    are the keepers — `keep_actions` takes the set.

### The desktop pet, and why it is not on that list

Nothing above unblocks it, and it is not one task. The dungeon needed ~59% of
`CompanionCore`; the desktop pet needs most of the rest — `PetBrain` (543),
`PetCareStatus`/`PetPresentation` (~330), `ScreenPlacement` (180) — plus the two
things that were not ports at all:

- ~~**Persistence.**~~ Done, 2026-09-04 — see [The save file](#the-save-file).
  This was the real gate and it is open.
- ~~**A Godot desktop shell.**~~ Done, 2026-09-04 — see
  [The desktop shell](#the-desktop-shell). The unknown is answered: Godot can be
  a transparent, borderless, always-on-top, click-through, correctly placed pet
  window.
- **The activity pipeline** (1,096 lines) — and its Windows and Linux
  equivalents, which do not exist in any language yet. That is a cross-platform
  cost the engine decision does not change; it would be owed under SceneKit too.

**The honest order** was persistence first, then the desktop shell proven as a
window that just sits there. **Both are done**, and neither turned up a reason to
stop.

What is left is the pet itself: **`PetBrain` (543) and `PetCareStatus` /
`PetPresentation` (~330)** — ports, with references to diff against, into a
window that already exists. Then the activity pipeline last, because it is the
only part that is not a port at all on two of three platforms.

The open question the shell surfaced and did not answer: **one window or two.**
The pet and the dungeon are separate scenes today, and how you get from standing
on the desktop to a delve decides whether the pet is the main window with the
dungeon as a second one, or a mode switch on a single window. Worth deciding
before `PetBrain` lands rather than after.

## Exporting

**The project could not be exported at all until 2026-09-04**, and nothing in the
editor would ever have said so. `scripts/godot-export` carries the four
requirements; the one worth repeating here is the first, because it fails
*silently*:

**Godot's .NET export needs a solution file.** The editor builds the `.csproj`
directly, so development works perfectly while export is broken. Without
`Worklings.sln` the export reports success and produces an app with **no managed
assemblies in it** — it launches, initialises Metal, loads no C# whatsoever, and
exits without an error. The three behind it: the solution needs
`ExportDebug`/`ExportRelease` configurations (not the `Debug`/`Release` that
`dotnet sln add` writes), it must be a `.sln` and not the `.slnx` a current SDK
generates by default, and Apple Silicon refuses a build unless ETC2 ASTC texture
compression is enabled.

**What an exported build proved**, which an editor run could not:

- **Per-pixel transparency survives export on macOS.** This was the open risk —
  there are reports of transparency working in the editor and rendering black in
  exported builds, particularly on Linux. It holds here: a real `Worklings.app`
  with terminal text legible straight through the window.
- **Borderless, always-on-top, roaming and the walk all hold.**
- **`SaveLocation` flips correctly.** The exported app is the first build for
  which `OS.HasFeature("template")` is true, so it was the first thing ever
  permitted to touch the real save. It resolved the real path rather than a test
  copy, and the file's checksum was identical before and after — the pet only
  reads; only a resolved delve writes.

**Still only claimed, not demonstrated: Windows and Linux.** No machine here for
either. Cross-platform was the whole argument for Godot over SceneKit and it
remains an argument. One documented caveat to carry: **Linux per-pixel
transparency needs a compositing window manager**, which GNOME and KDE enable by
default and lightweight desktops such as Xfce and LXQt do not — so the pet can
fail to be transparent because of the user's setup rather than our code.

Size, and what is deferred about it, is in
[distribution](../process/distribution.md#the-godot-build). Short version: the
game is 16 MB and the engine plus .NET runtime is everything else.

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
- **Godot sizes windows and popups in PHYSICAL pixels.** On a 2x display that
  makes everything half the apparent size it was asked for. This has now caused
  three separate bugs — a half-size menu, a dungeon window that looked like a
  thumbnail, and the letterbox bars — and it will cause more. Size against the
  screen, or multiply by `DisplayServer.ScreenGetScale`.
- **"Facing the camera" is not yaw zero.** The pet scene's camera sits off-axis,
  so a model at yaw 0 is already 30 degrees off. Turning symmetrically around
  zero turns *unevenly* around the viewer. Measure the angle to the camera —
  `atan2(cameraX, cameraZ)` — and work from that.
- **A node that frees itself leaves a disposed C# wrapper behind.** Calling
  `QueueFree` on it throws, and the throw takes out whatever was going to happen
  next. `IsInstanceValid` guards the call; `TreeExiting` clears the field at the
  source.
- **A drag's mouse-up often never arrives** when the window moves under the
  cursor. Poll the button state instead of trusting the event.
- **Swift's `Date` encodes as seconds since 2001-01-01 UTC**, not 1970. Using the
  Unix epoch is a 31-year error that still round-trips perfectly through C#.
- **A nil optional is an absent key, not a `null`.** Swift's synthesized encoder
  uses `encodeIfPresent`. Both decode the same, so this only shows up as a diff.
- **A `.tscn` writes a basis as ROWS; the C# `Transform3D` constructor takes
  COLUMNS.** The same twelve numbers, transposed. Copying a camera or a light
  transform out of a scene file into code silently rotates the whole rig
  somewhere else — the model lands hundreds of pixels off-frame and the viewport
  renders a clean, convincing empty box. `ModelBay.Rig` transposes on the way in.
- **`sed` addresses count input lines.** Deleting line 1 does not renumber line 2,
  so `sed -e '/banner/d' -e '1{/^$/d;}'` leaves the blank line under the banner in
  place — which is enough to make a stored probe reference never match.
