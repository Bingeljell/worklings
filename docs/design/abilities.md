# Worklings Abilities

## Status

**v1 model LOCKED (design); not yet implemented.** Abilities are systems-ladder step 4,
the layer above the [dungeon combat model](dungeons.md) and the [stat
system](combat-systems.md). Two tracks — **class actives** and **family passives** — both
ride the **status-effect** primitive (built) and the **trigger-hook** layer (designed).
Every magnitude is a held knob; this doc is the model and the starter rosters, not final
numbers.

## Progression currencies

Leveling grants **two separate point pools** (both distinct from XP itself):

- **Stat Points** — the player allocates these into the six stats manually (see [combat
  systems](combat-systems.md)). This is the niche-build lever (the glass-cannon healer,
  the Luck gambler). It replaces the previously-shipped automatic class-weighted growth
  (a reversal tracked in [progression](progression.md#levels)).
- **Skill Points** — spent in the **class skill tree** to unlock and rank abilities (and,
  later, chosen passives). Earned per level, with a bonus on delve completion. *(This
  merges the earlier delve-only "ability-point currency" into one pool — one tree
  currency, not two.)*

Persisted additively: stat allocations, the skill-point balance, and the set of
unlocked/ranked ability ids. Level and XP are never rewritten.

## The class skill tree

- **~4–5 abilities per class** at cap — a **shallow linear ladder with 1–2 either/or
  choice nodes**, enough for build identity without an MMO sprawl.
- Some abilities **auto-unlock at level gates**; others are **activated and ranked with
  Skill Points**.
- **Scaling is layered:**
  1. **Automatic** — every ability scales with your stats as you level and allocate, for
     free (no points spent).
  2. **Ranks** — Skill Points add *power*: bigger magnitude, lower cost, or shorter cooldown.
  3. **Capstone** — a final rank adds a **rider** (a new effect), so a heavily-invested
     ability *transforms in function*, not just inflates. This is the "fixed roster that
     scales in power **and** function."

## How an ability works in combat

- **Class- and level-gated** — you only ever see your own class's abilities, past their gate.
- **Costs Energy or Mana + a cooldown** — big abilities both, small ones resource-only — so
  they fire **several times a fight** (not a once-per-encounter charge). Energy is the
  martial resource (Juggernaut/Maverick/Aegis), Mana the magical one (Tinkerer/Wellspring);
  see [combat systems](combat-systems.md#resources--energy--mana).
- **The class's first ability replaces the generic Signature** — unlocking it upgrades the
  one "Unleash" slot into the class-specific move. One special action, no UI bloat.
- **The pet auto-uses its abilities** in combat (chosen by resource, cooldown, and
  situation); the player steers with **broad guidance at decision points**, not per-turn.
- **Deterministic** — resolves through the same seeded PRNG as the rest of combat, so it
  stays testable in `CompanionCore`.

### The status-effect system these need — *(built)*

Several abilities (and [enemy abilities](dungeons.md#the-cache-warren)) apply **timed
buffs/debuffs** rather than instant damage. **This shipped with the enemy abilities**
(`StatusEffect` on `Combatant.statuses`, folded into `effectiveStats`, ticked per round,
with a permanent option). Current kinds: `agilityDebuff`, `guardBuff`, `evasion`,
`phasing`. The vocabulary grows *as needed* — the actives and passives below will add
`regen`, `shield`/absorb, `powerBuff`/`powerDebuff`, `guardDebuff`, `damageReflect`,
`critUp`, and possibly `stun` — each added the moment something needs it, never speculatively.

### The trigger-hook layer these need — *(designed, not built)*

Status effects cover *what* an effect does. Many passives and richer items fire **when
something happens** (on-hit, on-hurt, at round-start). These need a small **trigger-hook**
layer: a lightweight in-combat event dispatch that passives, items, class abilities, and
enemy behaviours subscribe to, applying status effects (or damage) through the primitive
above.

- **Hook points** (first cut): `onRoundStart`, `onHit` (dealt), `onHurt` (taken),
  `onAbilityUse`, `onLowHP`, `onKill` — mapping onto the `CombatEvent` stream the encounter
  already emits, so the layer is a subscriber, not a rewrite.
- **Deterministic** — hooks resolve through the seeded PRNG in a fixed order.
- **Shared** — the second build-once primitive (after status effects); it's what makes
  *triggered* family passives and *on-effect* items possible. Static passives (a permanent
  modifier) need only the status primitive and can ship first.

**Build order:** rosters locked (this doc) → build the trigger-hook layer → build the
passives and items that ride it.

## The five class actives (v1)

Each is the class's Signature promoted into a costed, scaling action. Effects are
qualitative; every number is a held knob.

| Class | Signature stat | Ability | What it does | Cost |
| --- | --- | --- | --- | --- |
| **Wellspring** | Vitality | **Second Wind** | Restores a chunk of HP now, then a smaller regen-over-time for a few rounds — turns attrition into the win condition. | Mana |
| **Juggernaut** | Power | **Overbear** | A committed heavy strike that **cannot miss** at a large multiplier — but it telegraphs (winds up, or drops the pet's own Guard that round). Reliable burst with a cost. | Energy |
| **Aegis** | Guard | **Bulwark** | Braces hard this round (mitigation well above a normal Brace) **and** reflects a portion of the blocked damage back. | Energy |
| **Maverick** | Agility | **Flurry** | A burst of **extra strikes** this round (leaning the multi-strike identity), or a guaranteed crit — best when it can end a low-HP foe. | Energy |
| **Tinkerer** | Wit | **Exploit** | A Wit-scaled **magic bolt for direct damage** that also **marks** the foe (−evasion, −Guard) for a few rounds. A real spell that also opens up slippery/armored targets. | Mana |

Read against the [Flicker walkthrough](dungeons.md#a-worked-encounter-the-flicker):
**Exploit** hits *and* strips its evasion, **Flurry** out-volumes it, **Overbear**
one-shots it, **Bulwark** turns its bites back on it, **Second Wind** simply outlasts it.
One encounter, five genuinely different solutions.

## Family passives (v1)

One **automatic passive per family** (no button) — the racial identity in combat. The
roster lives in [Characters → Family passives](characters.md#family-passives): Wildkin
**Regrowth**, Elemental **Overload**, Relicborn **Relic Plating**, Glitchkin **Phase
Flicker**, Bloomglass **Refraction Ward**. They let a race cover a gap so you can go all-in
on your class (a Relicborn Juggernaut solo-levels safely on Relic Plating; a Wildkin Aegis
tanks *and* self-heals on Regrowth).

## Deferred (later, not v1)

- **Passive *lines*** (multiple family passives, chosen with Skill Points).
- **Secondary-mage synergy** — magic is native to the Elemental *race* in v1 (its passive,
  Overload, is the magic one); any race can still *be* a Tinkerer/Wellspring. Later, a
  Wit-flavoured passive could be granted to Glitchkin/Wildkin. Kept out of v1 for clarity.
- **Race actives**, and per-ability upgrade trees deeper than the capstone rider.

## Knobs

Held, to tune once playable: skill-points per level and per delve; unlock/rank costs; each
ability's resource cost and cooldown; the capstone-rider thresholds; and every effect
magnitude. These live in the `PetCombatRates` neighborhood alongside the dungeon knobs.
