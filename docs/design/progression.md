# Worklings Progression Design

## Status

This is the agreed design direction for activity awareness, experience, levels, and stats. Activity awareness (events, context, sources) and XP/levels/class/stats are implemented; everything past that — abilities, gear, dungeons, endgame, PVP — is not. It exists so that implementation slices, and eventually external contributors, build toward one coherent game rather than a collection of features.

The care loop described in [Pet Brain](pet-brain.md) is implemented and remains the foundation this design builds on.

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

All real-world stimulus enters through the provider-neutral boundary already defined in [Architecture](../engineering/architecture.md):

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
4. **Local git.** *(Implemented.)* An **in-app source, not an external adapter** — git has no lifecycle hooks to ride, so the running app is the watcher. It watches the `.git` directory of repositories you connect from the paw menu (**Connected Repos → Connect a Repo…**) and emits `milestone` per new commit. Opt-in per repository; the connected list is visible and each entry is one click to disconnect. Detection is by HEAD **commit-SHA movement** with an ancestor check (`GitCommitDelta`), so a message, diff, or path is never read; an amend, reset, or rebase that rewrites rather than advances history emits nothing, and commits made while the app was closed are not retro-credited (the baseline is synced silently on connect and launch). Successive commits the same day earn geometrically less XP — see `milestoneDecayFactor` in the Tuning reference.
5. **GitHub connect.** See below.
6. **Agent adapters.** Claude Code and Codex ship in `scripts/adapters/` (see [Activity adapters](../engineering/adapters.md)). Both map a lifecycle through their tool's hooks (event JSON drained and discarded on stdin): Claude Code's `workStarted`/`taskCompleted`/`awaitingInput`/`workEnded`, and Codex's `[hooks]` `SessionStart`/`Stop`/`SessionEnd` → `workStarted`/`taskCompleted`/`workEnded` (Codex has no documented "awaiting input" event yet).

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
| `taskCompleted` | Agent and build completions. Emitted by the Claude Code and Codex agent adapters (and the debug simulated source). |
| `milestone` | Commits small, merged PRs largest. Emitted by the local-git source (one per commit, with within-day diminishing returns); the debug simulated source also fires it. |

**Condition multiplier.** XP accrual scales with current wellbeing — the average of Fullness, Energy, Happiness, and Trust, floored so neglect slows accrual without ever fully halting it. This is the primary coupling between the two layers. The Character Screen surfaces it as one plain line under the XP bar ("Learning at N% …", `PetPresentation.learningRateLabel`) so the multiplier is legible rather than reverse-engineered from shrunken grants. **Later:** a proper wellbeing score with its own visual treatment (not a bar) is deferred — the single line is the alpha stand-in.

**Caps and diminishing returns.** Every source has its own daily cap, and an overall daily cap holds across all sources combined — the actual fairness mechanism (see below). On top of the caps, a source may also apply **per-event geometric decay** within a day: the Nth grant is worth `base × factor^(N-1)`, applied before the condition multiplier and the cap. Only `milestone` decays today (so a batch of commits tapers instead of piling linearly toward the cap); every other source is flat, tracked by a per-source daily count (`PetState.dailyEventCount`) alongside the XP ledger. All of this bookkeeping resets lazily by comparing a stored date to "now," the same pattern Log Work established, so there is no day-rollover code path to get wrong. Exact values are alpha tuning; see the Tuning reference below.

**Curve.** Level is derived from cumulative XP via a quadratic formula, not a stored value or a hand-authored table — level and XP can never disagree with each other, and the formula has no upper bound, so raising a level cap later never requires migrating anything.

## Levels

Each level grants **automatic stat growth, weighted by class.** There is no banked, manually-spent stat currency — a level applies immediately and permanently to the sheet, so stats mean something from the first level-up without needing an allocation UI that doesn't exist yet.

