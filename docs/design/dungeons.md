# Worklings Dungeons

## Status

This is the **design direction** for the first content system — solo dungeons — and
nothing here is implemented yet. It builds directly on the shipped [progression
sheet](progression.md) (XP, levels, class, five stats) and the [condition
layer](pet-brain.md), and it is the loop's first "spend" step: the place level, stats,
class, and condition finally *do* something instead of only accumulating (see
[Gameplay loop](gameplay-loop.md)).

v1 deliberately resolves against **stats + class + condition only** — no abilities, no
gear — and is built so both slot in later without rework. Abilities land in
`abilities.md`; gear stays a read-time effective-stats layer.

## The combat model

**Turn-based under the hood, auto-resolving, with light tactical input** — an *active
auto-battler*. Every turn is visible and narrated (a line of text plus a sprite
reaction), so the fight reads as a story rather than a spinner. The pet mostly acts on
its own; the player steers with an approach at the start and the occasional tactical
beat.

- **Encounter** — the pet versus one foe (v1; multi-foe groups are a later revisit).
- **Round** — both combatants act once, ordered by **initiative** (Agility). Ties resolve to the pet.
- **Action** — one per actor per turn.
- **Resolution** — rounds proceed until one side's combat HP hits zero. Foe at 0 → won. Pet at 0 → **downed**, the delve ends in a retreat.

Combat is **seeded-deterministic**: each encounter draws from a PRNG seeded from the
save state plus a per-delve nonce, so a fight is reproducible and testable in pure
`CompanionCore` (matching the existing deterministic-simulation boundary), while still
feeling varied turn to turn.

### The pet's actions (v1, pre-abilities)

| Action | Uses | Effect |
| --- | --- | --- |
| **Strike** | Power vs foe mitigation | Basic attack. Hit chance shaded by attacker vs defender Agility; can crit off Agility. |
| **Brace** | Guard | Raises mitigation for the round and grants a little HP regen. The survivable, patient option. |
| **Signature** | the class's signature stat | A once-per-encounter class-flavored move. This is the **seed of each class's first real ability** — see the per-class walkthrough. |

### The player's input: strategy at decision points

An encounter runs **n turns**, and the player steers it at **decision points** rather
than every turn. This cadence is the core lever the whole encounter is designed around.

- **Approach** — the standing strategy the Workling fights on *between* decisions: **Aggressive** (bias Strike / damage), **Careful** (bias Brace / survival), or **Clever** (bias Signature / exploit). The Workling acts automatically on the current Approach, so a hands-off player still gets a coherent fight.
- **Decision points** — moments where the player can *re-choose* the Approach or spend a one-off tactical action. They fire on a mix of:
  - **Cadence** — every *x* turns, a steady "reassess" beat.
  - **Events** — a triggered moment: the Workling drops low, the foe winds up a heavy move, an opening appears, the fight changes phase.

Designing an encounter is therefore largely **designing its decision points** — how often
the cadence beat lands, and which events force a rethink. A well-built foe punishes the
wrong standing Approach and rewards adapting at the right beat; that adaptive pressure is
where an encounter gets its texture, and it's the hook that tactical depth (and later,
abilities) hangs on.

## Stats in combat

The five progression stats, which today only grow, get their combat meaning here:

| Stat | Class | Combat role |
| --- | --- | --- |
| **Vitality** | Wellspring | Max combat HP + healing potency |
| **Power** | Juggernaut | Physical damage dealt |
| **Guard** | Aegis | Damage mitigation |
| **Agility** | Maverick | Initiative, hit/evade, crit |
| **Wit** | Tinkerer | Signature/ability potency + status effects |

Illustrative formulas (placeholders, to live in named tuning fields like
`PetProgressionRates`, never hard-coded at the call site):

```text
maxHP        = baseHP + Vitality * vitToHP
strikeDamage = max(1, Power * powScale - foe.Guard * guardScale)   ± variance
hitChance    = clamp(baseHit + (Agility - foe.Agility) * agiToHit, floor, ceil)
```

All of the pet's effective numbers are then scaled by the **condition effectiveness
multiplier** below.

## Condition ↔ combat: the closed loop

Combat HP is its **own transient pool** — it is *not* the Fullness/Energy needs, it
resets between delves, and a lost fight can never zero-out the pet's real condition. But
the two layers touch at the boundaries, in both directions:

**Condition → combat (on the way in and during):**
- **Effectiveness multiplier.** The same care→XP multiplier that already exists (`needs.xpMultiplier(floor:)`) scales the pet's effective stats and max HP. Well-cared → ~100%; neglected → down to the floor; **critical neglect → the pet refuses to enter** (the doc's "fights below its sheet, or refuses to fight").
- **HP regen.** Recovery between encounters within a delve (and the Brace trickle) scales with condition — a rested, happy Workling bounces back faster mid-delve.

**Combat → condition (on the way out):** the HP you *exit the delve with* moves **all
four conditions**, so a delve is a real event in the pet's day — a triumph lifts it
across the board, an ordeal wears it down across the board:

| Exit tier | Combat HP left | Fullness | Energy | Happiness | Trust |
| --- | --- | :---: | :---: | :---: | :---: |
| **Flawless** | ≥ 90% | ▲ | ▲ | ▲▲ | ▲ |
| **Solid** | 40–90% | ▼ | ▼ | ▲ | ▲ |
| **Barely** | < 40% | ▼▼ | ▼▼ | ▼ | – |
| **Downed** | retreat at 0 | ▼▼ | ▼▼ | ▼▼ | ▼ |

