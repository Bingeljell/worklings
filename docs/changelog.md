# Changelog

- **2026-07-15 > docs/pet_brain.md > Pet Brain vertical-slice plan > Defines needs, time progression, care interactions, persistence, placeholder presentation, test coverage, and review criteria for the next four commits.**
- **2026-07-15 > Package.swift > package manifest > Adds a dependency-free macOS Swift package with application, core library, and executable check targets.**
- **2026-07-15 > Sources/CompanionCore/ScreenPlacement.swift > defaultOrigin and clampedOrigin > Calculates safe companion-window positions within the usable screen frame.**
- **2026-07-15 > Sources/BuildCompanion/BuildCompanionApp.swift > main > Starts Build Companion as a menu-bar macOS application.**
- **2026-07-15 > Sources/BuildCompanion/AppDelegate.swift > applicationDidFinishLaunching, toggleCompanionVisibility, and quit > Manages the companion lifecycle and menu-bar controls.**
- **2026-07-15 > Sources/BuildCompanion/CompanionPanelController.swift > configurePanel, placeOnMainScreen, show, and hide > Creates a transparent draggable floating panel and positions it safely on the main display.**
- **2026-07-15 > Sources/BuildCompanion/PlaceholderPetView.swift > body, ears, and face > Draws an animated placeholder companion that respects reduced-motion settings.**
- **2026-07-15 > Tests/CompanionCoreChecks/ScreenPlacementChecks.swift > screen placement checks > Covers default, clamped, and oversized-window placement without requiring a bundled test framework.**
- **2026-07-15 > scripts/committer > repository and branch detection > Supports the first commit on an unborn branch and refuses detached-HEAD commits.**
- **2026-07-15 > docs/git_workflow.md > branching, committing, pushing, and releases > Moves the Git workflow into the documentation folder and aligns it with the actual committer safeguards.**
- **2026-07-15 > docs/product_brief.md > product vision, MVP scope, and success criteria > Defines the macOS-first companion experiment, provider-neutral direction, privacy boundary, and reversible neglect model.**
- **2026-07-15 > docs/architecture.md > system boundaries and component decisions > Defines the native Swift architecture, activity adapter contract, deterministic simulation, local persistence, and delivery slices.**
- **2026-07-15 > .gitignore > macOS and Swift build exclusions > Prevents local metadata, build products, Xcode user state, and packaged disk images from entering version control.**
