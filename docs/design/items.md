# Worklings Items & Gear

## Status

Design direction, not implemented. Items are the [systems-ladder](progression.md#the-systems-ladder)
step 5 (gear), specced here as a small **base set** to give delves something to drop and
the character sheet something to equip. **Magnitudes are held as knobs** — this is the
model and the starter items, not their final numbers.

The founding rule comes straight from [progression](progression.md#the-systems-ladder):
gear modifies **effective** stats at **read-time** and never touches the persisted base
numbers. So items arrive as pure computation — a save stores *which* items are owned and
equipped, and the effective-stat function folds their modifiers in on read. No stat
migration, ever.

## The effective-stat model

```text
baseStats        (persisted; from level + class growth)
  → + equipped item modifiers        = sheet stats   (what the Stats tab shows)
  → × condition effectiveness         = combat stats  (what the dungeon formulas read)
```

Combat already reads *effective* stats (see [dungeon formulas](dungeons.md#core-formulas));
gear simply enters that computation. The [care→combat multiplier](dungeons.md#condition--combat-the-closed-loop)
still applies on top, so gear raises the ceiling but neglect still scales it down.

## Slots

v1 keeps the slot set minimal — enough to make a choice, not enough to become a spreadsheet:

| Slot | Leans | Example |
| --- | --- | --- |
| **Tool** | offense | the thing it *does* work with |
| **Ward** | defense / survival | the thing that keeps it safe |
| **Charm** | utility / a signature stat | a small personal token |

Knob/decision: the slot count itself (start with these three, or fewer). One item per slot;
swapping is free (like class/family today) until there's a reason to cost it.

## Base item set

A starter handful, one favoring each stat, dual-coded to work-artifacts the way the
[dungeon bestiary](dungeons.md#the-cache-warren) and class names are. Modifiers are held
as knobs — the point is the shape (a small mono-stat nudge each), not the size.

| Item | Slot | Effect | Flavor |
| --- | --- | --- | --- |
| **Rubber Duck** | Charm | + Wit | The oldest debugging tool there is; it listens. |
| **Cracked Whetstone** | Tool | + Power | A worn edge still bites. |
| **Dented Buckler** | Ward | + Guard | It has taken worse hits than you have. |
| **Quickstep Charm** | Charm | + Agility | Always half a step ahead. |
| **Warm Backup-Coal** | Ward | + Vitality | A little reserve, kept banked against a bad day. |

These are intentionally simple mono-stat items: they teach the equip loop and make the
Stats tab matter without a rarity or affix system. Richer items (multi-stat, on-hit
effects, set bonuses) are a later layer that slots into the same read-time model.

## Where items come from

- **Delve drops** — the primary source; a delve can award an item alongside XP and ability points (see [rewards](dungeons.md#rewards)). Which foes/delves drop what is content, not yet assigned.
- **A starter item** — optionally, a new Workling begins with one modest item so the slot UI is never empty on first look. *(Decision, held.)*
- Knobs: drop rate / drop table per delve; whether a starter item exists.

## Persistence

Additive to the save: an **owned-items** list and the **equipped item per slot**. The
effective-stat computation reads these; base stats are never rewritten. A save without the
fields reads as "no items," so the schema bump is backward-compatible — the same additive
posture the XP/class/stat fields already used.

## Knobs

Held: slot count; each base item's modifier size; drop rates and drop tables; whether a
starter item exists; any future rarity multipliers. Collected alongside the dungeon knobs.

## Open questions

1. **Slot count** — three slots (Tool/Ward/Charm) or start with one or two?
2. **Starter item** — ship one by default, or leave slots empty until the first drop?
3. **Item power vs stat growth** — how large a modifier is "meaningful but not dominant" relative to a level-up's stat gain? (A balance question for once combat is playable.)
4. **Where equipping lives** — a new tab/section on the care card's Stats tab, or its own surface?