This is a deliberate change from treating stat points as a spendable currency: abilities and skill trees, when they exist, will unlock against level thresholds and consume their **own** future points currency, not stat growth. Overloading one currency for both "the sheet grows" and "you pick an ability" would have forced a premature choice between them. A data-driven per-level table (XP required, stat growth granted) means tuning either system never touches the other.

## Stats

The character sheet is battle-facing, sized for eventual PVE and PVP. The table below is the *ambient/day-one* meaning; the full **primary → derived-attribute** combat model (plus the classless sixth stat, Luck, resources, and the crit/defense model) lives in [Combat systems](combat-systems.md).

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

### Families carry a soft mechanical identity

Originally families were purely cosmetic — no stat affinities — to protect a "switching family preserves *all* progress" promise, made before combat existed. With dungeons shipped, that decision was **revised**: family now carries a **soft** mechanical identity so race means something in a fight, without the downsides of a hard lock.

Concretely, each family has one **signed stat lean** (a small `+` on its primary class's signature stat and a `−` on its weak class's stat) and one **passive**. That single signed lean gives every family a natural best-fit class (its **Primary**, boosted), a real-but-mild anti-fit (its **Weak**, penalized), and three neutral open classes — the full model and the five-family matrix live in [Characters → Family–class affinity](characters.md#familyclass-affinity). Class remains the *primary* mechanical axis; the family lean only tilts it, never locks it.

**What this costs the switch promise:** switching family now shifts the small racial lean and passive — so it is no longer *fully* progress-preserving. Everything the character earned still carries over untouched — level, XP, class, and the base stat growth from leveling — only the racial lean/passive change. The reversible-neglect and no-migration guarantees are unaffected; the lean is a read-time modifier on effective stats (like gear), never a rewrite of the persisted base sheet.

## Class

Class decides how the five stats grow: each class has one signature stat that grows fastest per level, with the remaining four still growing at a slower, steady rate so no stat is ever permanently frozen. This is what makes stat growth mean something before abilities or gear exist — a Juggernaut and a Maverick visibly diverge on the same sheet from level one.

Class is freely reassignable for now, the same way family is — there is nothing yet (no ability trees, no gear) that a class swap would need to protect. Once abilities lock to a class, reassignment may need to become a deliberate, costed action; that is a later revisit, not a constraint today.

The five-class roster — one class per stat, each filling a traditional RPG role, every name dual-coded between modern work/maker culture and its own mythic weight — lives in [Characters](characters.md#classes), the identity layer's single home, alongside families and species. In short: Vitality → **Wellspring**, Power → **Juggernaut**, Guard → **Aegis**, Agility → **Maverick**, Wit → **Tinkerer**.

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

Same posture as [Pet Brain's tuning reference](pet-brain.md#tuning-reference): every number below is alpha tuning, living in named `PetProgressionRates` fields (`Sources/CompanionCore/PetProgression.swift`), easy to retune without touching the mechanism.

| Knob | Default | Field |
| --- | --- | --- |
| `dailyWake` XP | 20 | `dailyWakeXP` |
| Focus Session XP per minute / minimum qualifying duration / daily cap | 2 / 10 min / 200 | `focusSessionXPPerMinute` / `focusSessionMinimumMinutes` / `focusSessionDailyCap` |
| Care action XP / daily cap | 3 / 60 | `careActionXP` / `careActionDailyCap` |
| `taskCompleted` XP / daily cap | 15 / 150 | `taskCompletedXP` / `taskCompletedDailyCap` |
| `milestone` XP / daily cap / per-commit decay | 40 / 200 / 0.7 | `milestoneXP` / `milestoneDailyCap` / `milestoneDecayFactor` |
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
4. The first real activity sources on top of the event stream: the Claude Code and Codex agent adapters (see [adapters](../engineering/adapters.md)) and the in-app local-git source. **Done.** GitHub connect remains, once reactions feel right on real input.
5. Dungeons/PVE: level-gated text encounters against the stat sheet, reusing existing mood/reaction sprite states.
6. Abilities and their own points currency, gear as an effective-stats computation layer, and multiplayer-normalized PVP, far later, on top of the sheet this document defines.
