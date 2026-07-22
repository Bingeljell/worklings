# Worklings Product Brief

## Vision

Worklings is a local desktop-pet experience built around the persistence, progression, identity, and attachment of an MMO character. A user cares for an individual Workling that develops an ongoing relationship with them.

The first activity-aware experience will react to Codex, but the longer-term system is provider-neutral: explicitly connected IDEs, agents, and other work tools emit small activity signals without handing Worklings the user's content.

The Workling should feel alive and worth returning to without becoming punitive, distracting, or invasive.

## Product principles

1. **A pet first:** Work context influences behavior, but the Workling has needs, preferences, routines, and spontaneous behavior of its own.
2. **MMO-style identity:** Creature family, personality, progression, and attachment should make a Workling feel owned rather than interchangeable.
3. **Local and private by default:** Observe activity state rather than prompts, source code, keystrokes, or screen contents.
4. **Integrations are adapters:** Codex is the first activity source, not a dependency embedded in the simulation.
5. **Gentle consequences:** Neglect may lead to sulking, illness, reduced trust, or a reversible runaway state. The MVP has no permanent death.
6. **Respect the desktop:** The user can drag, pause, tuck away, or reduce motion; roaming must not obstruct work.
7. **Stage distribution deliberately:** Source builds and experimental DMGs can validate the idea before Developer ID signing, notarization, or App Store work.

## Current validated experience

The macOS application currently launches Pixel in a transparent floating companion window above normal windows. Pixel defaults to a moss-fox Wildkin and can switch to an ember-newt Elemental or keyback pangolin Relicborn from the menu bar. Pixel can be dragged or tucked away, communicates needs through sprite poses and a hover summary, and opens a care card when clicked.

The care loop includes persistent needs, time and offline progression, favourite food and play choices, Feed, Play, Pet, and Sleep actions, menu-bar controls, and local JSON saves. Exact meters use positive semantics: more Fullness, Energy, Happiness, or Trust is always better.

The package, executable, bundle identifier, release scripts, repository, and save directory are now named Worklings. A legacy Build Companion save is copied forward without deleting the original.

## Character direction

The repository contains early concepts for three Workling families:

- **Wildkin:** creatures shaped by living ecosystems and natural magic.
- **Elemental:** creatures whose elemental affinity is part of their anatomy.
- **Relicborn:** creatures bonded to ancient mechanisms, relics, or rune-powered artifacts.

The moss-fox, ember-newt, and keyback pangolin establish world and silhouette direction. All three are packaged, mapped through the same twelve-frame pose contract, and selectable as Pixel's persistent appearance. This is not yet an adoption flow: switching family preserves the same name, needs, preferences, and relationship progress.

Families, species, and the five-class roster are catalogued in [Characters](characters.md), the single home for the identity layer.

## MVP progress

| Area | Implemented | Remaining |
| --- | --- | --- |
| Desktop shell | Transparent floating panel, drag, smoke-assisted tuck/wake, menu bar, safe initial placement, opt-in single-display roaming, pause control, Reduce Motion | Mood-driven movement, obstacle awareness, richer display behavior |
| Life simulation | Hunger, energy, happiness, trust, preferences, moods, care actions, deterministic progression | Tuning, deeper personality, routines, reversible neglect/runaway |
| Interaction | Selectable Wildkin, Elemental, and Relicborn mood/reaction frames; shared smoke transitions, idle cycle, delayed hover, care card, menu fallback, accessibility labels | Adoption, movement and richer action animation, broader keyboard and VoiceOver review |
| Persistence | Versioned atomic JSON, selected-family compatibility, corrupt-save preservation, offline cap, legacy save copy | Recovery UI and future schema migrations |
| Activity response | Provider-neutral architecture documented | Event types, reducer, simulated source, Codex adapter |
| Distribution | Worklings app/DMG/checksum scripts, mounted verification, and the published `v0.1.0-alpha.2` prerelease | Developer ID signing, notarization |

## MVP scope still intended

- Mood- and need-driven behavior intents that build on safe idle roaming.
- Idle, walking, sleeping, eating, playing, and work-reaction states.
- Reversible neglect or runaway behavior.
- Provider-neutral activity events.
- Codex reactions for working, awaiting input, completion, and failure.
- Clear controls to disable movement and integrations.

## Explicit non-goals for the MVP

- Permanent death.
- Cloud accounts or synchronization.
- App Store distribution.
- Screen recording, keystroke logging, prompt capture, or source-code analysis.
- Windows or Linux builds.
- A full Codex chat client.
- Economy, trading, multiplayer, battle systems, or monetization.

Those MMO systems remain possible later directions, not commitments for the first usable pet.

## Success criteria

The MVP succeeds when:

- a Workling can run for a normal workday without obstructing other applications;
- state survives restart and advances predictably while closed;
- care actions and personality differences are understandable;
- autonomous behavior feels alive without becoming distracting;
- Codex activity reaches the Pet Brain through a generic adapter boundary;
- movement and integrations can be disabled independently;
- privacy boundaries are understandable and testable;
- the project builds from a clean checkout and ships through a documented release process.

## Later exploration

- VS Code and JetBrains adapters.
- Foreground-application and idle-time signals with explicit consent.
- Additional families, individual species, personalities, progression, and animation packs.
- Signed and notarized direct-download releases.
- Battle, collection, social, or multiplayer systems after the solo relationship loop proves compelling.
- Cross-platform shells after the macOS interaction model is validated.
