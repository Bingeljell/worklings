# Worklings Architecture

## Status

The macOS host, persistent Pet Brain, care UI, legacy-save transition, behavioral checks, and direct-download packaging toolchain are implemented. Autonomous movement and activity integrations remain planned.

## Implemented system

```text
AppKit window and menu ─┐
                       ├─> PetSession ─> PetBrain ─> PetState
SwiftUI pet and card ──┘       │             │
                               │             └─> PetCareStatus / PetPresentation
                               └─> versioned JSON store
```

The simulation works without an activity source. UI surfaces call the same session actions and consume the same state.

## Planned activity boundary

```text
Activity source -> normalized event -> activity context -> Pet Brain intent -> presentation
```

Raw prompts, source code, tool arguments, window contents, and keystrokes are outside this contract.

## Technology and targets

Worklings uses Swift Package Manager and Apple frameworks only, targeting macOS 14 or newer.

- `CompanionCore`: deterministic state, simulation, persistence primitives, care status, and presentation decisions.
- `Worklings`: AppKit application/window behavior, SwiftUI views, live session, menu bar, and filesystem wiring.
- `CompanionCoreChecks`: dependency-free executable verification for machines without XCTest or Swift Testing.

SwiftUI handles declarative content. AppKit handles lifecycle, menu-bar behavior, transparent floating panels, pointer tracking, drag-versus-click classification, focus, and display coordinates.

## Application host

`WorklingsApp` starts the accessory application. `AppDelegate` constructs the shared `PetSession`, companion panel, and menu-bar controls. Application code must not duplicate Pet Brain rules.

## Companion window

The current panel:

- floats above normal windows;
- has a transparent background;
- can join Spaces and remain visible around full-screen workflows;
- supports dragging without opening the care card;
- clamps placement to the visible display frame;
- can be tucked away and restored;
- respects Reduce Motion in Wildkin idle animation.

Autonomous roaming, intent-driven movement, collision/obstruction policy, and multi-display travel are not implemented. A small moving window remains preferred over a display-sized overlay because it minimizes input interception.

## Pet simulation and presentation

`PetBrain` owns needs, preferences, actions, time progression, and semantic reactions. `PetState` owns the versioned relationship state. `PetCareStatus` owns urgency, natural-language summaries, and action availability. `PetPresentation` converts mood and reaction into presentation intent, and `WildkinPetView` maps that intent to sprite-sheet frames.

Internal hunger is presented as derived Fullness so all exact UI meters increase in the healthy direction. The derived value is not persisted.

Final artwork must remain a presentation concern. Wildkin, Elemental, and Relicborn assets should not introduce species-specific conditionals into the core simulation; family and species differences should enter through data or explicit personality/configuration models.

## Live session

`PetSession` is the single source of live app state. It:

- loads or creates Pixel;
- advances the brain at launch and every 60 seconds;
- guards and performs care actions;
- exposes short-lived reactions;
- persists updated state;
- reports local persistence warnings.

## Persistence and rebrand compatibility

The active save is:

```text
~/Library/Application Support/Worklings/pet-state.json
```

When this file is absent and the legacy Build Companion save exists, Worklings copies the legacy file into the new directory and preserves the original. If copying fails, the legacy store remains the fallback rather than losing progress.

JSON remains appropriate until query, concurrency, or migration requirements demonstrate a database need. Writes are atomic, schema versions are explicit, decoded values are clamped, and unreadable saves are never silently overwritten.

## Activity event pipeline

The pipeline is designed but not implemented. Each future adapter should emit:

```text
ActivityEvent
  sourceID       stable adapter identifier
  sessionID      optional opaque correlation identifier
  phase          started | active | awaitingInput | completed | failed | stopped
  occurredAt     timestamp
  intensity      optional normalized value
```

An event reducer will convert short-lived events into activity context. The Pet Brain should consume that context without knowing whether it originated in Codex, another agent, an IDE, or the operating system.

The first Codex adapter should use documented lifecycle signals rather than UI scraping or unstable transcript parsing. It must be optional and fail closed.

## Privacy and permissions

The current application processes state locally and requests no screen-recording, keystroke, or Accessibility permission. Future integrations must declare what they observe, why it is needed, and whether anything is retained. Sensitive capabilities must be independently opt-in.

Activity is behavioral context, not a productivity score. A Workling's survival must not depend directly on how much the user works.

## Packaging and distribution

The release scripts build an architecture-specific release executable, assemble `Worklings.app`, write bundle metadata, apply an ad-hoc signature, create a compressed DMG and SHA-256 checksum, then mount and inspect the final artifact.

The public `v0.1.0-alpha.1` asset predates the Worklings technical rename and is still branded Build Companion. The first Worklings-branded public artifact has not yet been published.

## Testing strategy

- Deterministic checks cover simulation, actions, urgency, presentation, persistence, and screen placement.
- Persistence checks cover round trips, schema rejection, corrupt-file preservation, clamping, and derived-value exclusion.
- Release scripts verify metadata, architecture, signatures, checksums, DMG integrity, and mounted contents.
- Manual macOS review remains necessary for hover, click/drag, focus, Spaces, full-screen apps, displays, accessibility, wake-from-sleep, installation, and launch.
- Future adapters require independent normalization and reducer tests.

## Delivery status

| Slice | Status |
| --- | --- |
| Floating draggable companion and tuck-away control | Complete |
| Deterministic needs and local persistence | Complete |
| Direct care interactions, preferences, hover, and care card | Complete |
| Worklings rebrand and legacy-save copy | Complete |
| App bundle, DMG, checksum, and mounted verification | Complete |
| Safe idle roaming within one display | Planned |
| Provider-neutral event pipeline with a simulated source | Planned |
| Codex adapter | Planned |
| First Wildkin runtime sprite set | Complete |
| Adoption and creature-family selection | Planned |
| Developer ID signing and notarization | Deferred |
