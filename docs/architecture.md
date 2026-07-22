# Worklings Architecture

## Status

The macOS host, persistent Pet Brain, three-family runtime selection, care UI, safe opt-in idle roaming, legacy-save transition, behavioral checks, direct-download packaging toolchain, the normalized activity-event pipeline with a debug simulated source, and the opt-in activity inbox external adapters write into are implemented. Intent-driven movement and the first real adapter (Codex) remain planned.

## Implemented system

```text
AppKit window and menu ─┐
                       ├─> PetSession ─> PetBrain ─> PetState
SwiftUI pet and card ──┘       │             │
                               │             └─> PetCareStatus / PetPresentation
                               └─> versioned JSON store
```

The simulation works without an activity source. UI surfaces call the same session actions and consume the same state.

## Activity boundary

```text
Activity source -> normalized event -> activity context -> Pet Brain intent -> presentation
```

The pipeline is implemented in `CompanionCore`: `ActivityEvent` carries kind, timestamp, and source id only; `ActivityContext` reduces events into short-lived, never-persisted state that expires when events stop; `PetBrain.observe` turns share-worthy events into reactions and small need changes; and `PetBrain.advance` accepts the context so active work modulates the simulation. A debug-build Simulate Activity menu is the first source, and the [activity inbox](#the-activity-inbox) is the doorway external adapters use; the adapters themselves remain planned.

Raw prompts, source code, tool arguments, window contents, and keystrokes are outside this contract. The event vocabulary, sources, and the progression systems built on top of it are defined in the [progression design](progression.md).

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
- can follow deterministic idle-roaming plans within its current display;
- pauses roaming for pointer interaction, dragging, care, tuck-away, transitions, and Reduce Motion;
- uses a shared eight-frame smoke overlay for launch, wake, tuck-away, and family replacement;
- can be tucked away and restored after the conceal animation completes;
- keeps idle and transition animation immediate when Reduce Motion is active.

Roaming is disabled by default and stored as a local application preference rather than pet state. `CompanionCore` produces deterministic normalized movement plans and safe screen targets; the AppKit controller owns timing, interruption, and frame animation. Mood-driven movement, obstacle awareness, and multi-display travel are not implemented. A small moving window remains preferred over a display-sized overlay because it minimizes input interception.

## Pet simulation and presentation

`PetBrain` owns needs, preferences, actions, time progression, and semantic reactions. `PetState` owns the versioned relationship state, including the selected `PetFamily`. `PetCareStatus` owns urgency, natural-language summaries, and action availability. `PetPresentation` converts mood and reaction into presentation intent, and `WorklingPetView` maps that intent to the selected family's sprite-sheet frames.

Internal hunger is presented as derived Fullness so all exact UI meters increase in the healthy direction. The derived value is not persisted.

Final artwork remains a presentation concern. Wildkin, Elemental, and Relicborn each have a packaged transparent 4-by-3 sheet using a shared twelve-frame order for idle, walking, mood, and care reactions. `PetFamily` selects the resource without introducing family-specific conditionals into the core simulation. A separate packaged smoke sheet and `CompanionTransitionPlan` define reveal, conceal, and family-swap presentation without entering persistent pet state. Future behavioral differences should enter through data or explicit personality/configuration models.

## Live session

`PetSession` is the single source of live app state. It:

- loads or creates Pixel;
- advances the brain at launch and every 60 seconds;
- guards and performs care actions;
- switches and persists the selected family without resetting relationship state;
- exposes short-lived reactions;
- persists updated state;
- reports local persistence warnings.

## Persistence and rebrand compatibility

The active save is:

```text
~/Library/Application Support/Worklings/pet-state.json
```

When this file is absent and the legacy Build Companion save exists, Worklings copies the legacy file into the new directory and preserves the original. If copying fails, the legacy store remains the fallback rather than losing progress.

JSON remains appropriate until query, concurrency, or migration requirements demonstrate a database need. Writes are atomic, schema versions are explicit, decoded values are clamped, and unreadable saves are never silently overwritten. The additive family field defaults to Wildkin when absent, keeping version 1 saves compatible.

## Activity event pipeline

The internal pipeline is implemented in `CompanionCore` (see [Runtime topology](#runtime-topology)): normalized events reduce into short-lived activity context, and the Pet Brain consumes that context without knowing whether an event originated in Codex, another agent, an IDE, or the operating system.

### The activity inbox

External adapters reach the pipeline through the **activity inbox**: a local spool directory the app watches, into which an adapter drops one small JSON file per event.

```text
~/Library/Application Support/Worklings/inbox/<any-name>.json

{
  "kind":      "workStarted | workEnded | taskCompleted | taskFailed | awaitingInput | milestone",
  "sourceId":  "stable lowercase adapter identifier, e.g. codex",
  "timestamp": "optional ISO8601; omitted means now"
}
```

The boundary is deliberately a file drop rather than a socket or local server: an adapter needs nothing but the ability to write a file (a three-line shell hook suffices), a missing or closed app costs the adapter nothing, and no network port ever opens. Adapters should write the file elsewhere and rename it into the inbox so a half-written payload is never observed; `scripts/emit-activity-event` demonstrates the pattern and serves as manual test tooling.

Validation lives in `CompanionCore.ActivityInbox` as pure, checked functions: unknown or app-owned kinds (`dailyWake`, presence, `workLogged`) are rejected, reserved source ids (`system`, `manual`, `simulated`) cannot be impersonated, malformed or oversize payloads are discarded, and timestamps older than the activity-context expiry window are dropped so a backlog written while the app was closed never replays onto the pet. `ActivityInboxMonitor` in the app target watches the directory, feeds valid events into the same `PetSession.receive` path every internal source uses, and deletes each file it inspects. The contract has no fields for content, so the privacy boundary is structural: an adapter physically cannot hand the pet prompts, code, or file paths.

The inbox is off by default and toggled by the "Accept Work Tool Events" menu item, independent of every other control. The first Codex adapter should use documented lifecycle signals rather than UI scraping or unstable transcript parsing, and emit into this inbox under its own source id.

## Privacy and permissions

The current application processes state locally and requests no screen-recording, keystroke, or Accessibility permission. Future integrations must declare what they observe, why it is needed, and whether anything is retained. Sensitive capabilities must be independently opt-in.

Activity is behavioral context, not a productivity score. A Workling's survival must not depend directly on how much the user works.

## Packaging and distribution

The release scripts build an architecture-specific release executable, assemble `Worklings.app`, write bundle metadata, apply an ad-hoc signature, create a compressed DMG and SHA-256 checksum, then mount and inspect the final artifact.

The first Worklings-branded public artifact is the `v0.1.0-alpha.2` GitHub prerelease. The older `v0.1.0-alpha.1` asset predates the Worklings technical rename and is still branded Build Companion.

## Testing strategy

- Deterministic checks cover simulation, actions, urgency, presentation, smoke transition midpoint behavior, persistence, and screen placement.
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
| Safe idle roaming within one display | Complete |
| Provider-neutral event pipeline with a simulated source | Complete |
| Activity inbox boundary for external adapters | Complete |
| Codex adapter | Planned |
| Wildkin, Elemental, and Relicborn runtime selection | Complete |
| Adoption and initial creature setup | Planned |
| Developer ID signing and notarization | Deferred |
