# Build Companion Product Brief

## Vision

Build Companion is a local desktop creature that develops an ongoing relationship with its user. It begins by reacting to Codex activity and grows into a provider-neutral companion that can respond to work across IDEs, coding agents, and other explicitly connected applications.

The companion should feel alive without becoming punitive, distracting, or invasive.

## Product principles

1. **A pet first:** Work status influences the pet, but the pet also has needs, preferences, routines, and spontaneous behavior of its own.
2. **Local and private by default:** Observe activity signals rather than user content. Do not capture keystrokes, screens, prompts, or source code.
3. **Integrations are adapters:** Codex is the first activity source, not a dependency embedded in the simulation.
4. **Gentle consequences:** Neglect may cause sulking, illness, reduced trust, or a reversible runaway state. The initial product has no permanent death.
5. **Respect the desktop:** Roaming must not obstruct work. The user can drag, pause, tuck away, or reduce the pet's motion.
6. **Experiment before distribution:** Prove the behavior locally before investing in signing, notarization, packaging, or public-release administration.

## Initial user experience

The user launches a small, transparent macOS companion that remains above normal windows. The pet can idle, walk within the usable area of a display, sleep, and react to direct interaction. A compact menu exposes care actions and controls without turning the experience into a dashboard.

As the user works in Codex, the companion reacts to generic activity conditions such as active work, waiting for input, completion, and failure. The same conditions can later come from other adapters.

## MVP scope

### Companion shell

- Transparent floating window with a placeholder pet.
- Safe movement within the visible desktop area.
- Idle, walking, sleeping, eating, playing, and reaction states.
- Drag, pause roaming, and tuck-away controls.
- Reduced-motion behavior.

### Life simulation

- Hunger, energy, happiness, and trust needs.
- Feed, play, pet, and sleep interactions.
- Simple individual preferences that modify reactions and outcomes.
- Versioned local persistence and offline time progression.
- Reversible neglected or runaway state.

### Activity response

- Provider-neutral activity events.
- Codex as the first adapter.
- Reactions to working, awaiting input, completed, and failed activity.
- No activity-content collection.

## Explicit non-goals for the MVP

- Permanent death.
- Cloud accounts or synchronization.
- App Store distribution.
- Screen recording, keystroke logging, or source-code analysis.
- Windows or Linux builds.
- A full Codex chat client.
- Economy, trading, multiplayer, or monetization systems.

## Success criteria

The MVP succeeds when:

- the pet can run for a normal workday without obstructing other applications;
- its state survives restart and advances predictably while closed;
- care actions produce understandable but personality-dependent reactions;
- Codex activity changes behavior through an adapter rather than simulation-specific code;
- the user can disable movement and integrations at any time;
- the project builds from a clean checkout with documented Apple tooling.

## Later exploration

- VS Code and JetBrains adapters.
- Foreground-application and idle-time signals with explicit consent.
- Additional pets, personalities, preferences, and animation packs.
- Signed and notarized direct-download releases.
- Cross-platform shells after the macOS interaction model is validated.
