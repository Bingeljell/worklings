# Worklings Architecture

## Status

This document records the initial architecture for an experimental macOS-first implementation. Decisions should be revisited when a working vertical slice provides evidence.

## System boundaries

Worklings consists of a desktop host, a deterministic pet simulation, presentation and movement logic, local persistence, and optional activity-source adapters.

```text
Activity sources -> normalized events -> pet simulation -> pet intent -> presentation
                           |                   |
                           |                   +-> versioned local state
                           +-> local, minimal event metadata
```

The simulation must function when no activity integration is enabled.

## Technology decision

Use Swift with SwiftUI for declarative interface elements and AppKit for window behavior that SwiftUI does not expose cleanly.

Reasons:

- precise control of transparent, floating macOS windows;
- native support for displays, accessibility preferences, menus, and application lifecycle;
- low idle overhead for an application intended to remain open all day;
- no third-party runtime dependency for the first experiment.

The first implementation should use Swift Package Manager and Apple frameworks only. Full Xcode may be introduced when packaging, signing, profiling, or project-level tooling requires it.

## Logical components

### Application host

Owns application lifecycle, menu commands, permissions, dependency construction, and clean shutdown. It must not contain simulation rules.

### Companion window

Wraps an AppKit panel or borderless window with a transparent background. It is responsible for:

- window level and workspace behavior;
- dragging and click handling;
- movement between safe positions;
- multi-display coordinate conversion;
- avoiding the menu bar, Dock, and unavailable screen regions;
- pausing or reducing animation when requested.

A small moving window is preferred over a display-sized transparent overlay because it minimizes input interception.

### Pet simulation

Owns needs, preferences, relationship state, time progression, and behavioral intent. Its update operation accepts an explicit timestamp or elapsed duration so tests do not depend on wall-clock timing.

Initial needs:

- hunger;
- energy;
- happiness;
- trust.

Simulation output is an intent such as idle, roam, seek attention, sleep, eat, play, celebrate, worry, sulk, or run away. Presentation chooses the animation for that intent.

### Activity event pipeline

Every integration implements an activity-source protocol and emits a normalized event:

```text
ActivityEvent
  sourceID       stable adapter identifier
  sessionID      optional opaque correlation identifier
  phase          started | active | awaitingInput | completed | failed | stopped
  occurredAt     timestamp
  intensity      optional normalized value
```

Raw prompts, source code, tool arguments, window contents, and keystrokes are outside this contract.

The event reducer converts short-lived source events into an activity context. The pet simulation consumes that context without knowing whether it originated in Codex, an IDE, or the operating system.

### Codex adapter

The first adapter will map supported Codex lifecycle signals to normalized events. The implementation should prefer explicit hooks or another documented interface over scraping application UI or unstable transcript formats.

The adapter is optional and must fail closed: if it is unavailable or untrusted, the pet continues running without Codex awareness.

### Persistence

Begin with a versioned JSON document in the user's Application Support directory. Use atomic replacement when saving and retain enough metadata to calculate offline progression safely.

Do not introduce a database until query, concurrency, or migration requirements demonstrate that JSON is insufficient. Never silently discard an unreadable save; preserve it for recovery and begin with a clearly reported fallback state.

## Privacy and permissions

The default product processes all state locally and requests no screen-recording or keystroke access.

Integrations declare their capabilities and required permissions. More sensitive capabilities, such as foreground window titles or Accessibility access, must be independently opt-in and must explain what is observed, why it is needed, and whether anything is retained.

## State and time rules

- Persist an explicit schema version.
- Clamp offline elapsed time to prevent clock changes from corrupting needs.
- Make decay and recovery rates configurable constants.
- Keep neglect reversible in the MVP.
- Do not tie survival directly to how much the user works or codes.
- Treat activity as behavioral context, not as a reward score.

## Testing strategy

- Unit-test simulation transitions with a controlled clock and deterministic random source.
- Unit-test persistence round trips, schema migration, corrupt-file preservation, and offline progression.
- Unit-test event normalization independently for every adapter.
- Exercise window placement against multiple synthetic screen frames.
- Run a manual macOS smoke test for drag behavior, Spaces, full-screen apps, multiple displays, reduced motion, and wake-from-sleep.

## Delivery slices

1. Floating placeholder that can be dragged and tucked away.
2. Safe idle roaming within one display.
3. Deterministic needs and local persistence.
4. Direct care interactions and preferences.
5. Provider-neutral event pipeline with a simulated source.
6. Codex adapter.
7. Packaging and public beta hardening.

Each slice should remain runnable and testable without requiring the next integration.
