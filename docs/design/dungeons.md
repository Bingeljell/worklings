# Worklings Dungeons

## Status

This is the **full design spec** for the first content system — solo dungeons — and
nothing here is implemented yet. It builds directly on the shipped [progression
sheet](progression.md) (XP, levels, class, five stats) and the [condition
layer](pet-brain.md), and it is the loop's first "spend" step: the place level, stats,
class, and condition finally *do* something instead of only accumulating (see
[Gameplay loop](gameplay-loop.md)).

v1 deliberately resolves against **stats + class + condition only** — no abilities, no
gear — and is built so both slot in later without rework. Abilities land in
`abilities.md`; gear stays a read-time effective-stats layer.

**Every number here is a proposed default — a knob to tune.** They're grounded in the
real stat scales (base stat 5, signature +3/level, others +1/level, so a Level-3 gate
gives a signature stat of 11 and off-stats of 7) but they're first-pass. All of them are
collected in [Tuning knobs](#tuning-knobs) at the bottom for the dial-up/dial-down
discussion, and they'd live in a named `PetCombatRates` struct the same way
`PetProgressionRates` holds the progression numbers — never hard-coded at the call site.

## The combat model

**Turn-based under the hood, auto-resolving, with light tactical input** — an *active
auto-battler*. Every turn is visible and narrated (a line of text plus a sprite
reaction), so the fight reads as a story rather than a spinner. The pet mostly acts on
its own; the player steers with an Approach and the occasional decision point.

- **Encounter** — the pet versus one foe (v1; multi-foe groups are a later revisit).
- **Round** — both combatants act once, ordered by **initiative** (higher Agility acts first; ties to the pet).
- **Action** — one per actor per turn.
- **Resolution** — rounds proceed until one side's combat HP hits zero. Foe at 0 → won. Pet at 0 → **downed**, the delve ends in a retreat.

Combat is **seeded-deterministic**: each encounter draws from a PRNG seeded from the save
state plus a per-delve nonce, so a fight is reproducible and testable in pure
`CompanionCore` (matching the existing deterministic-simulation boundary), while still
feeling varied turn to turn.

### Round loop

```text
for each round:
    order = combatants sorted by effective Agility, desc (pet wins ties)
    for actor in order:
        if actor is the pet: act on the current Approach (or a queued decision)
        else:               act on the foe's behavior script
        resolve action, narrate, update HP
        if either side is at 0 HP: end encounter
    if a decision-point trigger fires: pause for player input
```

### The pet's actions (v1, pre-abilities)

| Action | Uses | Effect |
| --- | --- | --- |
| **Strike** | Power vs foe Guard | Basic attack. `damage = max(1, Power·1.5 − foeGuard·1.0)` ± 15% variance. Hit chance and crit below. |
| **Brace** | Guard | Halves incoming damage for the round and regens a flat **2 HP**. The patient, survivable option. |
| **Signature** | — (v1 fixed) | A once-per-encounter move: a **guaranteed-hit strike at 1.5× damage**, offered at an *opening* decision point. In v1 every class shares this mechanic; the per-class flavor is the **seed of each class's first real ability** (see the walkthrough). |

### Core formulas

```text
maxHP     = 20 + Vitality · 3
strike    = max(1, Power · 1.5 − foeGuard · 1.0)   ± 15%
hitChance = clamp(0.75 + (Agility − foeAgility) · 0.03, 0.25, 0.95)
critChance= Agility · 0.01           (crit deals ×1.5)
```

Every one of the pet's effective numbers is then scaled by the **condition effectiveness
multiplier** (below) before it hits the table.

## The player's input: strategy at decision points

An encounter runs **n turns**, and the player steers it at **decision points** rather
than every turn. This cadence is the core lever the whole encounter is designed around.

- **Approach** — the standing strategy the Workling fights on *between* decisions. The Workling acts automatically on the current Approach, so a hands-off player still gets a coherent fight:
  - **Aggressive** — Strike every round.
  - **Careful** — below `carefulBraceThreshold` HP, *alternate* Brace and Strike until back above `carefulResumeThreshold`. The alternation is load-bearing: bracing every round meant the pet could neither out-heal nor out-damage whatever put it there, so a dip became an unwinnable fight the player only watched. The two thresholds are hysteresis, so recovery isn't instantly undone.
  - **Clever** — Strike, holding the Signature until the foe drops below `cleverFinisherThreshold`, then spend it unprompted. Without that rule Clever resolved identically to Aggressive.
- **Decision points** — moments where the player can *re-choose* the Approach or spend a one-off action (notably **Unleash** the Signature). They fire on a mix of:
  - **Cadence** — every **3 turns**, a steady "reassess" beat.
  - **Events** — a triggered moment: the Workling drops below **30%** HP; the foe winds up a heavy move; an **opening** appears (a foe over-extends — the window to Unleash); the fight changes phase (boss only).

Designing an encounter is therefore largely **designing its decision points** — how often
the cadence beat lands, and which events force a rethink. A well-built foe punishes the
wrong standing Approach and rewards adapting at the right beat; that adaptive pressure is
where an encounter gets its texture, and it's the hook that tactical depth (and later,
abilities) hangs on.

