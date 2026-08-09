# Worklings Items & Gear

## Status

Design **finalized** (2026-08-03); **first slice built** (2026-08-04) — see
[As built](#as-built) for what shipped and what the code deliberately left out. Items are the
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

### Tiers — depth is the reward curve

Each stat-item above exists at **three tiers**, because a delve that paid out once, at the
very bottom, asked four fights of a player and answered with a single item that might not
even suit their build. Every encounter now yields something, and **how deep you had to go
decides what**:

| Tier | Dropped by | Worth (knob) | The five |
| --- | --- | ---: | --- |
| **Scavenged** | the early encounters | +1 | Chipped File, Bent Pot Lid, Cold Coffee Dregs, Sticky Note, Frayed Lanyard |
| **Solid** | the last regular encounter | +2 | the original base set above |
| **Prime** | **the mini-boss only** | +4 | Master's Hone, Failsafe Plate, Everburning Backup, Root-Cause Lens, Hotpath Sigil |

The three tiers of a stat always share a **slot**, so a tier is a like-for-like upgrade: a
better Tool competes with your current Tool. The gradient *is* the reward for pushing — and
the reason the bank prompt is a real question, since Prime gear exists nowhere else.

**No cross-tier fallback.** When a tier is exhausted its encounters drop nothing rather than
substituting another tier: a boss handing out Scavenged junk reads as a bug, and an early
fight paying Prime because the Scavenged set is complete would gut the reason to push. Running
dry is the honest signal that this dungeon has given what it has; a real drop table (per-foe
rates, more items, generated affixes) is the later answer.

These stay intentionally **mono-stat, primaries only** — they teach the equip loop and make
the Stats tab matter without an affix system, and they touch **primary** stats
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

Equipping lives in the **[Character Screen](interaction.md#3-character-screen)** — the floating
hub window opened by clicking the Workling — in its gear slots + inventory, alongside the
Character, Skills, and Care tabs. *(This resolves the earlier "where equipping lives" open
question.)* The model bay shows the equipped Workling; the slot rail is the paper-doll frame
these three functional slots fill, and it stays visible beside the inventory so a click's
result is legible in the same glance.

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

## As built

`Sources/CompanionCore/Items.swift` (plus `ownedItems` / `loadout` on `PetState`)
implements the model above. Existing raw values were preserved when the catalogue grew to
three tiers, so the ten new items are purely additive — an old save reads back unchanged.
Three deviations, each forced by something that doesn't exist yet rather than by a change
of mind:

- **Five stats, not six — so fifteen items, not eighteen.** The Lucky Green-Build Coin
  needs a Luck stat, and combat v1 defers Luck. The coin (and its tiers) land when
  `PetStatKind` grows a `luck` case.
- **Two stat-lines are universal-only.** Attunement needs its family to exist;
  **Bloomglass** and **Glitchkin** are design-stage, so every Guard and Agility item reads
  the universal base for everyone until those families ship. Attunement is a soft nudge,
  so nothing is mis-modelled in the meantime — just unrewarded.
- **The family lean isn't folded in.** It belongs at the same read-time step and lands
  inside `PetStats.effective(...)` when it's built, without moving the seam.

Two invariants are enforced on construction rather than trusted, so a hand-edited or
future-written save is self-correcting: an item only ever sits in **its own slot**, and a
loadout can only reference an item that is **actually owned**.

Gear folds in **before** the condition multiplier (base → sheet → combat, as the ladder
above shows), so a neglected Workling's equipment is scaled down with everything else —
you can't gear your way out of care.

**Where the loadout is chosen:** both the **[Character Screen](interaction.md#3-character-screen)**
and the delve **briefing**, deliberately.

The Character Screen is gear's home — the persistent rail of three slots beside the model
bay, an Inventory tab holding everything owned, and a stat table that shows the gear
column next to the base one. The briefing keeps its own compact prep bar because that is
where the narration *motivates* the pick; sending a player to another window mid-descent
would break the beat the briefing exists to create.

Two surfaces pricing the same items is a drift risk, so they don't each do it: a single
`GearPricing` owns the vocabulary (`+3 Power ✦`, the loadout total line, the attunement
tooltip) on top of the one `ItemRates` that owns the arithmetic. The screens differ in
chrome and in nothing else.

**Drops, as built:** **every cleared encounter** awards one item of its depth's tier —
deterministic in the delve seed, never something already owned, never twice in a run,
and nil rather than a substitute once that tier is exhausted.

Gear won on the way down is **kept on a bank and on a retreat alike**: those encounters
were genuinely cleared, and clawing the spoils back would make the shallow fights
worthless again, which is the exact problem per-encounter drops exist to solve. What
banking forfeits is the **depth** — the completion bonus and the boss's Prime item. That
keeps press-your-luck teeth without charging four fights for one payout.

**The drop beat.** A reward is shown where it was won. Each encounter's item appears
**inside the bank/push prompt** — the compact card, priced and equippable on the spot —
so the decision to push is made with the last payout visible. The boss's Prime item gets
the **end screen's staged reveal**: it lands a beat after the victory fanfare thins out,
and **Return waits for it** — a drop dismissed before it was seen is the whole gamble
going unwitnessed. Everything picked up on the way down is tallied there as a receipt
rather than revealed twice.

Both cards carry the name, tier, slot, price (`+3 Power ✦`), and an **Equip** button, so a
prize doesn't require a trip to another window to do anything. Tier has one colour
everywhere it's named — grey, cyan, gold.

It also states **what equipping would cost**, via `Loadout.swap(to:family:rates:)`
(`GearSwap`). Because items are mono-stat *and* slot-bound, a swap usually moves two
different stats in opposite directions — gaining Agility while losing the Wit the Rubber
Duck was providing — so a bare "+2 Agility" would be a half-truth whenever the slot is
occupied. An empty slot is reported as exactly that: pure gain.

**Starter on an existing save.** A save written before gear reads as the *starter*
loadout rather than as an empty inventory — what a pet created today would get — so a
pre-gear Workling isn't left with a gear UI it could never fill. Still zero migration:
nothing persisted is rewritten.

## Where this is heading — class item sets *(direction, not yet designed)*

The catalogue is expected to grow **a lot**, and the intended shape is **a set of items per
class** rather than more one-off stat sticks. The current 5×3 grid is the scaffolding for
that: it exists to make the equip loop, the tier gradient, and the drop beat real while the
content is small.

**The fork to resolve before authoring class sets:** today's soft synergy attunes an item to
a **family**, and that only works because the family→class→stat matrix is 1:1 — "suits a
Relicborn" and "suits a Juggernaut" are currently the same sentence. Author gear *per class*
and they stop being the same sentence: a Relicborn Maverick would carry Glitchkin-flavoured
class gear while attuning to Relicborn items. So class sets either **replace** family
attunement or **stack** with it as a second, separate rider. That is a deliberate decision,
not something to let drift — and it is the reason the rider lives behind
`ItemRates.modifier(for:family:)` rather than being inlined at each call site.

Also queued for that pass: per-foe / per-delve **drop rates** (today every cleared encounter
drops exactly one item of a fixed tier), and what a delve does once its tiers are exhausted.

## Open (balance-pass) knob

- **Item power vs stat growth** — how large a base modifier (and its attunement rider) is
  "meaningful but not dominant" relative to a level-up's stat gain. Principle is locked:
  **gear is a nudge, not the dominant axis** — builds and levels still lead. The exact number
  waits until combat is playable.

  **First-pass values (2026-08-04):** base modifier **+2**, attunement rider **+1**,
  anchored to level-up growth (signature stat +3/level, others +1). So an unattuned item
  is worth less than one level of signature growth and an attuned one is worth exactly
  one; a full three-slot loadout lands near a level or two of progress spread across
  three stats. Visible on the sheet, never eclipsing it. Held in `ItemRates` — retune
  from real play without touching the mechanism.
