# Build Companion

Build Companion is an experimental macOS desktop pet designed to feel like a small, persistent companion rather than another status widget.

The project starts with coding activity—especially Codex—but its longer-term goal is broader: a private, local companion that can react to work across IDEs, agents, and other explicitly connected applications. The pet has its own needs, preferences, moods, and routines, and its behavior should remain meaningful even when no integration is active.

## What we are building

Build Companion combines three ideas:

- **A living pet:** hunger, energy, happiness, trust, preferences, reactions, and reversible neglect.
- **A respectful desktop presence:** a floating companion that can be moved, tucked away, and eventually roam without obstructing work.
- **Provider-neutral activity awareness:** Codex is the first planned activity source, but the Pet Brain consumes generic activity events rather than Codex-specific state.

The project is macOS-first and implemented in Swift with SwiftUI and AppKit. Pet state is processed and stored locally. Keystrokes, screen contents, prompts, and source code are outside the default data model.

## Current state

The current experimental build includes:

- a transparent floating companion window;
- a drawn placeholder pet that respects Reduce Motion;
- hunger, energy, happiness, and trust;
- favourite food and play preferences;
- deterministic time progression and capped offline progression;
- versioned local JSON persistence;
- hover summaries for relevant needs;
- a clickable care card with Feed, Play, Pet, and Sleep actions;
- menu-bar wake, tuck-away, care, and quit controls;
- dependency-free behavioral checks for simulation, persistence, presentation, care status, and window placement.

Final pixel art, autonomous movement, adoption, richer personality, activity integrations, and public packaging remain in development.

## Use from the repository

### Requirements

- macOS 14 or newer;
- Apple Command Line Tools or Xcode;
- Swift 6-compatible toolchain;
- Git.

Clone and enter the repository:

```bash
git clone git@github.com:Bingeljell/build_companion.git
cd build_companion
```

Run the companion:

```bash
swift run BuildCompanion
```

The first build may take a moment. Pixel appears as a floating desktop companion and adds a paw icon to the menu bar.

### Interacting with Pixel

- Hover over Pixel for a short natural-language status summary.
- Click Pixel to open the care card.
- Drag Pixel to reposition it without opening the card.
- Use Feed, Play, Pet, and Sleep to affect its needs.
- Use the paw menu to inspect state, tuck Pixel away, wake it, or quit.
- Press `Control+C` in the launching terminal to stop the process directly.

Pet state is stored under the current user's Application Support directory and restored on the next launch.

## Build and verify

Build every target:

```bash
swift build
```

Run the dependency-free behavioral checks:

```bash
swift run CompanionCoreChecks
```

The check runner is used because a minimal Apple Command Line Tools installation may not include XCTest or Swift Testing.

## Beta application download

A signed or notarized beta DMG is not available yet. Direct-download beta builds are planned for [GitHub Releases](https://github.com/Bingeljell/build_companion/releases) after the interaction model and application packaging are stable enough for non-developer use.

Until then, running from source is the supported path.

## Project direction

Near-term work focuses on proving the care loop and desktop interaction before adding autonomous movement. Later milestones include:

- intent-driven walking, resting, and attention-seeking;
- richer needs, routines, preferences, and recoverable neglect;
- Codex lifecycle reactions through documented integration points;
- adapters for other IDEs and agents;
- application bundling, signing, notarization, and beta distribution.

## Documentation

- [Product brief](docs/product_brief.md)
- [Architecture](docs/architecture.md)
- [Pet Brain](docs/pet_brain.md)
- [Pet interaction model](docs/pet_interaction.md)
- [Git workflow](docs/git_workflow.md)
- [Changelog](docs/changelog.md)

Build Companion is currently an experiment. Interfaces, save formats, behavior rates, and visual presentation may change while the core experience is being validated.