## Stats in combat

The five progression stats, which today only grow, get their combat meaning here:

| Stat | Class | Combat role |
| --- | --- | --- |
| **Vitality** | Wellspring | Max combat HP (`+3`/point) + Brace regen |
| **Power** | Juggernaut | Strike damage (`×1.5`) |
| **Guard** | Aegis | Damage mitigation (`−1.0`/point; doubled while Bracing) |
| **Agility** | Maverick | Initiative, hit chance, crit, evasion |
| **Wit** | Tinkerer | Signature/ability potency + status effects *(mostly latent until abilities)* |

## Condition ↔ combat: the closed loop

Combat HP is its **own transient pool** — it is *not* the Fullness/Energy needs, it
resets between delves, and a lost fight can never zero-out the pet's real condition. But
the two layers touch at the boundaries, in both directions.

**Condition → combat (entry & during):**
- **Effectiveness multiplier.** `effectiveness = max(0.5, avg(needs)/100)` — the same shape as the existing care→XP multiplier (`needs.xpMultiplier`), but with a higher **0.5 combat floor** so neglect weakens without crippling. It scales the pet's effective stats and max HP. Full condition → 100%; half → ~50%.
- **Refusal.** If **any need is critical (≤ 10)** the pet **won't enter** a delve — the doc's "fights below its sheet, or refuses to fight."
- **HP regen** between encounters within a delve restores **30% of max HP × effectiveness** — a rested, happy Workling recovers more mid-delve.

**Condition → combat (on the way out):** the HP you *exit the delve with* moves **all four
conditions**, so a delve is a real event in the pet's day — a triumph lifts it across the
board, an ordeal wears it down across the board:

| Exit tier | HP left | Fullness | Energy | Happiness | Trust |
| --- | --- | :---: | :---: | :---: | :---: |
| **Flawless** | ≥ 90% | +2 | +2 | +10 | +5 |
| **Solid** | 40–90% | −5 | −8 | +5 | +2 |
| **Barely** | < 40% | −10 | −15 | −5 | 0 |
| **Downed** | retreat at 0 | −12 | −20 | −12 | −6 |

Best case is a genuine across-the-board reward — a reason to delve even when the sheet
doesn't strictly need the XP. Worst case is a real setback the care loop then has to
repair. Every magnitude stays inside the **reversible-neglect envelope** — a disastrous
delve leaves the pet drained and shaken, never broken, and care always restores it. This
coupling is the tuning's sharpest edge and needs real playtesting.

## The delve as a journey — encounter & delve UX

The sections above spec the **combat engine**; this one specs the **experience wrapped
around it** — how a delve reads as a journey with anticipation and agency, not just a
chain of fights. Locked 2026-08-03. The combat model, Approach, and decision points are
unchanged; this layer is the shell they run inside.

**The five beats of a delve:**

1. **Briefing (narration, not a mechanic)** — a delve opens with a bit of **storytelling**:
   *"An amazing dungeon looms. If this is the Cache Warren, expect nimble Motes, a grabbing
   Snag, an evasive Flicker… and something heavy at the bottom. You may want to pack for
   accuracy — or bring a Ward."* The tone is flavor and anticipation; the **known-vs-unknown
   is light spice** ("we don't quite know what's down there"), not a depth-gated reveal
   system. The briefing's **one gameplay job is to set up the loadout choice** (beat 2) — it
   tells the player what *kind* of prep this delve rewards.
