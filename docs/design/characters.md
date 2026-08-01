# Characters: Families & Classes

This is the single home for the **identity layer** of Worklings: creature families, the species within them, the class roster, and — once they exist — skills and abilities. When a new species or class is added, it lands here first.

The **mechanics** behind these identities (XP math, stat-growth weights, caps, persistence) live in [Progression](progression.md); this doc records who the characters *are*, that one records how the numbers *move*. The two link to each other rather than repeating each other.

## Families

A family is the species axis — what MMOs would call a race. Family now carries a **light mechanical identity** — a stat lean and a passive (see [Family–class affinity](#familyclass-affinity)) — on top of its art, silhouette, and world flavour.

This is a deliberate **revision** of the original *purely cosmetic* families. That decision was made before combat existed and to protect a "switching family preserves everything" promise. Now that dungeons ship, race has earned the right to *mean* something in a fight. The touch is kept deliberately **soft** — a lean and a passive, never a hard class lock — so switching family still preserves name, needs, favourites, relationship, level, XP, and class; only the small racial lean and passive move with it. (See [Progression](progression.md#families-carry-a-soft-mechanical-identity) for the mechanical side.)

Every species ships the same twelve-frame pose contract, so all moods, reactions, and transitions read identically whichever Workling is active.

The five families form five thematic lanes — nature, elements, machinery, energy, cosmos:

| Family | Lane | World flavor | Current species |
| --- | --- | --- | --- |
| **Wildkin** | Nature | Creatures shaped by living ecosystems and natural magic. | Moss-fox |
| **Elemental** | Elements | Creatures whose elemental affinity is part of their anatomy. | Ember-newt |
| **Relicborn** | Machinery | Creatures bonded to ancient mechanisms, relics, or rune-powered artifacts. | Keyback pangolin |
| **Glitchkin** | Energy | Creatures born in the seams between systems — signal, speed, and unstable reality. | — *(design-stage; no art yet)* |
| **Bloomglass** | Cosmos | Creatures of celestial stillness — starlight, crystal, and refracted moonlight. | — *(design-stage; no art yet)* |

### Species

One species per shipped family exists today; each family is designed to hold many.

- **Moss-fox** (Wildkin) — a fox shaped by living woodland magic, moss and growth woven into its coat.
- **Ember-newt** (Elemental) — a newt whose inner fire is part of its body, not an effect on it.
- **Keyback pangolin** (Relicborn) — a pangolin whose scales are bonded to an ancient rune-powered relic, a key grown into its back.

Adding a species means: pick its family, deliver the twelve-frame pose contract, and add it here. Species selection today is Pixel switching appearance; a full adoption flow remains a later slice.

### Glitchkin & Bloomglass (design-stage)

The two newest families are designed but not yet built — no sprites, no species implemented. They round the roster to five balanced lanes.

- **Glitchkin** (also *Signalborn*) — beings shaped by electricity, waves, speed, and interference; born where signals overlap and portals misalign. Sleek, fast, asymmetric silhouettes broken by glowing pulse-lines; darting, flickering movement with brief afterimages. Where Wildkin feel *alive* and Relicborn feel *constructed*, Glitchkin feel **transmitted**. Direction species: Sparktail (waveform-eared fox), Pinghopper, Echo Lynx, Prism Wisp-Hare.
- **Bloomglass** (also *Astralglass* / *Astralward*) — beings of celestial stillness: starlight, crystal growth, refracted moonlight. Smooth, semi-translucent, luminous-from-within forms; slow, poised, deliberate motion. Read as **big floaty masses** — of soaking presence or of healing energy alike. Direction species: Starpetal Fawn, Halo Tortoise, Moonfin Axolotl, Aurora Hound.

## Classes

Class is the **primary mechanical-identity** axis. Any species can still be any class — the family lean only *tilts* the fit (see [Family–class affinity](#familyclass-affinity)), it never forbids a class. Class decides which of the five stats is the signature stat — the one that grows fastest per level — while the other four keep growing at a slower, steady rate. Growth weights and the leveling math live in [Progression](progression.md#class).

Every class name is deliberately dual-coded: a term with real currency in modern work/maker culture that also carries its own mythic or abstract weight, independent of any RPG convention. The roster maps one class per stat, each filling a traditional RPG role:

| Stat | Class | Role | Flavor |
| --- | --- | --- | --- |
| Vitality | **Wellspring** | Healer / Support | The source others draw on — sustains, restores, never runs dry. |
| Power | **Juggernaut** | Heavy offense | Hits like an unstoppable force — raw, overwhelming offense. |
| Guard | **Aegis** | Tank | The shield everyone stands behind — mitigates, endures, protects. |
| Agility | **Maverick** | Finesse offense | Moves fast, breaks convention — quick, decisive, takes the opening first. |
| Wit | **Tinkerer** | Mage-equivalent | Technology so advanced it might as well be magic — clever, inventive, otherworldly effective. |

Class is freely reassignable for now, the same way family is — nothing yet (no ability trees, no gear) needs protecting across a swap. Once abilities lock to a class, reassignment may become a deliberate, costed action; that is a later revisit recorded in [Progression](progression.md#class).

## Family–class affinity

Family and class are **soft-coupled**: each family fits some classes better than others, but never *only* those. The model is deliberately light so it adds identity without removing freedom or making any class unreachable — a hard race→class lock was considered and rejected (a one-character game shouldn't foreclose builds forever, and every class must stay pickable).

**Only two things are locked per family:**

- a **Primary** class — its natural best fit, where its stat lean and passive line up; and
- a **Weak** class — its anti-fit, where they pull against it.

The **other three classes are open** — fully valid choices the player picks freely. There is no separate bonus table: each family simply has one **fixed stat lean** (equal to its primary class's signature stat) and one **passive**, and that alone makes the primary shine, the weak class sag, and the middle three all viable. "Primary / weak" are *descriptions* of where the fixed lean lands, not extra machinery.

| Family | 🔒 Primary | 🔒 Weak | Open (player's pick) | Fixed lean · passive |
| --- | --- | --- | --- | --- |
| **Wildkin** | Wellspring | Juggernaut | Aegis · Maverick · Tinkerer | +Vitality · regrowth |
| **Elemental** | Tinkerer | Aegis | Juggernaut · Maverick · Wellspring | +Wit · elemental burst |
| **Relicborn** | Juggernaut | Maverick | Aegis · Tinkerer · Wellspring | +Power · machine armor/force |
| **Glitchkin** | Maverick | Juggernaut | Aegis · Tinkerer · Wellspring | +Agility · evasion / phase |
| **Bloomglass** | Aegis | Maverick | Wellspring · Tinkerer · Juggernaut | +Guard · ward / refraction |

Every class is the Primary of exactly one family, so no class lacks a natural home.

**Three flavours of damage** are what make this fit cleanly — "DPS" is not one class:

- **Juggernaut** — *physical* brute (Power). Relicborn's machine pistons.
- **Maverick** — *agile* strike (Agility). Glitchkin's spacetime zaps; a Glitchkin's evasive survivability (miss-based "tanking") lives here, in Agility, not in Aegis's raw absorption.
- **Tinkerer** — *magic* damage (Wit). Elemental's fireblast reads as a spell, which is why the Elemental — clever enough to bend the elements to its will — is the Mage.

**Bloomglass** flexes its Aegis primary two ways — a mass that *soaks* damage or a mass of *healing* energy (Wellspring is its strongest open option).

Weaknesses are first-pass and tunable. The passive column is a placeholder for the real passive design (below).

### Open item, for the abilities round

**Mage synergy is locked to Elemental only.** Glitchkin (spacetime magic) and Wildkin (ancient, long-accumulated Wit) are the two candidates for a *secondary* caster identity — but a "secondary mage" is expressed as a **passive**, so that call is deferred to passive design in [Abilities](abilities.md), not decided here. Until then, magic stays rare and native to one family: many things endure, but true magic is the Elementals' art.

## Skills & abilities

The **shape** is agreed; the rosters are the next design round (see [Abilities](abilities.md)). Two tracks, split by source:

- **Class → active abilities** — the "extra button" you press. Level- and class-gated, unlocked by a points currency deliberately separate from stat growth (see [Progression](progression.md)). The five first abilities are drafted in [Abilities](abilities.md).
- **Family → passive traits** — things that happen *because of what you are*, no button. The placeholders in the affinity table above (regrowth, evasion, ward…) become this track.

Both ride the shared **status-effect** primitive already built for combat, and the **trigger-hook** layer designed in [Abilities](abilities.md). Their full rosters — which class/family gets what, at which level — will live in this document once designed.
