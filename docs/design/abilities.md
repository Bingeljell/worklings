# Worklings Abilities

## Status

Design direction, not implemented. Abilities are the layer directly above the
[dungeon combat model](dungeons.md): they turn the generic v1 **Signature** action into a
real, class-specific move, and they're the first thing the dungeon's reward currency buys.
This doc specs *what each class's first ability does*; **magnitudes are deliberately held
as knobs** — the point is the mechanics, tuned once it's playable.

Abilities sit on the [systems ladder](progression.md#the-systems-ladder) at step 4, gated
by level and class, unlocked by a **points currency deliberately separate from stat
growth** so the two economies never share a schema.

## The ability-point currency

- **Earned** from delves (a delve completion grants ability points — see [dungeon rewards](dungeons.md#rewards)). Never from care or work XP, so abilities are a *content* reward, not a passive drip.
- **Spent** to unlock a class's abilities. v1: one first ability per class; later, a small per-class tree in unlock order.
- **Persisted** additively on the save: a points balance + the set of unlocked ability ids. Base stats and level are untouched — this is a parallel currency, exactly as [progression](progression.md#levels) requires.
- Knobs: points granted per delve; unlock cost per ability. *(Held.)*

## How an ability works in combat

An unlocked ability becomes a choosable action at a [decision point](dungeons.md#the-players-input-strategy-at-decision-points), replacing that class's generic Signature:

- **Class- and level-gated** — you only ever see your class's abilities, past their level gate.
- **Costed in combat** — each has a use limit: a once-per-encounter charge, or a cooldown of *n* rounds. Knob: the limit per ability.
- **Scales** with the class's signature stat (the same stat the class already grows fastest), so investing in the class deepens the ability without a second number to balance.
- **Deterministic** — resolves through the same seeded PRNG as the rest of combat, so it stays testable in `CompanionCore`.

### The status-effect system these need — *(built)*

Several abilities (and [enemy abilities](dungeons.md#the-cache-warren)) apply **timed
buffs/debuffs** rather than instant damage — Tinkerer's mark, Aegis's riposte window,
Maverick's extra action. So abilities require a small **status-effect** primitive on the
combat model: a named effect with a magnitude and a remaining-duration, applied to a
combatant, ticked each round, and read by the formulas.

**This shipped with the enemy abilities** (`StatusEffect` on `Combatant.statuses`, folded
into `effectiveStats`, ticked per round, with a permanent option). Current kinds:
`agilityDebuff`, `guardBuff`, `evasion`, `phasing`. The vocabulary grows *as needed* — the
class abilities and family passives below will add `regen`, `shield`/absorb,
`powerBuff`/`powerDebuff`, `guardDebuff` (Expose), `damageReflect` (Bulwark / Refraction),
`critUp`, and possibly `stun` — each added the moment something needs it, never speculatively.

### The trigger-hook layer these need — *(designed, not built)*

Status effects cover *what* an effect does. Many passives and richer items fire **when
something happens** — Glitchkin's Signal Surge on ability-use, Phase Flicker on being hit,
Bloomglass's Starlit Mend at round-start, an on-crit item that Snares. These need a small
**trigger-hook** layer: a lightweight in-combat event dispatch that passives, items, class
abilities, and enemy behaviours can subscribe to, which then apply status effects (or
damage) through the primitive above.

- **Hook points** (first cut): `onRoundStart`, `onHit` (dealt), `onHurt` (taken),
  `onAbilityUse`, `onLowHP`, `onKill`. These map cleanly onto the `CombatEvent` stream the
  encounter already emits, so the layer is a subscriber over existing events, not a rewrite.
- **Deterministic** — hooks resolve through the same seeded PRNG and fixed order as the
  rest of combat, so triggered effects stay replayable and checkable in `CompanionCore`.
- **Shared** — this is the second build-once primitive (after status effects). It's the
  thing that makes *triggered* family passives and *on-effect* items possible at all; the
  static passives (a permanent buff, e.g. Bloomglass Refraction Shell) need only the status
  primitive and could ship first.

Build order is settled: **lock the ability/passive rosters, then build the trigger-hook
layer, then the passives and items that ride it.**

### Active (class) vs passive (family)

Abilities split by source, matching [Characters](characters.md#skills--abilities):

- **Class → active abilities** — the "extra button": the five costed moves below.
- **Family → passive traits** — automatic, no button: the affinity-table placeholders
  (regrowth, evasion, ward…), built on the two primitives above.

The two tracks are independent, and a level-up can advance either.

## First ability per class

Each is the class's [Signature seed](dungeons.md#the-same-fight-per-class) promoted into a
costed action. Effects are qualitative here; every number is a knob.

| Class | Signature stat | First ability | What it does |
| --- | --- | --- | --- |
| **Wellspring** | Vitality | **Second Wind** | Restores a portion of max HP now, then a smaller regen-over-time for a few rounds. Turns attrition into the win condition. *(Knobs: heal %, regen amount/duration, use limit.)* |
| **Juggernaut** | Power | **Overbear** | A committed heavy strike that **cannot miss**, at a large damage multiplier — but it telegraphs (winds up a round, or drops the pet's own Guard for the round). Reliable burst with a cost. *(Knobs: damage multiplier, wind-up/Guard penalty, use limit.)* |
| **Aegis** | Guard | **Bulwark** | Braces hard this round (mitigation well above a normal Brace) **and** reflects a portion of the blocked damage back — punishing a foe that attacks into it. *(Knobs: mitigation, reflect %, use limit.)* |
| **Maverick** | Agility | **Flurry** | Takes an **extra action** this round (or a guaranteed-crit burst), exploiting speed. Best when it can end a low-HP foe outright. *(Knobs: extra hits / crit behavior, use limit.)* |
| **Tinkerer** | Wit | **Expose** | Marks the foe: a Wit-scaled debuff that **lowers its evasion and Guard** for a few rounds, making a slippery or armored target hittable for the whole party of one. The answer to the Flicker. *(Knobs: evasion/Guard reduction, duration, use limit.)* |

Read against the [Flicker walkthrough](dungeons.md#a-worked-encounter-the-flicker): Expose
neutralizes the evasion, Flurry outraces it, Overbear one-shots it, Bulwark turns its
bites back on it, Second Wind simply outlasts it. One encounter, five genuinely different
solutions — which is the whole point of building abilities against a fight that already
exists.

## Upgrade path (later)

v1 ships one ability per class. The unlock order and a small **per-class tree** (a second
and third ability, each level- and points-gated) come after the first ability loop feels
right. The currency and the unlocked-id set are designed so a tree is additive — no
reshape of the save or the combat model.

## Knobs

Held, to tune once playable: points-per-delve, unlock cost per ability, each ability's
use limit (charge vs cooldown length), and each ability's effect magnitude(s) as listed
in the table. These would live in the same `PetCombatRates` neighborhood as the dungeon
knobs.

## Open questions

1. **Signature vs first ability** — does unlocking a class's first ability *replace* the generic v1 Signature, or sit alongside it as a second choosable action?
2. **Cost model** — once-per-encounter charges (simpler) vs round cooldowns (more tactical)? Possibly per-ability.
3. **Ability scaling** — always the class's signature stat, or does Wit universally drive ability potency (making Tinkerer the "caster"), with the signature stat as a secondary?
4. **Tree shape** — how many abilities per class at the cap, and are they linear unlocks or branching?
5. **Family passive roster** — one passive per family, or a small passive line unlocked by level like the class actives? The affinity table's placeholders (regrowth, elemental burst, machine armor, evasion/phase, ward/refraction) become this roster.
6. **Secondary mage synergy** — Mage is currently native to Elemental only ([Characters](characters.md#familyclass-affinity)). Glitchkin (spacetime magic) and Wildkin (ancient Wit) are candidates for a Wit-flavoured *passive* that makes a secondary caster build synergise. Decide here, since it's expressed as a passive — keep magic rare, or grant one/both the synergy.
