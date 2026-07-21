# Worklings Progression Design

## Status

This is the agreed design direction for activity awareness, experience, levels, and stats. Activity awareness (events, context, sources) and XP/levels/class/stats are implemented; everything past that — abilities, gear, dungeons, endgame, PVP — is not. It exists so that implementation slices, and eventually external contributors, build toward one coherent game rather than a collection of features.

The care loop described in [Pet Brain](pet_brain.md) is implemented and remains the foundation this design builds on.

## The two-layer model

Worklings separates the **living pet** from the **character sheet**.

| Layer | Owns | Nature |
| --- | --- | --- |
| Condition | Hunger, Energy, Happiness, Trust | Short-horizon wellbeing. Rises and falls daily with care and activity. The tamagotchi layer, deliberately. |
| Progression | XP, level, stats | Long-horizon growth. Only ever accumulates. The MMO layer. |

Condition is not the stat block. Its job is presence and attachment: a creature that gets hungry, curls up, and misses you. The progression layer is the character sheet: how the Workling grows from companion to contender, and later, how it fights in PVE and PVP.

The layers couple in one direction: **condition gates progression.** A well-cared-for Workling earns XP at full rate. A neglected one learns slowly, and later fights below its sheet or refuses to fight. This keeps the care loop mechanically load-bearing without letting neglect destroy accumulated progress, which preserves the existing reversible-neglect principle.

## The character, not just the pet

"Pixel" is one companion's name, not the product's shape. Worklings the game is an RPG/MMO where the real-world-reactive companion **is** the player's character — call it a pet, a toon, an avatar, it's the same slot in the system. That framing is what justifies everything below: levels that gate real content, stats that mean something in a fight, and eventually a class identity, not just a bigger tamagotchi with a stat block bolted on.

It also means "Workling" doesn't have to stay a fixed creature roster forever. A distant, unscoped idea: syncing hobbyist-built real-world bots as alternate avatars. Nothing here depends on that — it's just why the architecture should keep assuming "a character" rather than hard-coding "a small cared-for pet" wherever it can.

## The systems ladder

The long-run shape, roughly in build order, so every slice below is built with room to grow into this rather than needing rework later:

