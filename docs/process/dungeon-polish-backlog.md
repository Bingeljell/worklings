# Dungeon polish backlog

Things Nikhil raised while play-testing the Godot build — the dungeon mostly,
the desktop pet where it comes up — that were deliberately deferred rather than
dropped. Each entry says what was asked for, why it is
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

## Memory: a heavy baseline, and a measurement that lied

**Reported 2026-09-14** — Worklings at ~650 MB idle and peaking at 1.4 GB during
a fight. The first investigation concluded there was an unbounded leak of
~2.9 MB/s. **That conclusion was wrong, and the way it was wrong is the useful
part of this entry.**

### The instrument was the leak

Every measurement was taken with `tools/BeatShot` attached, which grabs the
viewport and writes a PNG on every combat beat. `GetTexture().GetImage()` on a
1280x720 viewport is a **3.7 MB readback per beat**, and beats fire on attack
moves — which is exactly the "~2.7 MB steps landing on attack beats, never on
countdowns" that looked so much like a per-attack leak. It also explains why
every bisect came back identical: disabling the VFX layer, the ghost trails, the
impact sparks, the tweens, the HUD or the animation switching changes none of
the *beats*, so none of them changed the number.

The tell was there and was misread: the growth was invariant under every change
to the thing being measured, which should have pointed at the measuring rather
than at the measured.

### What the dungeon actually does

Run plainly, with no capture tool attached and AutoPlay on:

| Elapsed | RSS |
| --- | --- |
| 20 s | 446 MB |
| 60 s | 473 MB |
| 120 s | 493 MB |
| 180 s | 498 MB |
| 240 s | 507 MB |
| 300 s | 510 MB |

**Asymptotic, not linear.** The first two minutes climb ~47 MB as each of the
four foes appears for the first time — a body made visible, its textures
uploaded, and its ghost trail baked on its first swing, which the code already
documents as a per-model one-time cost. After that it adds ~8 MB over the last
100 seconds and is still flattening. There is no unbounded leak.

### What is worth doing

The baseline is genuinely heavy rather than growing, and that is the real target:

- ~450–510 MB for the dungeon alone, and 311 MB for the pet scene alone —
  **and the shipped app runs both in one process**, which is most of the gap
  between a 510 MB measurement here and a 1.4 GB reading in Activity Monitor.
  Note that Activity Monitor's Memory column reports footprint, which measured
  ~15% above RSS on this process.
- Roughly half of it is graphics. Five characters at 1024² textures, the arena,
  and **4× MSAA**, which `CacheWarrenScene.BuildStage` enables because the thin
  additive geometry of the signature layer crawls badly without it. Cutting MSAA
  is the obvious lever and it is a real quality cost — the effects it protects
  are the ones the layer exists for. Not a trade to make casually.
- Resolution is **not** a factor worth chasing: 720p and 1440p measured within
  noise of each other (475 MB vs 460 MB).
- Ghost trails are the largest single warm-up cost — `GhostCount` baked
  `ArrayMesh` snapshots per character, and the Snag is a 60k-triangle body. If
  the baseline needs to come down, that is where the mass is.

**Never measure memory with `BeatShot` attached.** Run the scene plainly with
`WORKLINGS_AUTOPLAY` and sample `ps -o rss=` from outside, or use `footprint`
for a per-region breakdown.

## The Pangolin is too small on the desktop

**Asked for 2026-09-14.** "The Pangolin on desktop is too small. Can't tell
anything. Should be at least 25-50% bigger than current size. The Ram is nicely
distinguishable, but the Pangolin is not."

It is small on purpose and the purpose is wrong here. `DesktopPetScene` scales
every body by `creature.StageHeight * DesktopUnitsPerStageUnit / modelHeight`,
so the Pangolin lands at 3.24/5.56 of the Ram — the size relationship the roster
encodes for two creatures standing on the same dungeon floor. On the desktop
there is nothing to stand next to, so the comparison buys nothing and costs
legibility: a 3.24-unit creature in a small always-on-top window is a smudge.

**Seam:** the constant and the rule are both in `DesktopPetScene`, four lines
apart. The options, in order of honesty:

1. **Floor the desktop size** — scale by roster height but never below some
   fraction of the Ram's. Keeps a size relationship, guarantees legibility.
2. **Frame to fill instead** — normalise every body to the same rendered
   height, as the aura studies do for their portraits. Simplest, and throws the
   relationship away entirely.
3. **A per-creature desktop multiplier** on `Creature`. Most control, one more
   number per creature to get wrong.

Option 1 is the one to try first: roughly `max(creature.StageHeight, 0.75 *
TempestRam.StageHeight)` puts the Pangolin ~38% larger, inside the range asked
for, without a new field.

## Auras: the desktop washes them out

**Observed 2026-09-14**, alongside the above. `CreatureAura` renders
`blend_add`, and the desktop scene is lit bright — `ambient_light_energy 1.2`
and a 1.8 key light — over white fleece already near full brightness. Additive
blue on near-white barely moves. The Cache Warren is dark, which is exactly why
the same effect reads there.

**Seam:** the strength lives in the shader's `power` term. The fix is a
per-scene multiplier passed in at construction, not a global push — raising it
enough for the desktop would blow the effect out in the dungeon, where it
already works.

## The Snag's motes are not wired in

The Snag's aura is the one that is **not** the shader: it is separate billboard
geometry orbiting the body, positioned from a cached idle pose. It exists only
in `tools/CreatureAuraStudyEffects.cs` and appears in no scene.

Because the motes orbit the whole creature rather than clinging to surface
points, parenting them to the actor root should be enough — they would follow
the body wholesale without needing bone anchors. Nikhil picked variant 03,
Golden Spores, and flagged it as still feeling subtle; judge that at dungeon
distance before pushing it again.

## The crevice mask is a guess

`CreatureAura` derives seams from local darkness in the albedo rather than an
authored emission map, so it can light dark facial details or metal as though
they were fur gaps. Visible on the Pangolin's snout and feet at Breathing
Energy, and on the Ram's neck behind the horns.

Accepted deliberately — it reads well enough at gameplay distance. The fix is a
painted emission mask per creature, which is art work rather than code, and
worth doing when a creature needs its aura to be exact rather than atmospheric.

## Pacing between beats

Not reported, observed. A kill still costs a few seconds of dead air before the
bank-or-push choice appears. `BeatSeconds`, `ActionSeconds`, `ReadSeconds` and
`CardSeconds` are all exported on `CacheWarrenScene` and `WORKLINGS_FAST=1`
collapses them, so this is a tuning pass rather than a build.
