# Dungeon polish backlog

Things Nikhil raised while play-testing the Godot build — the dungeon mostly,
the desktop pet where it comes up — that were deliberately deferred rather than
dropped. Each entry says what was asked for, why it is
worth doing, and where the seam is — so picking one up does not start with
re-deriving the problem.

Ordered roughly by how much the fight gains per hour spent.

## Where this stands, 2026-09-15

Branch **`polish/desktop-size-and-aura`**, six commits, not merged and not
pushed. Build clean, all nine probes pass. Nikhil has played the desktop pet and
the character screen; the aura arc-segment change and the character screen's
window bounds are the parts he has **not** re-run yet.

Done today, and written up in the changelog: the desktop pet size floor, the
travelling auras and the Ram's off-body arcs, and the character screen rebuilt
as two columns with a gear rail.

Tools worth knowing before picking anything up:

- `tools/character_shot.tscn` grabs the character window without the editor, and
  takes `WORKLINGS_SHOT_SIZE=1040x700` — the screen is resizable, and a
  fixed-size shot cannot check that it holds up.
- `WORKLINGS_AURA_INTERNAL=1 WORKLINGS_AURA_SELECT=<slug>` on
  `tools/aura_study.tscn` renders the three variants side by side, and
  `scripts/render-aura-studies.py --internal --only <slug>` packages the video.
  The study wears the shipped shader, so what it shows is what ships.

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

## The Pangolin is too small on the desktop — FIXED 2026-09-15

`DesktopPetScene` now floors the desktop size at `MinDesktopStageHeight` (0.8)
of the Ram's stage height, so the Pangolin renders ~37% larger and the Ram is
untouched. The roster relationship still holds for anything above the floor.

## Auras: the desktop washes them out — FIXED 2026-09-15

Two knobs, because there were two problems. `CreatureAura.For` takes a
per-scene `strength` (dungeon 1.0, desktop 2.6) for the lighting, and
`Recipe` carries a per-creature `Gain` (Ram 1.0, Pangolin 1.8) for how much of
the silhouette that creature's aura actually covers. They multiply.

Washing out was only half of it: everything spatial was written in model units,
at wavelengths longer than the animal, so the whole body pulsed at once instead
of energy travelling over it. All of it is in body units now — normalised to
the mesh's own bounds, with a flow axis down its longest side — and the
Pangolin's crest is three interfering bands at incommensurate speeds, so a
patch lights, dies and re-lights elsewhere rather than sliding tail to snout on
a loop you can time. **The Ram's arcs now leave the body**: a second copy of
the skinned mesh, sharing its skin and skeleton, pushed out along its normals
where a discharge is passing. Still no pose cache. Its fur effect is untouched.

Lanes are gated to a short travelling segment: a whole lit loop around the body
is a hoop rather than a spark, which showed up on the horns, where the shell is
wide compared to what it wraps and the ring floats clear of the model. Found
only once the character bay was wired — the study's dark background hid it.

`AuraStudy` wears the shipped `CreatureAura` rather than a copy of it, so the
comparison videos are the game's shader; `InternalEnergyAura` is deleted.

**Every brightness number in one place.** Two knobs, and they multiply:

| Knob | Where | Values |
| --- | --- | --- |
| Per-scene `strength` | the `CreatureAura.For` call site | dungeon 1.0 · desktop 2.6 · character bay 1.3 |
| Per-creature `Gain` | `CreatureAura.Recipe` | Ram 1.0 · Pangolin 1.8 |

All of them are eyeballed against their own lighting, not measured.

## The Pangolin has no detail at desktop pet scale

**Observed 2026-09-15**, once the size floor and the brighter aura made it
legible enough to judge. It reads as a shape with energy on it; the shell
plates that make it a *clockwork* pangolin do not survive the window size.

Not an aura problem and probably not a shader one — the likely levers are the
albedo's contrast at small sizes and whether the plate edges want an authored
line rather than a baked shadow. Parked deliberately: it is a legibility
ceiling, not a defect.

## The Pangolin's walk is janky

**Observed 2026-09-15** on the desktop pet. The walk clip itself, not the aura
or the new scaling — noted while looking at something else and deferred.

## The character screen: what is left

**Shipped 2026-09-15** as the hybrid of three drawn layouts — two columns, gear
rail, bay that grows. What it does not have yet:

1. **Item art.** `ItemIcon` draws one mark per *slot* — a hone, a shield, a
   star — tinted by tier. Fifteen items want fifteen pieces of art, and until
   they exist a per-item mark would be a lie about how much art there is. This
   is the placeholder and it is holding.
2. **The Inventory tab is still the old list.** It is now a browser rather than
   the only way to equip, and it has not been redesigned to look like one.
3. **The Skills tab is still a placeholder.** The ability tree is designed and
   unbuilt; nothing about the new layout changes that.
4. **Tab order.** Nikhil wants the four reordered, Care staying its own tab.
   Deferred by him, not forgotten.
5. **The bay is hard-coded to the Ram.** `ModelBay` loads `tempest_ram.glb`
   whatever family the Workling is, which is the same gap `DesktopPetScene`
   closed when the Pangolin became wearable.
6. **The bay's aura strength is 1.3 and eyeballed**, like every other strength
   in the game. Its own knob, at the top of `ModelBay`.

**One trap, recorded because it cost an afternoon.** `MaxWidth` caps a control's
width, which Godot has no native way to express. Handing a `Container` child a
rect smaller than its own minimum makes it request a re-sort, which re-hands it
the same rect: the layout never settles, no error is printed, and the process
stops drawing. It is fixed — the rect is clamped to the child's minimum and the
overflow is clipped — but any new use of that pattern can reintroduce it.

Also seen once and not reproduced: a `character_shot` run hung for five minutes
where the identical command then took thirty seconds. Not the deadlock above,
which was reliable. Unexplained.

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
