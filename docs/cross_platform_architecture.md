# Cross-Platform Architecture Roadmap

## Status and intent

Worklings is currently a macOS application written in Swift, SwiftUI, and AppKit. That remains the right implementation for validating the product: the current simulation is small, Swift is native and performant, and Apple frameworks provide direct access to the window, menu-bar, display, filesystem, and accessibility behavior the macOS experience needs.

There is no planned rewrite. This document records boundaries and decision points that should keep a later expansion possible without compromising the macOS product now. A platform or framework is not selected until its user experience, operating-system constraints, maintenance cost, and safety properties have been tested.

## Guiding principles

1. **Validate the product before changing stacks.** Cross-platform work should follow evidence that people value the persistent companion, not precede it.
2. **Share rules and data before sharing interface code.** Pet progression, care, reactions, and event semantics are more durable than any windowing toolkit.
3. **Treat each platform as a product surface.** A floating desktop companion, an iOS Live Activity, and an Android overlay are not equivalent experiences.
4. **Keep integrations voluntary, local, minimal, and reversible.** A platform port must preserve the existing rule that Worklings observes activity signals, not work content, and never damages another tool's configuration.
5. **Prefer data compatibility over implementation compatibility.** Stable schemas and behavioral test vectors permit a future implementation in another language without forcing every platform through a foreign-function boundary.
6. **Do not optimize for hypothetical performance.** Introduce Rust or an engine only when a measured need or chosen host architecture justifies the additional build and maintenance surface.

## Target architectural boundary

The current `CompanionCore` target contains both portable domain behavior and macOS-oriented infrastructure. Before adding a second platform, separate those responsibilities conceptually and, when useful, into explicit modules:

```text
Platform activity sources ─> normalized ActivityEvent ─┐
                                                       │
Platform UI ───────────────> user actions ─────────────┼─> WorklingsDomain
                                                       │        │
Platform clock ────────────> time input ───────────────┘        │
                                                                v
                                                    state + presentation intent
                                                                │
                    ┌───────────────────────────────────────────┼──────────────┐
                    v                                           v              v
              macOS renderer                              future renderer   persistence
           (SwiftUI + AppKit)                            (engine/toolkit)     adapter
```

### `WorklingsDomain`

The portable domain should contain:

- pet state, needs, preferences, family, mood, and relationship progression;
- deterministic time advancement and offline-progress limits;
- care actions and their availability rules;
- normalized activity-event vocabulary and reduction into short-lived context;
- reactions and platform-neutral presentation intent;
- schema versions, validation rules, and migrations expressed without operating-system APIs.

The domain should accept time, random seeds, events, and user actions as inputs. It should not read the clock, filesystem, process environment, user defaults, displays, or tool configuration itself.

The following should remain outside `WorklingsDomain`:

- `CoreGraphics` types such as `CGPoint`, `CGSize`, and `CGRect`;
- AppKit, SwiftUI, window placement, pointer tracking, menu-bar behavior, and animation timing;
- filesystem locations, `FileManager`, file watching, and atomic-write mechanics;
- Codex, Claude Code, Git, or future provider-specific configuration;
- shell commands and platform-specific process launching;
- notification, widget, overlay, or permission APIs.

If roaming calculations remain useful to multiple platforms, define small domain-owned geometry values such as `WorklingsPoint`, `WorklingsSize`, and `WorklingsRect`. Convert those values to `CoreGraphics`, Godot, Flutter, or other toolkit types at the platform boundary.

### Platform adapters

Platform code translates between operating-system capabilities and the domain:

- **Host/UI adapter:** windows, menus, widgets, lifecycle, accessibility, input, rendering, and animation.
- **Persistence adapter:** application-support location, safe reads and writes, backups, migrations, and recovery.
- **Activity-source adapters:** provider hooks, repository watching, and other opt-in signals converted into the shared `ActivityEvent` contract.
- **Clock and scheduling adapter:** wall-clock input, timers, wake-from-sleep handling, and background execution allowed by that platform.

Provider-specific configuration must not become a requirement for the domain. Unsupported integrations on a platform should reduce available activity sources, not change pet-state semantics or corrupt a shared save.

## Portable contracts

### Save data

Keep the pet save as documented, versioned JSON for as long as its scale permits. Changes should remain additive where possible, migrations must be explicit, and an unreadable or newer save must never be silently replaced.

A future platform should be able to decode the same logical state even if it uses a different language. File locations and synchronization are platform concerns; the schema is a product contract.

Cloud synchronization, if introduced, requires a separate design for identity, conflicts, deletion, encryption, offline behavior, and rollback. It should not be inferred merely from supporting more platforms.

### Activity events

Preserve the narrow normalized event contract: kind, source identifier, and time. Raw prompts, source code, diffs, tool arguments, filenames, and window content remain outside the contract.

Transport may differ by platform. The current local inbox is appropriate for macOS command-line integrations, while another platform may use an application API, deep link, extension, or no external source at all. Transport differences must not expand the data observed without a separate privacy and consent review.

### Behavioral compatibility

Add golden behavioral fixtures before a second implementation is started. Each fixture should contain:

- a versioned initial state;
- explicit timestamps, events, actions, and any random seed;
- the expected resulting state and presentation intent.

Run the fixtures against the Swift domain and any future implementation. This is more useful than requiring identical internal class structures or prematurely maintaining Swift-to-Rust bindings.

## Technology options

The alternatives below are candidates for prototypes, not commitments.

