# Worklings Gameplay Loop

The umbrella that ties the design docs together: how a Workling goes from a cared-for
companion to a character that grows and fights, and where each system fits. The
detailed mechanics live in the docs this one links to — read this first for the shape,
then follow the links for the numbers.

This doc is a **frame**, not a spec: it records how the pieces relate and the order
they're built in. Where a system already exists, its own doc is the source of truth;
where it doesn't yet, this doc marks it planned.

## The one-directional coupling

Worklings runs two layers that never merge (see [Progression](progression.md#the-two-layer-model)):

| Layer | What it is | Horizon |
| --- | --- | --- |
| **Condition** | Fullness, Energy, Happiness, Trust — the tamagotchi layer. | Short. Rises and falls daily. |
| **Progression** | XP, level, stats, class — the character sheet. | Long. Only ever accumulates. |

They couple in exactly one direction: **condition gates progression.** A well-cared-for
Workling earns at full rate; a neglected one learns slowly and — once content exists —
fights below its sheet or refuses to fight. Neglect is always reversible, so it can
throttle growth but never destroy it. That single rule is what keeps the care loop
mechanically load-bearing instead of decorative.

## The loop, today

What a player actually does, and what it feeds:

1. **Care** — Feed, Play, Pet, Sleep keep condition up. ([Pet Brain](pet-brain.md), [Interaction](interaction.md))
2. **Work** — real activity (daily wake, focus blocks, commits, agent runs) arrives as content-free events and grants XP, gated by the condition multiplier. ([Progression](progression.md#activity-events))
3. **Grow** — XP raises level; level grows class-weighted stats. ([Progression](progression.md#levels), [Characters](characters.md#classes))

Today the loop ends at step 3: the character sheet grows but does not yet *do* anything.
Everything below is what closes that gap.

## The loop, where it's going

The [systems ladder](progression.md#the-systems-ladder) lists the build order. In loop
terms:

- **Content (dungeons/PVE)** — *designed, not yet built.* The first place level, stats, class, and condition are spent rather than just accumulated: level-gated solo delves of turn-based, narrated encounters resolved against the sheet. This is the loop's missing "spend" step and the next major slice. Design in [Dungeons](dungeons.md).
- **Abilities** — *planned.* Level- and class-gated actions used inside encounters, unlocked by a points currency deliberately separate from stat growth. Design lands in `abilities.md`.
- **Gear** — *planned.* Modifies *effective* stats at read-time without touching the persisted base numbers, so it arrives as computation rather than a save migration.
- **Endgame, then PVP** — *deferred.* A level cap followed by lateral progression; PVP waits behind multiplayer normalization.

## Why this order

A first encounter that resolves against **stats +
class + condition alone** makes the existing progression finally matter, and it creates
the concrete demand — "this fight needs an answer" — that then shapes what the first
abilities and gear should even be. The encounter is designed so those layers slot in
later without rework.

## Where each system is documented

- Condition and care rules → [Pet Brain](pet-brain.md)
- Reading and caring for the pet → [Interaction](interaction.md)
- XP, levels, stats, class, the full ladder → [Progression](progression.md)
- Families, species, class roster → [Characters](characters.md)
- Dungeons / encounters → [Dungeons](dungeons.md)
- Abilities → `abilities.md` *(planned)*
