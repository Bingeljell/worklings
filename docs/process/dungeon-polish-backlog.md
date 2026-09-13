# Dungeon polish backlog

Things Nikhil raised while play-testing the Godot dungeon that were deliberately
deferred rather than dropped. Each entry says what was asked for, why it is
worth doing, and where the seam is — so picking one up does not start with
re-deriving the problem.

Ordered roughly by how much the fight gains per hour spent.

## Whose turn is it

**Asked for 2026-09-14.** Some indicator that it is the player's turn — "an
arrow bobbing above their head pointing at them, or a slow circular glow below
them; solid border, subtle glow under them".

Right now the only cue is the command bar going from dim to lit, which is at the
bottom of the frame while the player is looking at their Workling in the middle
of it. The foe already declares its intent over its head (`IntentBadge`), so the
Workling having nothing is also an asymmetry the eye notices.

**Seam:** the phase is already explicit — `Phase.Choosing` is exactly "it is your
turn" — and `IntentBadge` is a worked example of a screen-space overlay tracking
a creature in 3D. A ground ring wants to be world-space instead, and
`VfxPrimitives` already builds ground marks for the signature layer.

## Per-attack and per-signature sound effects

**Asked for twice, 2026-09-13 and 2026-09-14.** "Individual sound effects for
the spells — lightning, fireball, whiplash, etc., to make it more fun."

Every blow currently plays from one shared set in `CombatAudio` (`Hit`, `Crit`,
`Unleash`, `Slam`, …), so a Ram's headbutt, a Snag's whip and a Flicker's swipe
are the same noise and all four signatures share one sound. The *visual*
signature layer already gives each creature an identity; the audio is the one
system that says nothing about it.

**Seam:** `AbilitySignature.For(modelName)` already picks the visual. The sound
should key off the same value rather than a generic `CombatSound.Unleash`, so
adding a creature stays one roster entry. Per-creature basic attacks want the
same treatment through the roster, not a switch at the call site.

## Camera framing and combatant spacing

**Asked for 2026-09-14.** "The camera needs to be pulled out a little and the
Workling and foe need to be moved apart more."

The framing was locked for a 1v1 study against a Scamp. It does not hold for the
Monolith: at 7.50 units the mini-boss nearly touches the Workling, and there is a
lot of dead floor bottom-right in every frame. Party formations are unproven for
the same reason.

**Seam:** `StageSet` owns the camera and the marks, and the capture tool builds
the same arena, so a change is reviewable without launching the game. Note that
the current angle is a deliberate locked decision — changing it is a real call,
not a tweak.

## The loadout screen

**Asked for 2026-09-14.** "The UI for the loadout needs a complete overhaul.
It's all text and unintuitive. I'd like it similar to an MMO or RPG or something
like PoE where I can see what all my character has as icons around my character
based on their slots available."

`LoadoutPanel` is a text grid — a row per slot, cycled with the arrow keys. The
target is the standard paper-doll: the Workling in the middle, slot frames
arranged around it, items as icons, hover for detail.

**Seam:** the panel already owns the whole interaction and hands back a
`PetState`, and equipping already validates ownership through `PetState`, so the
rewrite is presentation only. It needs item icons, which do not exist yet — the
placeholder rule applies: build the real layout with placeholder art rather than
letting missing icons justify another text wall.

## Memory: the dungeon leaks, and the baseline is heavy

**Reported 2026-09-14** — Worklings at ~650 MB idle and peaking at 1.4 GB in a
single fight, with the dungeon feeling laggy. Measured rather than assumed, and
the answer is that the baseline is defensible and the growth is not.

**It is dungeon-specific.** The desktop pet scene sits flat at 311 MB for
minutes with no growth at all. A delve grows monotonically: footprint 633 MB →
778 MB over 50 seconds, roughly 2.9 MB/s, and it never comes back down.

**The growth is on both sides of the fence**, over that same 50 seconds:

| Region | Start | End |
| --- | --- | --- |
| graphics (unmapped) | 281 MB | 321 MB |
| MALLOC_SMALL | 134 MB | 174 MB |
| MALLOC_LARGE | — | 71 MB |
| IOAccelerator (graphics) | 103 MB | 103 MB |

Godot's own counters agree and narrow it: `MemoryStatic` climbs in quantised
steps of about 2.7 MB that land on attack beats rather than on countdowns, while
**node count and resource count stay flat** (517 nodes, 89 resources, zero
orphans). So it is not leaked nodes. It is runtime-created resources or
`RefCounted` objects that outlive the node that made them, plus something
GPU-side.

**Ruled out by bisect**, each with its own 45-second run:

- the signature VFX layer (`AbilityEffects = false`) — still grew ~43 MB
- attacker travel and ghost trails (`AttackersTravel = false`) — still grew ~49 MB
- the impact spark burst (early-return in `SpawnSpark`) — still grew ~38 MB
- combat audio — players are built once in the constructor, and the whole audio
  directory is 3.2 MB

So it is in something always-on and per-attack that none of those toggles reach.
`DamageNumbers`, the hit-stop tweens, and per-attack material or mesh creation
inside `ImpactFrames.Flash` are the unexamined candidates.

**The baseline is separately worth a look.** Of 676 MB resident, ~338 MB is
graphics — five characters at 1024² textures, the arena, and **4× MSAA enabled
in `CacheWarrenScene.BuildStage`** for the thin additive geometry the signature
layer is made of. MSAA at that level on a 720p viewport is a real cost and is
the first thing to measure against.

**Reproduction** (the harness already exists):

```bash
WORKLINGS_SAVE=/tmp/pet.json WORKLINGS_BEAT_OUT=/tmp/beats \
  WORKLINGS_BEATSHOT_REALTIME=1 \
  godot --path godot/worklings res://tools/beat_shot.tscn
# in another shell, against the running pid:
footprint -p <pid>
```

Adding a `Performance.GetMonitor` line to `BeatShot._Process` gives the
engine-side counters; that instrumentation was temporary and is not committed.

## Pacing between beats

Not reported, observed. A kill still costs a few seconds of dead air before the
bank-or-push choice appears. `BeatSeconds`, `ActionSeconds`, `ReadSeconds` and
`CardSeconds` are all exported on `CacheWarrenScene` and `WORKLINGS_FAST=1`
collapses them, so this is a tuning pass rather than a build.