| Option | Strongest fit | Principal tradeoff | When to investigate |
| --- | --- | --- | --- |
| SwiftUI + AppKit | Highest-quality macOS host and Apple-platform integration | Interface code does not carry directly to Windows or Linux | Continue for the current product; also suitable if expansion is limited to Apple platforms |
| Godot | A game-first, animated companion across Windows, macOS, and Linux | Native menus, settings, accessibility, hooks, and OS-specific polish may require extensions or companion code | First candidate to prototype if the priority is a consistent desktop game experience |
| Flutter | App-style interface reuse across desktop and mobile | Transparent, always-on-top, click-through companion behavior may require plugins and native host work | Prototype if mobile and conventional app screens become more important than desktop-pet behavior |
| Tauri + Rust | A small system-webview application with a Rust backend and broad desktop reach | A webview adds another rendering/runtime boundary, and unusual companion-window behavior still needs platform validation | Consider if the product evolves toward a conventional desktop app with web-based UI |
| Native host per platform | Best platform-specific behavior and safety | More interface implementations to maintain | Consider when native polish and system integration matter more than shared UI code |
| Rust domain library | Memory-safe shared native core | Foreign-function interfaces, bindings, packaging, and debugging across languages add substantial complexity | Introduce only for a measured performance need or when the selected host already makes Rust a natural boundary |

Godot deserves the first non-macOS desktop feasibility spike because its windowing features align with the companion concept: transparent and borderless windows, always-on-top behavior, mouse passthrough, and desktop/mobile exports are documented capabilities. A prototype must still test those capabilities on every intended operating system; a feature existing in an engine does not guarantee identical window-manager behavior or native-quality accessibility.

References:

- [Swift supported platforms](https://www.swift.org/platform-support/)
- [Godot feature list](https://docs.godotengine.org/en/stable/about/list_of_features.html)
- [Godot project exporting](https://docs.godotengine.org/en/stable/tutorials/export/exporting_projects.html)
- [Flutter supported platforms](https://docs.flutter.dev/reference/supported-platforms)
- [Tauri architecture](https://v2.tauri.app/concept/architecture/)

## Platform capability questions

Before choosing a target, answer these questions with a small product brief and prototype.

### Windows and Linux desktop

- Can the Workling float without stealing keyboard focus?
- Can pointer input pass through transparent regions while the visible creature remains interactive?
- Do always-on-top behavior, multiple displays, virtual desktops, scaling, and sleep/wake work reliably?
- Can the app provide a native tray/menu experience, accessibility metadata, autostart, updates, signing, and safe uninstall?
- Which activity sources exist, and can they be connected without overwriting user configuration or requiring broad permissions?

### iPhone and iPad

iOS does not provide a general-purpose window that floats above other applications. A mobile Workling would need a deliberately different form: an in-app habitat, widget, Live Activity, Dynamic Island presentation where available, notification interaction, or a combination of these. Background execution, update frequency, and data sources are constrained by the operating system.

Reference: [Apple ActivityKit](https://developer.apple.com/documentation/ActivityKit)

### Android

Android can support drawing over other applications through special access, but this is a sensitive permission with user-trust, store-policy, device-compatibility, battery, and accessibility consequences. An overlay should be treated as an optional capability, not the default assumption. A widget, notification, or in-app habitat may be the better initial product.

Reference: [Android special permissions](https://developer.android.com/training/permissions/requesting-special)

## Recommended sequence

### Phase 1 — Validate on macOS

- Continue with SwiftUI and AppKit.
- Complete adoption, progression, safety, distribution, signing, and notarization work.
- Measure retention, interaction patterns, and which activity sources users actually value.
- Avoid Rust or engine migration without a demonstrated product or performance reason.

### Phase 2 — Harden portable boundaries

- Identify the pure subset of `CompanionCore` and name the intended boundary `WorklingsDomain`.
- Move `CoreGraphics` geometry and screen placement into macOS/platform infrastructure, or replace portable calculations with domain-owned geometry values.
- Inject clock and random inputs into domain behavior.
- Keep persistence mechanics and tool connectors behind adapters.
- Publish versioned save and activity-event fixtures.
- Add golden behavioral tests.

This refactoring can happen incrementally. A module split is worthwhile when it makes a boundary enforceable, not merely to increase the number of targets.

### Phase 3 — Choose one expansion hypothesis

Write a short decision record naming:

- the first target platform;
- the intended user experience on that platform;
- required integrations and permissions;
- which behavior and data must be shared;
- distribution, update, accessibility, and uninstall expectations;
- explicit success and cancellation criteria for the prototype.

Do not choose “all platforms” as the first target.

### Phase 4 — Build a disposable vertical slice

For a desktop expansion, first test Godot with one transparent companion window, idle animation, dragging, click-through behavior, multiple displays, focus, sleep/wake, and one mocked activity event. Do not port the full Pet Brain until the operating-system experience is proven.

For mobile, prototype the actual allowed surface—such as an iOS Live Activity or an Android widget—rather than using a conventional app screen as evidence that the companion experience will work.

### Phase 5 — Make the implementation decision

Choose among a shared engine/toolkit, native hosts, or a hybrid only after the vertical slice. Record:

- user-experience quality;
- accessibility and permission behavior;
- installer, updater, and uninstall safety;
- platform-specific code required;
- build and release complexity;
- performance and battery measurements;
- maintenance burden for a small team.

Rust becomes a justified choice if those results identify a concrete role for it. It is not a prerequisite for speed, portability, or safety by itself.

## Decision gate

Cross-platform implementation should begin only when:

- the macOS product has evidence worth carrying to another platform;
- one target and its product experience are named;
- portable state and event contracts have compatibility fixtures;
- the target's windowing or mobile surface has been proven in a disposable prototype;
- privacy, consent, configuration ownership, persistence, uninstall, accessibility, signing, and updates have explicit acceptance criteria.

Until that gate is met, the preferred architecture is a well-factored Swift macOS application with portable domain rules—not a speculative rewrite.
