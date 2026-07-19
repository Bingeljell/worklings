# Pet Brain

The Pet Brain is the Workling's life simulation — the **condition layer** from the [progression design](progression.md). It runs entirely locally, works without any integration, and is deterministic: the same state and the same clock always produce the same pet.

Pixel is the current test Workling and can appear as the Wildkin moss-fox, Elemental ember-newt, or Relicborn keyback pangolin. Changing family never resets care progress.

## Who owns what

`CompanionCore` owns the rules: needs, moods, care outcomes, and time progression. `PetSession` is the live boundary — it ticks the simulation once a minute, performs care actions, switches families, and saves after changes. Windows, menus, and artwork live elsewhere.

## Condition

Four needs, each `0...100`, and **higher is always better**:

| Need | A new Workling starts at |
| --- | ---: |
| Fullness | 85 |
| Energy | 80 |
| Happiness | 70 |
| Trust | 50 |

New Worklings favour Berries and Puzzle play.

> Internals: the code stores `hunger` and the interface derives `Fullness = 100 - hunger`. Only hunger is persisted. Everywhere else — docs, UI, thresholds — we speak Fullness.

## Time passing

Per hour away or idle:

| Need | Change per hour |
| --- | ---: |
| Fullness | -4 |
| Energy | -3 |
| Happiness | -1 |
| Trust | stable |

A starving or exhausted Workling also bleeds Happiness and Trust. Offline progression caps at seven days, so a long trip doesn't come home to a tragedy — but `lastUpdatedAt` always advances, so the same absence is never punished twice.

These rates are alpha tuning. The current Fullness drain can make Pixel feel permanently hungry after long absences; expect rebalancing from real usage.

## Care actions

| Action | Effect |
| --- | --- |
| Favourite food | Fullness +30, Happiness +8, Trust +3 |
| Other food | Fullness +20, Happiness +3, Trust +1 |
| Favourite play | Fullness -8, Energy -14, Happiness +22, Trust +6 |
| Other play | Fullness -7, Energy -12, Happiness +14, Trust +3 |
| Pet | Happiness +8, Trust +4 |
| Sleep | Fullness -6, Energy +35, Happiness +2 |

Everything clamps to `0...100`. Feed is unavailable at Fullness 100, Play below 15 Energy, Sleep at 100 Energy; Pet always works. Favourites earn bigger reactions. Asking an exhausted Workling to play gets a refusal, not a state change.

## Mood

First match wins:

1. **Hungry** — Fullness `<= 25`
2. **Sleepy** — Energy `<= 20`
3. **Wary** — Trust `<= 20`
4. **Sad** — Happiness `<= 30`
5. **Happy** — Happiness `>= 75`, Trust `>= 60`, and Fullness `>= 60`
6. **Content** — otherwise

`PetCareStatus` separately ranks notice/urgent/critical conditions for hover summaries and action availability — see the [interaction model](pet_interaction.md).

## Presentation

`PetPresentation` turns mood and the latest reaction into a face, palette, label, and short thought; `WorklingPetView` maps that to the selected family's sprite frames. Care reactions take over for about three seconds, then normal state resumes.

## The save

Versioned JSON at `~/Library/Application Support/Worklings/pet-state.json`, written atomically. Version 1 holds: schema version, name, family, the four needs (as hunger internally), favourites, and the last progression timestamp.

An unreadable save is never overwritten — it's preserved, persistence pauses for the session, and a fresh in-memory pet takes over. First launch after the rebrand copies a legacy Build Companion save forward without deleting it.

Progression fields — level, XP, banked stat points, allocated stats — will extend this save **additively** per the [progression design](progression.md), the same way the family field did: old saves load unchanged.

## Checks

`swift run CompanionCoreChecks` covers clamping, defaults, mood priority, deterministic progression, offline caps, care tradeoffs and refusals, persistence round trips, corrupt-save preservation, family switching, urgency, presentation, and placement.

## Next Pet Brain work

- Activity context: a short-lived input layer for normalized events, separate from long-lived relationship state.
- The condition XP multiplier and progression fields from the [progression design](progression.md).
- Tune need rates from real usage.
- Personality beyond two favourites.
- Reversible neglect and runaway recovery.
- Mood- and need-driven movement, attention-seeking, and sleep intents on top of idle roaming.
- Adoption and initial creature setup without resetting relationship state.