*(▲ gain, ▼ loss, magnitude by count; a proposal to tune.)* Best case is a genuine
across-the-board reward — a reason to delve even when the sheet doesn't strictly need the
XP. Worst case is a real setback the care loop then has to repair. Every magnitude stays
inside the **reversible-neglect envelope** — a disastrous delve leaves the pet drained
and shaken, never broken, and care always restores it. This coupling is the tuning's
sharpest edge and needs real playtesting.

## A worked encounter: the Flicker

*Setting — the **first** dungeon's, not the world's. Worklings is a broad universe, and
dungeons can span many work- and productivity-themed settings; this is one, not the
canon. The first delve, the **Cache Warren**, is the buried strata of the machine the
Workling lives in, rendered as a fantasy underworld whose bestiary **dual-codes** to
work-chaos the way the class names do (Wellspring, Juggernaut): Motes, Snags, Flickers,
and a slow **Monolith** at the bottom. Later dungeons are free to inhabit entirely
different work universes — the combat model below is setting-agnostic.*

The **Flicker** is a jittery, half-there creature — high Agility, low HP, moderate bite.
It is hard to *land* a hit on but folds the moment you do. It's the ideal teaching foe
because every class solves "an evasive target" differently:

```text
Round 1 — Flicker acts first (Agility). It phase-darts in: 6 damage.
          Your Workling Strikes — but the Flicker blurs aside (miss).
Round 2 — Careful Approach: your Workling Braces, reading the pattern (+mitigation, +2 HP).
          Flicker bites into the guard: 2 damage.
Round 3 — Opening! (decision point → Unleash) the Signature lands clean.
          The Flicker snaps back into focus and scatters. Encounter won.
```

Narration + a sprite reaction per line is the whole texture of the fight.

## The same fight, per class

The point of one encounter across five classes: it's where class identity first becomes
*mechanical*, and where the first ability ideas fall out.

- **Maverick (Agility)** — out-initiatives and out-dodges the Flicker; wins the accuracy war outright. *Ability seed: a guaranteed-hit or extra-turn burst.*
- **Tinkerer (Wit)** — its Signature reads the pattern and negates the evasion. *Ability seed: an accuracy debuff / "mark" that makes a slippery foe hittable.*
- **Juggernaut (Power)** — misses often but one shot ends it; a feast-or-famine race. *Ability seed: a big committed swing that can't miss but costs a turn to wind up.*
- **Aegis (Guard)** — can't be out-damaged; Braces through the misses and grinds it down. *Ability seed: a counter that punishes the foe's attack.*
- **Wellspring (Vitality)** — highest HP, out-attrits everything; healing turns a long fight trivial. *Ability seed: a regen/second-wind that makes attrition its win condition.*

Reading these five back-to-back is how we'll shape the **first ability per class** —
each is the Signature move promoted into a real, costed action. That work moves to
`abilities.md`.

## The delve frame

A **solo delve** is the unit of content:

- **Shape** — 3 encounters + 1 mini-boss.
- **Gate** — unlocked at a modest level (the first place level gates real content).
- **Attrition** — combat HP carries *across* encounters within the delve; regen between them scales with condition. Fully restored after the delve.
- **Cadence** — a limited number of attempts per day (a stamina/cooldown), so a delve is a returning daily ritual and pairs naturally with the `dailyWake` login rhythm.
- **Rewards** — XP, plus an **ability-point currency** (deliberately *separate* from stat growth, per [progression](progression.md#levels)); gear drops later as effective-stat modifiers. Reduced or forfeit rewards on a Downed exit.

## New sprite states this needs

Combat needs poses the current [twelve-frame contract](characters.md) doesn't have. All
three families share the contract, so **every new pose is authored for all three sheets**
— which is exactly the multiplier that makes the planned 3D→2D asset pipeline pay for
itself (author/rig once, render every pose for every family).

Proposed additions (the art list):

| Pose | When it shows |
| --- | --- |
| **Strike** | landing an attack |
| **Hurt** | taking a hit (recoil) |
| **Low-HP** | staggered / on the ropes |
| **Victory** | encounter or delve won |
| **Downed** | retreat at 0 HP |
| **Brace** *(opt.)* | defending |
| **Signature** *(opt.)* | unleashing the class move |

Sheet/code contract: the current sheet is a 4×3 grid of 256px cells (1024×768), mapped
by `WorklingSpriteFrame`'s explicit column/row cases. The core five poses extend it to a
**4×4 grid** (a new row 3); the two optional poses would take a further row. Adding a
pose = extend each family's sheet + add the enum case. The `dungeons.md` combat states
reuse this exact mechanism, so no new rendering path is needed.

## Open questions (for iteration)

1. **Combat model** — confirm the active-auto-battler spine before deeper tuning.
2. **Condition↔combat magnitudes** — the exit-tier deltas and effectiveness floor need playtesting; the risk is combat souring the daily care loop if set too harsh.
3. **XP channel** — do dungeon rewards share the existing daily XP caps, or is dungeon XP a separate channel?
4. **Randomness** — confirm seeded-per-encounter PRNG (reproducible, testable) over live RNG.
5. **Encounter breadth** — v1 single-foe; when do multi-foe groups and targeting arrive?
6. **First abilities** — promote each class's Signature into its first costed ability (moves to `abilities.md`).
