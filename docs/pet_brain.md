# Pet Brain Vertical Slice

## Objective

The next Worklings milestone should prove a small care loop before adding autonomous movement or external activity integrations. The user should be able to care for the same persistent pet across launches and understand its condition through temporary placeholder visuals.

The implementation is split into four reviewable commits: this plan, the Pet Brain domain, placeholder application content, and comprehensive checks.

## Scope

### Pet state

The initial state contains:

- a schema version;
- a pet name;
- hunger, energy, happiness, and trust values;
- a favourite food and favourite play activity;
- the time at which progression was last calculated.

Every need uses a closed `0...100` range. Higher hunger is worse; higher energy, happiness, and trust are better. Values must be clamped at the domain boundary rather than only in the interface.

### Time progression

The simulation advances from an explicit previous timestamp to an explicit current timestamp. This keeps the Pet Brain deterministic and testable.

Initial real-time rates are deliberately gentle:

| Need | Baseline change per hour |
| --- | ---: |
| Hunger | +4 |
| Energy | -3 |
| Happiness | -1 |
| Trust | No baseline decay |

Severe hunger or exhaustion may add a small happiness and trust penalty. Offline progression is capped at seven days so clock changes or a long absence cannot irreversibly destroy the relationship.

### Care interactions

The first actions are:

- feed a selected food;
- play a selected activity;
- pet;
- sleep.

Actions update needs immediately and return a semantic reaction. Favourite food and play choices produce stronger positive outcomes. Actions still have tradeoffs: play consumes energy and increases hunger, while sleep restores energy but also increases hunger.

### Mood and reactions

The Pet Brain exposes a mood derived from needs rather than UI-specific colours or animation names. Initial moods are happy, content, hungry, sleepy, sad, and wary.

Urgent physical needs take precedence over positive mood. Placeholder presentation maps the semantic mood and the latest interaction reaction to a colour treatment, face, and short thought bubble.

### Persistence

State is encoded as versioned JSON under the user's Application Support directory. Saves use atomic file replacement.

If a save cannot be decoded, the application must preserve the unreadable file, report the failure locally, and run with a fresh in-memory state. It must not silently overwrite the unreadable save.

## Placeholder application experience

The existing drawn pet remains temporary. This slice adds enough feedback to evaluate the loop without committing to final art:

- face and colour changes based on mood;
- short thought bubbles for urgent needs and interactions;
- menu-bar summaries for the four needs;
- menu actions for feeding, playing, petting, and sleeping;
- clicking the pet performs the pet interaction;
- state loads on launch and saves after progression or interaction.

The interface should label temporary content clearly enough that later sprite work can replace presentation without changing Pet Brain rules.

## Test strategy

The repository's dependency-free executable check harness will cover:

- need clamping and mood priority;
- deterministic time progression and the offline cap;
- interaction tradeoffs and preference bonuses;
- JSON round trips and corrupt-save preservation;
- placeholder presentation mapping;
- existing screen placement behavior.

Full Xcode test frameworks remain deferred. The checks must run with the installed Apple Command Line Tools.

## Review criteria

At the end of the slice:

- relaunching restores the same pet and needs;
- time away changes needs predictably without permanent loss;
- all four care actions visibly affect the pet;
- preferences create observable differences;
- no raw activity or user-content collection is introduced;
- movement and Codex-specific behavior remain absent;
- the application builds and all dependency-free checks pass.

The review should decide whether the care loop feels understandable and worth extending before work begins on movement.
