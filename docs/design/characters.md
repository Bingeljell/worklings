# Characters: Families & Classes

This is the single home for the **identity layer** of Worklings: creature families, the species within them, the class roster, and — once they exist — skills and abilities. When a new species or class is added, it lands here first.

The **mechanics** behind these identities (XP math, stat-growth weights, caps, persistence) live in [Progression](progression.md); this doc records who the characters *are*, that one records how the numbers *move*. The two link to each other rather than repeating each other.

## Families

A family is the species axis — what MMOs would call a race. It is deliberately **cosmetic**: art, silhouette, and world flavor, with no stat affinities. That keeps the shipped promise that switching family preserves the same name, needs, favourites, relationship, and progression. Mechanical identity belongs to [class](#classes) instead.

Every species ships the same twelve-frame pose contract, so all moods, reactions, and transitions read identically whichever Workling is active.

| Family | World flavor | Current species |
| --- | --- | --- |
| **Wildkin** | Creatures shaped by living ecosystems and natural magic. | Moss-fox |
| **Elemental** | Creatures whose elemental affinity is part of their anatomy. | Ember-newt |
| **Relicborn** | Creatures bonded to ancient mechanisms, relics, or rune-powered artifacts. | Keyback pangolin |

### Species

One species per family exists today; each family is designed to hold many.

- **Moss-fox** (Wildkin) — a fox shaped by living woodland magic, moss and growth woven into its coat.
- **Ember-newt** (Elemental) — a newt whose inner fire is part of its body, not an effect on it.
- **Keyback pangolin** (Relicborn) — a pangolin whose scales are bonded to an ancient rune-powered relic, a key grown into its back.

Adding a species means: pick its family, deliver the twelve-frame pose contract, and add it here. Species selection today is Pixel switching appearance; a full adoption flow remains a later slice.

## Classes

Class is the **mechanical-identity** axis, fully separate from family: any species can be any class. Class decides which of the five stats is the signature stat — the one that grows fastest per level — while the other four keep growing at a slower, steady rate. Growth weights and the leveling math live in [Progression](progression.md#class).

Every class name is deliberately dual-coded: a term with real currency in modern work/maker culture that also carries its own mythic or abstract weight, independent of any RPG convention. The roster maps one class per stat, each filling a traditional RPG role:

| Stat | Class | Role | Flavor |
| --- | --- | --- | --- |
| Vitality | **Wellspring** | Healer / Support | The source others draw on — sustains, restores, never runs dry. |
| Power | **Juggernaut** | Heavy offense | Hits like an unstoppable force — raw, overwhelming offense. |
| Guard | **Aegis** | Tank | The shield everyone stands behind — mitigates, endures, protects. |
| Agility | **Maverick** | Finesse offense | Moves fast, breaks convention — quick, decisive, takes the opening first. |
| Wit | **Tinkerer** | Mage-equivalent | Technology so advanced it might as well be magic — clever, inventive, otherworldly effective. |

Class is freely reassignable for now, the same way family is — nothing yet (no ability trees, no gear) needs protecting across a swap. Once abilities lock to a class, reassignment may become a deliberate, costed action; that is a later revisit recorded in [Progression](progression.md#class).

## Skills & abilities

Not designed or implemented yet. When they arrive, abilities will be level- and class-gated and unlocked by a points currency that is deliberately separate from stat growth (see [Progression](progression.md)). Their rosters — which class gets what, at which level — will live in this document.