1. **Level/XP** — the gate. Everything downstream reads "what level is this character" to decide what's available.
2. **Stats** (Vitality, Power, Guard, Agility, Wit) — ambient effects from day one; the character's base numbers.
3. **Class** — a mechanical-identity axis, separate from family (family is cosmetic species; class is how stats grow and, later, what abilities are available). Built alongside Level/Stats, not deferred — see [Class](#class) below.
4. **Abilities** — level- and class-gated actions, unlocked by a future points currency that is deliberately *not* the same currency as stat growth (see [Levels](#levels)).
5. **Gear** — modifies *effective* stats at read-time without ever touching the persisted base numbers, so it can arrive later as pure computation rather than a save migration.
6. **Dungeons/PVE** — level-gated text encounters resolved against stats/abilities/condition, narrated with the mood-and-reaction sprite states that already exist. This is the first place level actually does something rather than just existing, and the biggest canvas item.
7. **Endgame** — a level cap, then lateral progression (guild-wars/FF-style). The level table must not assume a hard ceiling it can't extend past.
8. **PVP** — deferred behind multiplayer normalization; see [Fairness](#fairness).

## Activity events

All real-world stimulus enters through the provider-neutral boundary already defined in [Architecture](architecture.md):

```text
Activity source -> normalized event -> activity context -> Pet Brain intent -> presentation
```

A normalized event carries an event kind, a timestamp, and a source identifier. It never carries prompts, source code, diffs, commit messages, file paths, window contents, or keystrokes.

### Event vocabulary

| Event | Meaning |
| --- | --- |
| `dailyWake` | The app was opened or first used on a new calendar day |
| `workStarted` / `workEnded` | A sustained work or focus block began or ended |
| `taskCompleted` / `taskFailed` | An agent run, build, or comparable unit of work finished |
| `awaitingInput` | A connected agent is blocked on the human |
| `milestone` | A commit was made, a PR was opened, or a PR was merged |
| `userIdle` / `userReturned` | Presence changed, based on system input idle time |

### Planned sources

Ordered roughly by implementation cost:

1. **Daily wake.** The app itself is a source: the first launch or interaction of each calendar day emits `dailyWake`. This is the login-reward hook and requires no permissions or integrations.
2. **Simulated source.** A debug-only control that emits arbitrary events, used to tune pet reactions and XP rules before any real adapter exists, and to keep behavioral checks deterministic.
3. **Presence.** System input idle time (no content, no per-app visibility) drives `userIdle` and `userReturned`, and bounds work blocks.
4. **Local git.** FSEvents watching of explicitly connected repositories emits `milestone` on commit. Opt-in per repository.
5. **GitHub connect.** See below.
6. **Agent adapters.** Codex first, per the architecture doc: session lifecycle events from locally written session files map to `workStarted`, `awaitingInput`, `taskCompleted`, and `taskFailed`.

### GitHub connect

An explicit, opt-in integration that reads the user's own recent GitHub activity — commit counts, PRs opened, PRs merged — and converts it into `milestone` events.

- Authentication uses the OAuth device flow with read-only scopes; the token is stored in the user's Keychain.
- Worklings stores only event kinds, counts, and timestamps derived from the API response. Repository names, commit messages, and diffs are discarded at the adapter boundary, consistent with the content-free event contract.
- The integration is off by default, clearly disconnectable, and its absence never harms the pet.

GitHub activity has a property no local source has: it is a public, timestamped, third-party record. Merged PRs in particular are expensive to fake at scale. This makes GitHub-sourced XP the most verifiable progression input and the natural anchor for later multiplayer normalization.

## Experience

XP is earned from normalized events and from care quality, so progression is provider-neutral by construction.

| Source | Notes |
| --- | --- |
| `dailyWake` | The login reward. Modest, reliable, streak-friendly. |
| Completed work blocks (`workEnded`) | The workhorse source: sustained real activity, gated by a minimum qualifying duration so starting and immediately stopping earns nothing. |
| Care actions | A trickle, so tending the pet always means something. |
| `workLogged` | A small amount alongside its fixed Happiness gain, reusing the same cooldown and cap. |
| `taskCompleted` | Agent and build completions. Formula defined now; dormant until a real adapter fires it (only the debug simulated source can today). |
| `milestone` | Commits small, merged PRs largest. Formula defined now; dormant for the same reason. |

**Condition multiplier.** XP accrual scales with current wellbeing — the average of Fullness, Energy, Happiness, and Trust, floored so neglect slows accrual without ever fully halting it. This is the primary coupling between the two layers.

**Caps and diminishing returns.** Every source has its own daily cap, and an overall daily cap holds across all sources combined — the actual fairness mechanism (see below). Both reset lazily by comparing a stored date to "now," the same pattern Log Work already established, so there is no day-rollover code path to get wrong. Exact values are alpha tuning; see the Tuning reference below.

**Curve.** Level is derived from cumulative XP via a quadratic formula, not a stored value or a hand-authored table — level and XP can never disagree with each other, and the formula has no upper bound, so raising a level cap later never requires migrating anything.

## Levels

Each level grants **automatic stat growth, weighted by class.** There is no banked, manually-spent stat currency — a level applies immediately and permanently to the sheet, so stats mean something from the first level-up without needing an allocation UI that doesn't exist yet.

This is a deliberate change from treating stat points as a spendable currency: abilities and skill trees, when they exist, will unlock against level thresholds and consume their **own** future points currency, not stat growth. Overloading one currency for both "the sheet grows" and "you pick an ability" would have forced a premature choice between them. A data-driven per-level table (XP required, stat growth granted) means tuning either system never touches the other.

## Stats

The character sheet is battle-facing, sized for eventual PVE and PVP:

| Stat | Battle meaning | Ambient meaning before battles exist |
| --- | --- | --- |
| **Vitality** | Hit points | Slower hunger and energy decay |
| **Power** | Offense | Bolder reactions and celebrations |
| **Guard** | Defense | Steadier under neglect penalties |
| **Agility** | Speed, turn order | Faster, fancier roaming |
| **Wit** | Skill and special effectiveness | Small XP bonus, stronger puzzle-play results |

Every stat has an ambient effect from day one so growth is meaningful long before combat ships.

**Base vs. effective stats.** Only the base numbers above — what leveling has granted — are ever persisted. Gear, when it exists, modifies an *effective* stat computed at read-time (`effective = base + class weighting already baked in + equipped gear`), so it can be added later as pure computation rather than a save migration. The save only ever needs to know what the character has permanently earned.

**Trust and Bond.** Trust stays a condition need. If a long-horizon relationship stat proves necessary, sustained high Trust can graduate into a separate **Bond** stat; that decision is deferred.

**Families stay cosmetic; class carries mechanical identity.** Stat affinities per family (Wildkin, Elemental, Relicborn) would conflict with the shipped promise that switching family preserves all progress, so family stays a purely cosmetic species choice. Class — see below — is the new, separate axis that gives stats a reason to diverge, without touching family at all.

## Class

Class decides how the five stats grow: each class has one signature stat that grows fastest per level, with the remaining four still growing at a slower, steady rate so no stat is ever permanently frozen. This is what makes stat growth mean something before abilities or gear exist — a `Warden` and a `Striker` visibly diverge on the same sheet from level one.

Class is freely reassignable for now, the same way family is — there is nothing yet (no ability trees, no gear) that a class swap would need to protect. Once abilities lock to a class, reassignment may need to become a deliberate, costed action; that is a later revisit, not a constraint today.

Every class name is deliberately dual-coded: a term with real currency in modern work/maker culture today, that also carries its own mythic or abstract weight independent of any RPG convention. The roster maps one class per stat, each filling a traditional RPG role:

| Stat | Class | Role | Flavor |
| --- | --- | --- | --- |
| Vitality | **Wellspring** | Healer / Support | The source others draw on — sustains, restores, never runs dry. |
| Power | **Juggernaut** | Heavy offense | Hits like an unstoppable force — raw, overwhelming offense. |
| Guard | **Aegis** | Tank | The shield everyone stands behind — mitigates, endures, protects. |
| Agility | **Maverick** | Finesse offense | Moves fast, breaks convention — quick, decisive, takes the opening first. |
| Wit | **Tinkerer** | Mage-equivalent | Technology so advanced it might as well be magic — clever, inventive, otherworldly effective. |

## Fairness

The save is a local JSON file and Worklings is open source; anyone determined can edit their pet. Fairness is therefore designed as **caps, not cryptography**:

**PVE first, PVP later.** A player who edits their save to max level affects only their own single-player experience — nobody else's dungeon run is touched, and doing so mostly just undercuts their own progression. This is why fairness doesn't need to be airtight yet: it matters for PVE mainly so a diligent legitimate player never feels outpaced by a save-edited one in spirit, not because cheating there harms anyone else. PVP is the case that actually requires rigor, and it is explicitly deferred behind multiplayer normalization below — nothing in the PVE-era design needs to anticipate PVP-grade fairness yet.

- **Daily XP caps and per-source diminishing returns.** A perfect cheat can only compress time; it cannot produce a pet a diligent legitimate player could never have. The ceiling is the fairness mechanism.
- **Wall-clock gating.** XP accrual is bounded by real elapsed time, reusing the same discipline as the existing capped offline progression.
- **Shallow power curves.** Levels are primarily prestige and identity, so an edited save is unimpressive rather than oppressive.
- **No local save obfuscation.** It contradicts the open-source posture and has never worked anywhere.
- **Multiplayer normalization.** When multiplayer arrives, levels and stats are normalized or bracketed server-side, and claimed progression can be sanity-checked against plausible event rates — with GitHub-verifiable milestones as the strongest anchor.

Exact cap values and rates are deliberately deferred; realism-derived inputs make them straightforward to add when tuning begins.

## Persistence

Progression fields (XP, class, stats, daily accrual bookkeeping) extend the existing versioned save additively, following the pattern established by the family field: older saves load unchanged with defaults, and no migration destroys care state. Level is never itself stored — it is always derived from XP, so the two can never desync.

## Tuning reference

Same posture as [Pet Brain's tuning reference](pet_brain.md#tuning-reference): every number below is alpha tuning, living in named `PetProgressionRates` fields (`Sources/CompanionCore/PetProgression.swift`), easy to retune without touching the mechanism.

| Knob | Default | Field |
| --- | --- | --- |
| `dailyWake` XP | 20 | `dailyWakeXP` |
| Focus Session XP per minute / minimum qualifying duration / daily cap | 2 / 10 min / 200 | `focusSessionXPPerMinute` / `focusSessionMinimumMinutes` / `focusSessionDailyCap` |
| Care action XP / daily cap | 3 / 60 | `careActionXP` / `careActionDailyCap` |
| `taskCompleted` XP / daily cap | 15 / 150 | `taskCompletedXP` / `taskCompletedDailyCap` |
| `milestone` XP / daily cap | 40 / 200 | `milestoneXP` / `milestoneDailyCap` |
| `workLogged` XP / daily cap | 5 / 30 | `workLoggedXP` / `workLoggedDailyCap` |
| Overall daily XP cap (across every source combined) | 500 | `overallDailyCap` |
| Signature stat gain per level / every other stat | 3 / 1 | `signatureStatGainPerLevel` / `otherStatGainPerLevel` |
| Condition multiplier floor | 0.2 | `conditionMultiplierFloor` |
| Level curve | `50 × (level − 1) × level` cumulative XP | `PetProgressionCurve.totalXPRequired(forLevel:)` |
| Starting stat value | 5 | `PetStats.startingValue` |

## Implementation order

1. Event vocabulary, activity context, and the simulated source in `CompanionCore`, with behavioral checks. **Done.**
2. `dailyWake`, presence, Log Work, Focus Session, and pet renaming — the cheapest real stimuli and companion-identity basics. **Done.**
3. XP, levels, class, and class-weighted stat growth on the event stream, as an additive save-schema revision. **Done.**
4. GitHub connect and the first agent adapter, once reactions feel right on real input.
5. Dungeons/PVE: level-gated text encounters against the stat sheet, reusing existing mood/reaction sprite states.
6. Abilities and their own points currency, gear as an effective-stats computation layer, and multiplayer-normalized PVP, far later, on top of the sheet this document defines.
