# Worklings Pet Brain

## Status

The first persistent care loop is implemented. Pixel is currently the single test Workling and uses placeholder runtime visuals; the Wildkin, Elemental, and Relicborn artwork in the repository is concept direction rather than selectable in-app content.

The Pet Brain is deterministic, independent of Codex, and usable without any activity integration. Autonomous movement, adoption, deeper personality, reversible runaway behavior, and activity-driven intent remain planned work.

## Responsibilities

`CompanionCore` owns the rules for needs, preferences, time progression, actions, moods, urgency, and presentation intent. It does not own windows, menus, timers, filesystem locations, or final artwork.

`PetSession` is the live application boundary. It advances the simulation once per minute, performs care actions, publishes reactions to the UI, and persists state after changes.

## State model

The version 1 save contains:

- schema version;
- Workling name;
- hunger, energy, happiness, and trust;
- favourite food and favourite play activity;
- the timestamp of the last progression calculation.

Every need is clamped to `0...100`. Internally, higher hunger is worse while higher energy, happiness, and trust are better.

The interface derives **Fullness** as `100 - hunger`. Fullness is not persisted and does not change the save schema. Fullness, Energy, Happiness, and Trust therefore share one visible rule: a higher number and longer bar mean better wellbeing.

Pixel currently starts with:

| State | Initial value |
| --- | ---: |
| Hunger | 15 |
| Fullness shown to the user | 85 |
| Energy | 80 |
| Happiness | 70 |
| Trust | 50 |
| Favourite food | Berries |
| Favourite play activity | Puzzle |

## Time progression

Progression accepts explicit timestamps so tests do not depend on wall-clock timing.

| Need | Baseline change per hour |
| --- | ---: |
| Hunger | +4 |
| Energy | -3 |
| Happiness | -1 |
| Trust | No baseline decay |

Severe hunger and exhaustion add happiness and trust penalties. Offline progression is capped at seven days, but `lastUpdatedAt` advances to the actual current time so the same absence is not applied repeatedly.

These values are alpha tuning, not settled game balance. In particular, the current hunger rate can make Pixel feel persistently hungry after a long absence and should be evaluated with tester feedback.

## Care actions

All results are clamped to the valid need range.

| Action | Effect |
| --- | --- |
| Favourite food | Hunger -30, Happiness +8, Trust +3 |
| Other food | Hunger -20, Happiness +3, Trust +1 |
| Favourite play | Hunger +8, Energy -14, Happiness +22, Trust +6 |
| Other play | Hunger +7, Energy -12, Happiness +14, Trust +3 |
| Pet | Happiness +8, Trust +4 |
| Sleep | Hunger +6, Energy +35, Happiness +2 |

Feed is unavailable at zero hunger, Play is unavailable below 15 energy, Sleep is unavailable at 100 energy, and Pet remains available. Favourite choices return stronger semantic reactions; exhausted play returns a refusal without changing needs.

## Mood and urgency

Mood is derived in priority order:

1. Hungry at hunger `>= 75`.
2. Sleepy at energy `<= 20`.
3. Wary at trust `<= 20`.
4. Sad at happiness `<= 30`.
5. Happy when happiness is `>= 75`, trust is `>= 60`, and hunger is `<= 40`.
6. Content otherwise.

`PetCareStatus` separately ranks notice, urgent, and critical conditions for hover summaries, ambient feedback, and action availability. This keeps shared care rules out of SwiftUI and AppKit.

## Presentation contract

`PetPresentation` maps semantic mood and the latest reaction to the current placeholder face, palette, label, and short thought. Care reactions temporarily take precedence for approximately three seconds; normal need presentation then resumes.

The live UI currently provides:

- natural-language hover summaries with at most two conditions;
- a click-opened care card with exact positive wellbeing meters;
- Feed, Play, Pet, and Sleep actions;
- favourite food and activity markers;
- matching menu-bar state and actions;
- accessible labels and Reduce Motion support.

## Persistence and compatibility

State is stored as versioned JSON at:

```text
~/Library/Application Support/Worklings/pet-state.json
```

On first launch after the rebrand, Worklings copies an existing legacy save from `Application Support/BuildCompanion` when the Worklings save does not yet exist. The legacy file is preserved.

Writes use atomic replacement. An unreadable save is preserved, persistence is disabled for that session, and the app falls back to a fresh in-memory pet rather than overwriting unknown data.

## Verification

The dependency-free check executable currently covers:

- clamping and derived Fullness boundaries;
- new-pet defaults and mood priority;
- deterministic time progression, backward clocks, and offline caps;
- care tradeoffs, preference bonuses, and refusals;
- JSON round trips, schema rejection, corrupt-save preservation, and decoded clamping;
- confirmation that derived Fullness is not persisted;
- urgency, summaries, action availability, presentation, and screen placement.

Run it with:

```bash
swift run CompanionCoreChecks
```

## Next Pet Brain work

- Tune need rates from real usage rather than intuition.
- Add personality traits beyond two favourites.
- Define reversible neglect and runaway recovery.
- Separate short-lived activity context from long-lived relationship state.
- Add intent outputs for idle, roaming, attention-seeking, sleep, and activity reactions.
- Support adoption and creature-family selection without coupling rules to particular artwork.
