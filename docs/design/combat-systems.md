# Worklings Combat Systems — Stats, Derived Attributes & Resources

## Status

**Vision doc — the destination, not the v1 build.** This is the agreed target model for how the character sheet drives a fight, so every combat slice builds toward one coherent system rather than painting itself into a corner. It sits under [Progression](progression.md) (which owns XP/levels/persistence) and feeds [Dungeons](dungeons.md) (which owns encounter formulas) and [Abilities](abilities.md).

**What v1 actually ships is a curated subset — see [v1 subset vs roadmap](#v1-subset-vs-roadmap) at the bottom.** Everything here is additive and knob-tunable; nothing requires building it all at once.

> Note: the *currently implemented* combat uses an earlier, simpler stat interpretation (e.g. Agility drives hit/crit/initiative directly). This document is the target the engine migrates toward, incrementally and additively.

## The two-layer model

The six **primary stats** are not the combat variables — they *feed* the combat variables (the **derived attributes**). Each primary drives a *bundle* of derived stats, and a derived stat can take input from more than one primary.

This is the mechanism that keeps the game from being either **too simplistic** ("one stat = one number") or a **shopping list** ("a class needs three stats to function"): you stack your class's stat and get a rounded package, while cross-builds emerge from how you *weight* primaries and the classless Luck stat.

```text
primary stats (allocated)  →  derived attributes (what combat reads)  →  the fight
```

## The six primary stats

Five map 1:1 to the five classes (their signature stat — see [Characters](characters.md#familyclass-affinity)); **Luck is classless** — the universal wildcard any build can weight.

| Primary | Class | Drives (derived attributes) |
| --- | --- | --- |
| **Power** | Juggernaut | physical attack damage · physical **crit damage** |
| **Agility** | Maverick | **multi-strike** (extra attacks per turn) · evasion (bonus) · **energy regen** · **armor penetration** |
| **Wit** | Tinkerer | magical damage · **healing power** · magical **crit damage** · **mana regen** |
| **Vitality** | Wellspring | max **HP** · HP regen |
| **Guard** | Aegis | **mitigation / block** (deterministic %) |
| **Luck** | — *(classless)* | **crit chance** (all schools) · evasion (primary) · loot/drop chance · proc & "second-chance" triggers |

Wit is deliberately **"anything magical"** — offensive spells, healing, and ability/status potency all scale from it. That makes a **Wellspring a two-stat class** (Wit for output + Vitality for survival), which is intentional: it's what creates the healer build spectrum (see [balance philosophy](#class-balance-philosophy)).

## Derived attributes (the combat variables)

The full palette, grouped, with its source. Not all ship in v1.

**Offense**
- **Physical damage** ← Power
- **Magical damage** ← Wit
- **Healing** ← Wit
- **Multi-strike** (chance of extra attack(s) in a turn) ← Agility
- **Crit chance** ← Luck
- **Crit damage** ← Power (physical) / Wit (magical)
- **Armor penetration** (ignores a share of the target's mitigation) ← Agility
- *(roadmap: lifesteal, DoT potency)*

**Defense / survival**
- **HP** ← Vitality
- **HP regen** ← Vitality
- **Mitigation / block** — a deterministic **% of damage reduced on every hit** ← Guard
- **Evasion** — an **RNG chance to dodge a hit entirely** ← Luck (primary) + Agility (bonus)
- *(roadmap: damage reflect, shield/absorb, status resistance / tenacity, threat)*

**Resource / utility**
- **Energy** (martial resource) + regen ← baseline + Agility
- **Mana** (magical resource) + regen ← baseline + Wit
- *(roadmap: cooldown reduction)*

### Crit — chance vs damage, split by source

- **Crit *chance* lives on Luck** (all schools). Stack Luck to crit *more often*.
- **Crit *damage* lives on the damage stat** — Power for physical crits, Wit for magical crits. Stack those to crit *harder*.

So a **Juggernaut + Luck** lands rare-but-devastating crits; a **Maverick + Luck** (many hits × high crit chance) becomes a crit machine; a **Tinkerer + Luck** spikes magical crits. High-variance "gambler" builds are a real, opt-in axis for any class.

### Defense — reliable armor vs random dodge

Two mechanically distinct layers, deliberately not overlapping:
- **Mitigation (Guard)** is **deterministic** — every hit is reduced by a %. Reliable, always-on.
- **Evasion (Luck/Agility)** is **RNG** — some hits are dodged entirely, most are not.
- **Armor penetration (Agility)** counters mitigation — the answer to tanky foes and, later, PvP.
- **Accuracy is folded into evasion**: an attack lands unless the defender evades it — no separate hit/accuracy stat.

### Time in a turn-based auto-battler

Combat is turn-based auto-resolving, so "speed" can't be real-time. **Attack speed = a chance at extra strike(s) within the same turn** (multi-strike, from Agility). That's how a fast, light attacker (Maverick) out-*volumes* a slow, heavy one.

> Tuning flag: multi-strike is powerful — it multiplies damage *and* crit rolls *and* armor-pen application. Each extra strike should be a *chance* and/or a *fraction* of a full hit, or Agility runs away with the game.

## Resources — Energy & Mana

Abilities are gated by a **resource economy**, not an arbitrary use count — so how often you can act is a *build* decision.

- **Energy** — the **martial** resource (Juggernaut / Maverick / Aegis abilities). Small pool, fast per-turn regen, frequent tactical use.
- **Mana** — the **magical** resource (Tinkerer / Wellspring abilities). Larger pool, slower regen, scarce and burst-y.
- **Regen = baseline + stat amplifier**: everyone regenerates a baseline each turn; **Agility** amplifies Energy regen and **Wit** amplifies Mana regen (and **Vitality** amplifies HP regen). This makes **Maverick** the resource-*fluid* martial (spammy small abilities) while **Juggernaut/Aegis** are resource-*thrifty* (fewer, bigger ones), without starving them.

Resources may combine with per-ability **cooldowns** (a big ability costs resource *and* has a cooldown). See [Abilities](abilities.md).

## Luck — the classless wildcard

Luck owns the game's RNG and is the surface most items/perks hook into:
- **crit chance** (all schools), **evasion** (primary source), **loot / drop chance**, and **proc / "second-chance" triggers**.
- It is **never a class signature stat** — it's the shared "gambler" axis any build can weight, at the cost of its main stat.
- It's the design space for high-variance builds (a squishy Luck-stacker that *just doesn't get hit* and is saved by a proc when it does) and for item hooks (e.g. a boss-drop that prevents dropping below 1 HP for a turn — Luck scales how often such a proc fires).

## Threat & loot (parked, but sourced)

Defined now so systems are built with room for them, but not scheduled:
- **Threat / aggro** — for group tanking. Source: Guard + taunt abilities + items. Meaningless until group content exists.
- **Loot / drop rate** — Source: Luck + items + perks.

## Class balance philosophy

The design pillar: **every class reaches a comparable outcome by a different route.** Assuming equal HP and otherwise-equal conditions:

| Class | Route | Feel |
| --- | --- | --- |
| **Maverick** | low base damage, **fast** (multi-strike) | many small hits; must keep attacking |
| **Juggernaut** | **high** base damage, slow | few huge hits; same kill-time as Maverick by a different path |
| **Tinkerer** | high magical damage, medium speed | spell burst; kill-time like Juggernaut |
| **Aegis** | medium damage, **outlasts** via mitigation | slower kills, but doesn't die doing it |
| **Wellspring** | medium damage, **outlasts** via self-healing | slower kills, sustained through attrition |

Two families of win condition — **race the clock** (Maverick / Juggernaut / Tinkerer, comparable time-to-kill via speed vs power vs magic) and **outlast** (Aegis / Wellspring, win by not losing). The differentiator between classes is *how* you win and *how much you can take*, not raw effectiveness — which is exactly what makes group composition matter later.

## Stat allocation

**Locked: manual point allocation.** A level grants two separate pools — **Stat Points** (allocated into the six stats here) and **Skill Points** (spent in the class skill tree; see [Abilities](abilities.md)). Manual Stat Points are what make the niche builds real (a glass-cannon healer that stacks Wit and gets two-shot; a Juggernaut that gambles into Luck). This **reverses the currently shipped model** (automatic class-weighted growth, no banked points — see [Progression → Levels](progression.md#levels)); how manual allocation coexists with any class-weighted default is a v1 implementation detail. The racial lean (a signed `+`/`−`, see [Characters](characters.md#familyclass-affinity)) folds in on top as a read-time modifier.

## v1 subset vs roadmap

Build order is incremental and additive. A sensible **v1 subset** (coherent, minimal): **HP, physical damage, magical damage, healing, deterministic mitigation, RNG evasion, crit chance + crit damage, multi-strike.**

**Deferred to later slices:** the **Energy/Mana** economy, the **Luck** stat & procs/second-chance, **armor penetration**, **threat**, **loot tables**, **manual allocation**, lifesteal/DoT/reflect/shield/tenacity/cooldown-reduction. Each drops onto this same primary→derived model when its slice comes up.

## Knobs (held)

All magnitudes are alpha tuning, to live in the `PetCombatRates` neighborhood (`Sources/CompanionCore/PetCombat.swift`): every primary→derived coefficient, multi-strike chance/fraction, mitigation %, evasion %, crit chance/damage curves, armor-pen %, resource pool sizes and regen (baseline + amplifier), and the Luck coefficients.

## Open questions

1. **Manual allocation shape** — pure free-allocation, or class-weighted defaults the player can nudge? (Tracked in [Progression](progression.md).)
2. **Ability scaling detail** — each ability scales off its class stat + Wit as a universal secondary, or strictly its class stat? (Tracked in [Abilities](abilities.md).)
3. **Two damage schools, one defense** — confirmed single mitigation for now; a physical/magical resistance split is explicitly *out* until scale demands a rewrite.
