# Worklings Items & Gear

## Status

Design **finalized** (2026-08-03); execution deferred. Items are the
[systems-ladder](progression.md#the-systems-ladder) step 5 (gear), specced here as a small
**base set** to give delves something to drop and the [Character Screen](#where-items-live)
something to equip. The model and the starter items are locked; **magnitudes remain knobs** —
the numbers are a balance pass for once combat is playable.

The founding rule comes straight from [progression](progression.md#the-systems-ladder):
gear modifies **effective** stats at **read-time** and never touches the persisted base
numbers. So items arrive as pure computation — a save stores *which* items are owned and
equipped, and the effective-stat function folds their modifiers in on read. No stat
migration, ever.

## The effective-stat model

```text
baseStats        (persisted; from level + class growth)
  → + family lean + equipped item modifiers   = sheet stats   (what the Stats tab shows)
  → × condition effectiveness                  = combat stats  (what the dungeon formulas read)
```

The **family lean** (see [Characters → Family–class affinity](characters.md#familyclass-affinity)) folds in at read-time exactly like gear does — a small nudge to effective stats, never a rewrite of the persisted base — so switching family needs no migration. **Item family-attunement riders** (below) fold in at this same read-time step: a matching family reads a slightly larger modifier, still pure computation, still no persisted change.

Combat already reads *effective* stats (see [dungeon formulas](dungeons.md#core-formulas));
gear simply enters that computation. The [care→combat multiplier](dungeons.md#condition--combat-the-closed-loop)
still applies on top, so gear raises the ceiling but neglect still scales it down.

## Slots

v1 ships **three slots** — enough to make a real choice, not enough to become a spreadsheet.
They are **functional, not paper-doll**: Worklings are creatures, so gear is never
helmet/chestplate armor — it's the stuff a working companion carries. Each slot has its own
small fantasy so equipping *feels* like a choice, not a stat-stick swap:

| Slot | Leans | The fantasy — *"the thing that…"* |
| --- | --- | --- |
| **Tool** | offense | …it works *with*. The implement you bring to the problem. |
| **Ward** | defense / survival | …keeps it safe. What you hide behind on a bad day. |
| **Charm** | utility / signature / Luck | …is just *yours*. Personality, and the home of the classless Luck axis. |

One item per slot; swapping is free (like class/family today) until there's a reason to cost
it. **The slot set can grow** as the game builds out — but toward more *creature/work-themed
functional* slots (e.g. a companion trinket, a sigil, a consumable loadout), **never** a
literal human armor paper-doll. Additional slots are deferred backlog, not v1.

## Base item set

One item favoring each of the **six** primary stats, dual-coded to work-artifacts the way the
[dungeon bestiary](dungeons.md#the-cache-warren) and class names are. Each has a **universal
base nudge** every build can use, plus a soft **family attunement** (next section). Modifiers
are held as knobs — the point is the shape (a small mono-stat nudge), not the size.

| Item | Slot | Base (universal) | Attunes to | Flavor |
| --- | --- | --- | --- | --- |
| **Cracked Whetstone** | Tool | + Power | **Relicborn** *(Juggernaut)* | A worn edge still bites. |
| **Rubber Duck** | Charm | + Wit | **Elemental** *(Tinkerer)* | The oldest debugging tool there is; it listens. |
| **Dented Buckler** | Ward | + Guard | **Bloomglass** *(Aegis)* | It has taken worse hits than you have. |
| **Quickstep Charm** | Charm | + Agility | **Glitchkin** *(Maverick)* | Always half a step ahead. |
| **Warm Backup-Coal** | Ward | + Vitality | **Wildkin** *(Wellspring)* | A little reserve, banked against a bad day. |
| **Lucky Green-Build Coin** | Charm | + Luck | **— none —** | It passed on the first try. Nobody knows why. |

These stay intentionally **mono-stat, primaries only** — they teach the equip loop and make
the Stats tab matter without a rarity or affix system, and they touch **primary** stats
(not derived attributes), keeping the [two-layer stat model](combat-systems.md) clean. Richer
items (multi-stat, on-hit / proc effects, set bonuses, derived-stat gear) are a later layer
that slots into the same read-time model. *(Note: the Luck item and the six-stat spread assume
Luck exists; combat v1 builds a stat subset that defers Luck — so the Luck coin lands whenever
the Luck stat does, not before.)*

## Family attunement — the soft synergy layer

Items are **universal**: any family, any class, any build can equip anything — matching the
[soft-affinity philosophy](characters.md#familyclass-affinity) that every build stays
reachable and nothing is ever hard-locked. On top of that, each stat-item carries a small
**attunement rider** to the **one family whose primary class leans on that stat**:

- The mapping is exact because the family→class→stat matrix is 1:1 — Power/**Relicborn**,
  Wit/**Elemental**, Guard/**Bloomglass**, Agility/**Glitchkin**, Vitality/**Wildkin**.
- A matching family reads a **slightly larger** modifier at the effective-stat step; everyone
  else gets the universal base. The rider is **soft** (small magnitude, a nudge not a gate) —
  it rewards thematic pairing without punishing off-theme builds.
- **The Luck coin has no attunement by design.** Luck is the classless sixth axis with no
  family home, so its gear stands apart — the stat system telling one consistent story through
  items.
- v1 riders are **read-time stat bumps only** (no new system). Richer family riders — passives
  or procs hooking the [trigger-hook layer](abilities.md) — are a deferred later layer.

## Where items live

Equipping lives in the **[Character Screen](../design/)** — the floating hub window opened by
clicking the Workling — in its gear slots + inventory, alongside the Stats and Skills tabs.
*(This resolves the earlier "where equipping lives" open question; see the character-screen
design decision.)* The 3D model bay shows the equipped Workling; the slot UI is the paper-doll
frame these three functional slots fill.

## Where items come from

- **Delve drops** — the primary source; a delve can award an item alongside XP and skill
  points (see [rewards](dungeons.md#rewards)). Which foes/delves drop what is content, not yet
  assigned.
- **A starter item** — a new Workling **begins with one modest item** so the gear UI is never
  empty on first look. Default is the **Rubber Duck** (iconic, warm first impression); the
  specific starter is a knob.
- Knobs: drop rate / drop table per delve; which item is the starter.

## Persistence

Additive to the save: an **owned-items** list and the **equipped item per slot**. The
effective-stat computation reads these; base stats are never rewritten. A save without the
fields reads as "no items," so the schema bump is backward-compatible — the same additive
posture the XP/class/stat fields already used.

## Knobs

Held: each base item's base modifier size; the attunement rider size; drop rates and drop
tables; which item is the starter; any future rarity multipliers or extra slots. Collected
alongside the dungeon knobs.

## Locked decisions (2026-08-03)

1. **Slots** — three functional slots (Tool / Ward / Charm), each with its own fantasy;
   creature-appropriate, never a human armor paper-doll. Set may grow later toward more
   themed functional slots.
2. **Item effects** — mono-stat, **primaries only**, one per each of the six stats (Luck coin
   added). No derived-stat or proc gear in v1.
3. **Restrictions** — none; universal equip, with a **soft family-attunement rider** as the
   synergy layer.
4. **Starter item** — yes, one modest item (default Rubber Duck).
5. **Where equipping lives** — the Character Screen (resolved).

## Open (balance-pass) knob

- **Item power vs stat growth** — how large a base modifier (and its attunement rider) is
  "meaningful but not dominant" relative to a level-up's stat gain. Principle is locked:
  **gear is a nudge, not the dominant axis** — builds and levels still lead. The exact number
  waits until combat is playable.