2. **Prep / loadout (the real player option)** — off the briefing, the player picks a
   **loadout** (equipped gear — see [Items](items.md#where-items-live)) and a **starting
   Approach**. This is where the briefing pays off: a defensive-sounding delve invites heavy
   **Ward** gear; a "slippery, lucky" one invites stacking **Luck**/accuracy. *(Downstream of
   the combat engine — the gear stats only bite once combat reads them.)* Prep is fed by the
   **Character Screen** (the same gear slots).
3. **Fight** — the pet fights **autonomously** on the current Approach and **uses its own
   abilities** ([abilities.md](abilities.md)); the player is hands-off between decisions.
4. **Steer** — at [decision points](#the-players-input-strategy-at-decision-points) the
   player gives **broad guidance** (re-choose the Approach, spend the Unleash) — steering,
   never micromanagement.
5. **Bank or push (press-your-luck)** — after each cleared encounter the player chooses to
   **bank** (exit safely, keep everything earned so far, forfeit the completion bonus) or
   **push deeper** toward the boss at rising **attrition** risk. A **Downed** exit still
   loses half, so pushing is a genuine gamble. A little "we don't know exactly what's next"
   is what gives this beat its tension.

**Rewards map onto press-your-luck with zero rework.** The existing split —
[per-encounter XP + a delve-completion bonus](#rewards) — already encodes the gamble:
banking keeps the per-encounter XP (and any drops) earned so far but forfeits the
completion bonus + ability point + best drop, which remain the **boss capstone**. Pushing
risks a Downed exit (half XP, no bonus). No new reward system is needed — banking is just a
*voluntary* early exit alongside the existing Downed exit.

**Not included (deliberately):** active **scouting**, a separate ceremonial **pre-fight
beat** (the Approach is set at prep and re-chosen at decision points), and any **systemic
info-reveal engine** — the briefing is narration, kept deliberately light. A **branching
node map** (route forks) was considered and rejected for v1 in favor of the linear backbone
+ press-your-luck; it stays a possible later layer. **Solo is fully viable**; multiplayer
(later) is where group composition is meant to matter (the one-stat-per-class, TTK-route
design enables it).

## One dungeon now, many later

**Direction:** eventually Worklings has **multiple dungeon encounters, each with its own
vibe and its own entry button** (the Cache Warren is explicitly *"the first dungeon's
setting, not the world's"*). **But the immediate goal is to get ONE dungeon — the Cache
Warren — actually playable**, built on the discussions above: the real combat engine, a few
**items** as loadout, and enough of the **journey** (briefing narration → loadout → the
encounter chain → press-your-luck → rewards) that a user can *experience* a delve end to
end. Breadth (more dungeons, per-dungeon buttons) comes after the first one proves the loop.
The [vertical-slice build notes](#what-a-first-playable-encounter-needs) below are the
starting point; this first playable dungeon widens that slice just far enough to feel like a
delve, not the whole system at once.

## The battle stage — camera & staging

**Rendering approach:** a live SceneKit 3D room with the existing baked 2D sprite sheets
billboarded into it as actors — not a flat painted backdrop (today's Cache Warren arena)
and not live 3D characters either. The room gives real depth and lighting for free; the
actors stay exactly the assets the [3D→2D pipeline](sprite-prompts.md) already produces,
so nothing about that pipeline or its pose contract changes. This follows the standing
call on rendering: baked sprites stay the actor format everywhere in the dungeon, live 3D
is reserved for the character/gear screen.

**The stage is four depth bands**, near to far: the party floor, an arena gap (where
attacks and VFX play out), a raised foe platform, and a back wall. No side walls — an
early pass added them for a sense of enclosure, but they didn't help read the
composition and were dropped. **The camera never moves once a dungeon is authored** — it's
staged like a backlot, not a walkable level, so nothing outside the frustum ever needs
building.

**Per-dungeon (and per-encounter) variety comes from a diagonal entry/exit direction**
across the frame, not a different camera or new geometry: the party enters one corner,
the foes hold the opposite, and — planned, not yet built — after a win the party walks
past the cleared foe toward the exit corner and the shot dissolves into the next
encounter. The direction can rotate 90° each encounter ("turn left") as the cheap-variety
mechanism for a future dungeon builder: same room kit, same camera rig, just a different
corner pairing.

**Found with the Dungeon Stage Camera Tool** (🐾 menu → *Dungeon Stage Camera Tool…*,
debug builds only, `Sources/Worklings/DungeonStageCameraTool.swift`) — an orbitable grey
blockout of the four bands with placeholder party/foe billboards, so a standoff
composition can actually be judged rather than guessed from a still image. Its window is
a **fixed 1280×720 (16:9, scaled 1080p)** and deliberately not resizable: the same camera
transform frames differently at a different aspect ratio, so numbers are only meaningful
measured against that fixed shape.

**The Cache Warren's locked camera:**

```
position   x 16.65   y 17.76   z 13.14
target     x -1.92   y -0.10   z 2.29
azimuth 59.7°   elevation 39.7°   radius 27.95   roll 0.0°
```

Re-centered 2026-08-27: the prior target (-3.60, -0.63, 5.16) skewed the whole 16:9 frame
toward the top-right, leaving a large dead zone bottom-left. Same azimuth, elevation, and
radius — only the look-at point moved, so the diagonal-corner composition below is
unchanged, just better framed within the fixed shot.

Diagonal direction: **bottom-left (party) → top-right (foes)**, held for every encounter
in this dungeon. Chosen over a lower, closer "cinematic" angle tested alongside it —
getting low or close to the party's back costs the depth read between the two ranks, and
the drama is meant to come from VFX and attack animations at combat time, not the resting
camera.

**Wired into the real arena (2026-08-20):** the flat painted cave backdrop is gone — the
live `DungeonStageScene` room, at the locked camera above, is what the Cache Warren's
actual in-game panel renders now (room-only; see `Sources/Worklings/DungeonStage3D.swift`
and `CombatPanel.swift`'s `ArenaBackground`). The panel itself resized from 600×480 to
1280×720 to match. Party and foe **still render as the old flat side-by-side SwiftUI
columns** — untouched by this pass, not yet repositioned into the diagonal corners or
turned into real scene billboards.

**How the environment art has to work, so it doesn't look janky:** the room is real 3D
geometry (the four bands), so its surfaces get **materials** — prompt-generated PBR
albedo/normal/roughness/height sets keyed to the "buried machine strata" identity —
applied directly onto that existing blockout, the same way any 3D scene is textured.
**Not** a single flat painted image dropped in as a backdrop plane: that's exactly what
would clash, since the near geometry has real depth/perspective from the locked camera's
elevation and a flat image behind it wouldn't. A painted matte is still fair game for
anything genuinely distant glimpsed *through* an opening past the back wall — safe from
ever revealing its flatness specifically because the camera is fixed and never exposes a
parallax mismatch — but the room itself gets textured as 3D, not papered over with a
picture.

**Not yet done:** real party/foe marks and turning them into real scene billboards
(currently the untouched flat 2D columns), the walk-past-and-dissolve transition, and the
environment materials themselves — the stage is still grey blockout boxes, nothing
textured yet. Every future dungeon needs its own angle found the same way — only the
Cache Warren's is locked. **Testing new characters and effects happens in the Dungeon
Stage Camera Tool first** (same room, real camera, debug billboards) — the plan is to
extend it to cycle real animated frame sequences before anything gets wired into the
actual arena, rather than iterating directly against production code.

### Effects — baked vs. live

Two different kinds of "effect" on a character, deliberately handled differently:

- **Pose-inherent effects stay baked into the sprite.** The signature pose's
  family-coloured aura (§7 of [the bake spec](bake-spec.md) — Elemental's orange fire,
  Wildkin's green nature-glow, etc.) is part of that pose's own render, same as it always
  was. It doesn't change per-encounter, so there's nothing to gain by pulling it out.
- **Everything else was meant to be a live layer — the Tempest Ram's crackle overturned
  that.** The original plan: hit impacts, elemental bursts, character theming all get
  built as reusable in-engine effects (particles/shaders/sprite overlays layered on top
  of the billboard) rather than rendered into new Blender frame sequences, since baking
  multiplies every effect variant against every pose × every character. **First real test
  case (2026-08-27) went the other way.** A live 2D overlay on the flat billboard was
  tried first and abandoned — it has no information about the mesh's actual surface, so
  it couldn't trace the horn ridges / wool-tuft edges / joints the reference art called
  for ("electricity crawling on the body," contours taken into account). Rebuilt instead
  as a Blender material effect: a per-vertex curvature attribute drives an emissive
  shader, with a frame-driven traveling sweep (forward pass → pause → reverse pass →
  pause, tuned to loop seamlessly with the idle action) plus small floating spark/arc
  anchors. Full story and the iteration it took to get there in the 2026-08-27 changelog
  entries. **Revised guidance**: an effect that needs to read as tracing the character's
  real 3D form (crackle veins, armor seams, anything contour-following) belongs baked
  into the material, at the cost of a re-bake per action; an effect that's genuinely
  independent of surface shape (a screen-space flash, a detached particle burst) is
  still a better fit for a live layer. Only baked for the Ram's idle pose so far — the
  walk/headbutt/damage actions would each need their own pass/pause timing re-tuned to
  that action's own frame range, not a blind copy of idle's numbers.

## The Cache Warren

*Setting — the **first** dungeon's, not the world's. Worklings is a broad universe, and
dungeons can span many work- and productivity-themed settings; this is one, not the
canon. The Cache Warren is the buried strata of the machine the Workling lives in,
rendered as a fantasy underworld whose bestiary **dual-codes** to work-chaos the way the
class names do (Wellspring, Juggernaut). Later dungeons are free to inhabit entirely
different work universes — the combat model above is setting-agnostic.*

**Gate:** Level 3. **Shape:** three encounters, then a mini-boss. **Cadence:** limited
attempts per day (a stamina, refreshed on `dailyWake`), so a delve is a returning ritual.

The four foes form a deliberate mechanic curve — a warm-up, a wall, an accuracy test,
then an endurance check:

| # | Foe | HP | Pow | Guard | Agi | Wit | Hook |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | **Mote** | 30 | 4 | 1 | 6 | 1 | Trivial warm-up; a few rounds to teach the loop. |
| 2 | **Snag** | 30 | 7 | 6 | 3 | 3 | Tanky grabber; its bite lowers your Agility for a turn. Rewards Power / patience. |
| 3 | **Flicker** | 18 | 6 | 2 | 14 | 4 | Evasive; hard to hit, folds fast. Rewards accuracy / the Signature opening. |
| B | **Monolith** | 90 | 12 | 12 | 2 | 2 | Slow mini-boss; heavy wind-up hits telegraphed a turn ahead. Rewards Bracing the big blow and grinding. |

Combat HP **carries across** the three encounters (regen between them scales with
condition); the boss is the attrition payoff. HP fully restores after the delve.

### Enemy behaviors & abilities

Each foe runs a small **behavior script** — what it does on its turn — and the tougher
ones carry a named ability. These lean on the same **status-effect** and **telegraph**
primitives the [class abilities](abilities.md#the-status-effect-system-these-need) need, so
both get built once. Magnitudes are held as knobs.

| Foe | Behavior | Ability |
| --- | --- | --- |
| **Mote** | Attacks every turn; no tactics. | — (pure filler; teaches the loop). |
| **Snag** | Attacks; occasionally grabs instead. | **Snare** — on a grab, applies a timed debuff lowering the pet's Agility (its initiative and accuracy sag). Rewards Power or riding it out. *(Knobs: snare magnitude, duration, grab frequency.)* |
| **Flicker** | Darts in and out; hard to pin. | **Blur** (passive high evasion) plus an occasional **Phase** that dodges the next hit outright — then it **over-extends**, opening the Unleash window. *(Knobs: evasion, phase chance, opening frequency.)* |
| **Monolith** | Slow; acts rarely but heavily. | **Slam** — a heavy hit **telegraphed a turn ahead** (a decision point to Brace or eat it), and it **hardens** (raises Guard) at HP-threshold **phases**. *(Knobs: slam multiplier, telegraph length, phase thresholds.)* |

The design intent of the curve: Mote teaches the loop, Snag punishes a glass-cannon
Approach, Flicker demands accuracy (or Tinkerer's Expose), and Monolith rewards reading a
telegraph and Bracing — so by the boss the player has used every part of the model once.

## A worked encounter: the Flicker

The Flicker is the teaching foe for "an evasive target," walked here with a **Level-3
Aegis** (Guard 11; Vitality/Power/Agility/Wit 7) at full condition — `maxHP = 20 + 7·3 =
41`, effectiveness 1.0. Flicker is Agility 14, so it acts first each round.

```text
Approach: Aggressive (Strike).   Flicker 18 HP · Your Workling 41 HP
Round 1 — Flicker phase-darts: max(1, 6·1.5 − 11) = 1 dmg → 40.
          Your Workling Strikes — hit roll 0.54 → misses (it blurs aside).
Round 2 — Flicker darts: 1 → 39.
          Strike lands: 7·1.5 − 2 = 8 dmg → Flicker 10.
Round 3 — (cadence decision point) hold Aggressive.
          Flicker darts: 1 → 38.  Strike misses.
Round 4 — Flicker darts: 1 → 37.  Strike lands: 8 → Flicker 2.
          The Flicker over-extends — an OPENING (event decision point).
Round 5 — Unleash: guaranteed-hit Signature, 8·1.5 = 12 → Flicker down. Won.
```

Exit at 37/41 = 90% → **Flawless**. Narration + a sprite reaction per line is the whole
texture; the Aegis can't really be hurt (its Guard 11 shrugs the darts to 1) but has to
grind through a 54% hit rate, and the Unleash at the opening closes it cleanly.

## The same fight, per class

One encounter across five classes is where class identity first becomes *mechanical*, and
where the first ability ideas fall out.

- **Maverick (Agility 11)** — hit chance `0.75 + (11−14)·0.03 = 0.66` and out-initiatives more; wins the accuracy war outright. *Ability seed: a guaranteed-hit or extra-turn burst.*
- **Tinkerer (Wit 11)** — its Signature reads the pattern and negates the evasion. *Ability seed: an accuracy debuff / "mark" that makes a slippery foe hittable.*
- **Juggernaut (Power 11)** — Strike `11·1.5 − 2 = 14.5`; misses often but two clean hits end it — feast-or-famine. *Ability seed: a big committed swing that can't miss but costs a wind-up.*
- **Aegis (Guard 11)** — the worked example: can't be out-damaged, grinds through the misses. *Ability seed: a counter that punishes the foe's attack.*
- **Wellspring (Vitality 11)** — `maxHP = 53`, out-attrits everything; healing turns a long fight trivial. *Ability seed: a regen / second-wind that makes attrition the win condition.*

Reading these five back-to-back is how we'll shape the **first ability per class** — each
is the shared v1 Signature promoted into a real, costed, class-specific action. That work
moves to `abilities.md`.

## Rewards

- **Per encounter** — XP on a kill: Mote 8, Snag 20, Flicker 25, Monolith 100.
- **Delve completion** — +50 XP and **1 ability point** (the currency deliberately *separate* from stat growth, per [progression](progression.md#levels)).
- **Downed exit** — half XP for the encounters actually cleared; no completion bonus, no ability point.
- **Gear** — deferred; when it arrives it drops here as read-time effective-stat modifiers.
- **XP channel** — dungeon XP has its **own daily cap (300)**, separate from the work/care caps, so grinding delves can't cannibalize the work-driven economy but is still bounded. *(Open question — could instead share the overall cap.)*

## New sprite states this needs

Combat needs poses the current [twelve-frame contract](characters.md) doesn't have. All
three families share the contract, so **every new pose is authored for all three sheets**
— exactly the multiplier that makes the planned 3D→2D asset pipeline pay for itself
(author/rig once, render every pose for every family).

| Pose | When it shows |
| --- | --- |
| **Strike** | landing an attack |
| **Hurt** | taking a hit (recoil) |
| **Low-HP** | staggered / on the ropes |
| **Victory** | encounter or delve won |
| **Downed** | retreat at 0 HP |
| **Brace** | defending |
| **Signature** | unleashing the class move |

Sheet/code contract: each sheet is a **4×5 grid** of 256px cells (1024×1280), mapped by
`WorklingSpriteFrame`'s explicit column/row cases. Rows 0–2 hold the original twelve
companion poses; row index 3 holds Strike, Hurt, Low-HP, and Victory; row index 4 holds
Downed, Brace, Signature, and one unused cell. Adding a pose = extend each family's sheet
+ add the enum case; no new rendering path is needed. Ready-to-use generation prompts for
these poses (and the Cache Warren foes) live in [Sprite prompts](sprite-prompts.md).

## What a first playable encounter needs

Stepping back from the design to the *build*: what has to exist for a single encounter to
be playable end-to-end. **Decided: ship a vertical slice first** — one encounter, not the whole delve — to prove
the loop, then widen.

### The vertical slice (smallest playable thing)

**The pet versus one Mote (or Flicker): three base actions, one Approach, one decision
point, a win/lose, and one reward + condition delta applied.** No delve chain, no
abilities, no items, no boss. If that reads as a fight and the numbers flow back into the
sheet and needs, everything else is additive.

### Domain — `CompanionCore` (pure, testable)

- **Combat state model** — `CombatState` (round, turn order, each combatant's HP + statuses, the RNG seed, current Approach, any pending decision, the narration log) and `Combatant` (effective stats, HP, statuses).
- **Seeded PRNG** — a small deterministic generator in `CompanionCore`, seeded from save state + a delve nonce, so a fight replays identically under test.
- **Resolver** — `step()` advancing one action as a pure function of `(state, seed)`: initiative, action resolution via the [formulas](#core-formulas), status ticks, win/lose check.
- **Effective-stat function** — base + (gear, when it exists) × condition effectiveness; the resolver reads this, never raw base stats.
- **Foe data** — the [stat blocks and behavior scripts](#enemy-behaviors--abilities) as data, not code branches.
- **Decision-point detection** — the cadence + event triggers that pause for input.
- **Orchestration** — encounter → (later) delve: HP carry, inter-encounter regen, entry gate (level + [refusal](#condition--combat-the-closed-loop)), and exit-tier computation.
- **Reward + feedback application** — grant XP (reuse the existing progression path), apply the [exit-tier condition deltas](#condition--combat-the-closed-loop) to needs, and (later) award ability points / items.
- **Behavioral checks** — determinism/replay, each formula, decision triggers, exit-tier mapping, refusal, reward caps, and the per-class differences. This is the layer `CompanionCoreChecks` covers.

### App — `Worklings` (timing, animation, presentation)

- **A combat surface** — **decided: a dedicated combat panel** that opens near the pet for a fight and closes back to the quiet desktop companion afterward (chosen over an expanded care card or a full window — room for two combatants without cramping the card, and it preserves the companion feel). It needs: pet + foe sprites, two HP bars, the narration log revealing turn by turn, an Approach control, and a decision-point prompt.
- **An entry point** — how a delve starts: a paw-menu item ("Enter the Cache Warren") and/or a button on the Stats tab, gated by level and blocked on critical need.
- **Sprite wiring** — the new [combat poses](#new-sprite-states-this-needs) added to `WorklingSpriteFrame` + extended family sheets, and a **foe-sprite path** (foes are a new asset type — likely their own sheet/loader, since today only the pet is drawn).
- **Pacing** — turns revealed with a delay so it reads as a fight, honoring Reduce Motion (instant/settled fallback).
- **Narration** — templated lines per action/outcome (the text beside each sprite beat).
- **Write-back** — apply results through `PetSession` and persist.
- **Accessibility** — the log, HP values, and prompts exposed to VoiceOver; decisions keyboard-reachable; colour never the only signal — matching the [interaction](interaction.md#accessibility) bar the rest of the app holds.

### Persistence (additive, backward-compatible)

New save fields, all additive so an old save reads cleanly: delve stamina / attempts-per-day
(reuse `DailyTally` for the rollover), dungeon-XP daily bookkeeping, and — as their layers
land — ability points + unlocked ids ([abilities](abilities.md#the-ability-point-currency))
and owned/equipped items ([items](items.md#persistence)). Bump the save version once,
additively.

### Content minimum for the slice

One foe fully defined (Mote is simplest), the three base actions, and the minimum sprites
to not look broken: pet **Strike / Hurt / Victory / Downed** (idle reused) plus **one foe
sprite**. Janky first-pass art is fine (see [sprite prompts](sprite-prompts.md)).

### Decisions

1. **Combat surface** — ✅ **decided: a dedicated combat panel** (over expanded card / full window).
2. **First cut** — ✅ **decided: the vertical slice** (one encounter) before the full delve.
3. **Foe-sprite pipeline** — *open:* sheet layout and loader for a non-pet drawable.
4. **Save-migration** timing — *open:* one version bump now covering the known additive fields, or bump per layer.

## Tuning knobs

Everything dial-able, in one place, for the dial-up/dial-down discussion. Proposed to live
in a `PetCombatRates` struct alongside `PetProgressionRates`. **All values below are held —
noted, not being tuned yet.** Enemy-ability, [ability](abilities.md#knobs), and
[item](items.md#knobs) knobs are held in their own docs.

| Knob | Default | Raising it… |
| --- | --- | --- |
| `baseHP` | 20 | Everyone survives longer; flattens the Vitality gap. |
| `vitToHP` | 3 | Makes Vitality (and Wellspring) matter more for survival. |
| `powScale` | 1.5 | Faster kills; Power/Juggernaut swingier. |
| `guardScale` | 1.0 | Mitigation matters more; Aegis tankier, low-Power stalls out. |
| `strikeVariance` | ±15% | More/less randomness in damage. |
| `baseHit` | 0.75 | Fewer misses overall; dulls evasion mechanics. |
| `agiToHit` | 0.03 | Widens the accuracy gap Agility buys; evasive foes get sharper. |
| `hitFloor / hitCeil` | 0.25 / 0.95 | Bounds on the worst/best hit chance. |
| `critPerAgility` | 0.01 | More crits; Agility/Maverick spikier. |
| `critMultiplier` | 1.5 | Bigger crit payoff. |
| `braceMitigation` | ×0.5 dmg | How much Brace rewards patience. |
| `braceRegen` | 2 HP (floor) | Minimum sustain from defending, whatever the pool. |
| `braceRegenFraction` | 8% of max HP | Sustain from defending, scaled to the pool so a flat number doesn't decay into nothing. |
| `carefulBraceThreshold` | 40% HP | When Careful enters its defensive band. |
| `carefulResumeThreshold` | 60% HP | When Careful leaves it. The gap is hysteresis — a single threshold latched the pet into Brace permanently. |
| `cleverFinisherThreshold` | foe at 35% HP | When Clever spends the Signature it's been holding. |
| `signatureMultiplier` | 1.5 | Power of the once-per-encounter Unleash. |
| `decisionCadence` | every 3 turns | Less frequent = more hands-off, fewer choices. |
| `lowHPEvent` | 30% | When the "faltering" decision point fires. |
| `combatEffectivenessFloor` | 0.5 | How hard neglect can nerf combat (lower = harsher). |
| `refusalThreshold` | need ≤ 10 | How bad condition must get before the pet won't delve. |
| `interEncounterRegen` | 30% × eff | Attrition pressure across a delve. |
| exit-tier deltas | see table | The whole combat→condition feedback strength. |
| `delveGateLevel` | 3 | When the first dungeon unlocks. |
| `delveAttemptsPerDay` | 3 | How much a player can grind per day. |
| foe stat blocks | see table | Each encounter's difficulty and mechanic weight. |
| encounter/delve XP | 8/20/25/100 · +50 | Reward pace vs the work economy. |
| `dungeonXPDailyCap` | 300 | Ceiling on grind XP. |
| `abilityPointsPerDelve` | 1 | How fast the ability currency accrues. |

## Open questions (for iteration)

1. **Combat model** — confirm the active-auto-battler spine before deeper tuning.
2. **Condition↔combat magnitudes** — the exit-tier deltas, the 0.5 effectiveness floor, and the regen rate need playtesting; the risk is combat souring the daily care loop if set too harsh.
3. **XP channel** — separate dungeon cap (proposed) vs sharing the overall daily cap.
4. **Randomness** — confirm seeded-per-encounter PRNG (reproducible, testable) over live RNG.
5. **Encounter breadth** — v1 single-foe; when do multi-foe groups and targeting arrive?
6. **First abilities** — promote each class's Signature into its first costed ability (moves to `abilities.md`).
7. **Combat-impact readability at range** (2026-08-22) — the diagonal stage keeps party
   and foe on opposite corners, real physical distance apart. How does an attack actually
   *land* visually across that gap? Parked, not designed — effects (see above) are the
   likely answer but unconfirmed. One stopgap floated, deliberately janky but cheap: both
   combatants fly to the center on an attack, the attacker hits, the defender winces, both
   fly back to their corners. Worth a mock before committing either way.
8. **Impact frames** (2026-08-27) — hit-stop, camera shake, dust/debris bursts on a
   stomp or landed hit; the single biggest lever for combat feeling weighty rather than
   just animated. Genuinely blocked, not just unstarted: it needs to hook into the real
   combat loop (`CombatPanel.swift`'s beat system) and shake/freeze a real `SCNNode`,
   and the real arena still renders party/foe as flat SwiftUI columns, not scene
   billboards (item below). Can be prototyped in the Dungeon Stage Camera Tool first
   (freeze + shake + dust burst on the Ram's headbutt, same "test in the tool first"
   pattern as everything else) without waiting on that wiring.
9. **Flat painted backdrop vs. the live 3D room — under active test, not decided.**
   dungeons.md's "no flat backdrop" call (§"The battle stage") is being tested against
   real reference art, not assumed correct — see the 2026-08-27 changelog entries. A
   toggleable backdrop mode exists in the Dungeon Stage Camera Tool now. Color match
   alone isn't the bar — the working diagnosis for "characters look pasted on, not from
   the same universe" is four separate things, only the first of which is built:
   - [x] **Contact shadow** — a ground decal anchoring feet to the floor. Done, tuned
     against the backdrop's own near-black ground values.
   - [ ] **Atmospheric depth/haze** — the foe corner sits further from camera than the
     party corner; the backdrop has real depth falloff (dimmer/softer with distance)
     that the crisp, full-contrast sprite doesn't pick up at all yet.
   - [ ] **Render-fidelity match** — the backdrop is a soft painted/photoreal render:
     the character cutouts are clean 3D-render edges. That gap alone reads as "pasted
     on" independent of color or shadow.
   - [ ] **Directional light match** — sprites are lit by one fixed, even three-lamp rig
     (bake-spec §3) regardless of where they stand; the backdrop's light is local and
     directional. A flat color tint (tried, see changelog) can't fix this — it needs an
     actual rim/kicker light term, likely a bake-time change per dungeon, not a runtime
     trick. **In progress (2026-08-28):** a runtime fake — a fragment-shader gradient
     across the billboard's UV space, warm/bright toward the corner's light source and
     cool/dim away from it — added as a third toggle in the camera tool, with direction
     and strength as live-tunable fields since the UV-orientation guess is untested.
     Not yet judged against reference art; a strength-clamp bug found on the first run
     (values past 1.0 washed the sprite to a flat color) is fixed.
   Whichever way this resolves, it resolves for *all* future dungeons, not just Cache
   Warren — worth deciding deliberately once the checklist above is further along,
   not mid-way through it.
