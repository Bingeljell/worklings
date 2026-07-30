# Worklings Documentation

The map of the `docs/` tree. Detailed design and engineering notes live here; the
repository root keeps only entry-point and governance files (`README.md`,
`CONTRIBUTING.md`, `LICENSE`, `SECURITY.md`, `CODE_OF_CONDUCT.md`).

Docs describe **implemented behavior unless a section marks itself deferred**, so a
reader can trust the present tense and treat "planned" / "deferred" as the only
future-tense signals.

## Top level

- [Product brief](product-brief.md) — vision, MVP scope, and what "done" means.
- [Changelog](changelog.md) — append-only record of every change, newest first. Updated on every commit; entries record paths as they were at the time, so historical links are intentionally not rewritten.

## design/ — the game

How a Workling lives, grows, and (soon) fights. This is the active growth area:
abilities, dungeons, and the class/family splits all land here as they take shape.

- [Gameplay loop](design/gameplay-loop.md) — the umbrella: how care, progression, and content fit into one loop. Start here.
- [Pet Brain](design/pet-brain.md) — the condition layer: needs, moods, deterministic simulation, care rules.
- [Progression](design/progression.md) — the character sheet: XP, levels, stats, class, and the systems ladder up to dungeons and endgame.
- [Characters](design/characters.md) — the identity layer: families, species, and the five-class roster.
- [Interaction](design/interaction.md) — how a player reads and cares for a Workling: the pet, hover, care card, menu bar.

## engineering/ — how it's built

- [Architecture](engineering/architecture.md) — targets, boundaries, the activity inbox, privacy posture.
- [Cross-platform architecture](engineering/cross-platform-architecture.md) — the macOS-first stance and the decision gates before any expansion. Guidance, not committed scope.
- [Adapters](engineering/adapters.md) — the "sending half": how external tools feed content-free activity events in.

## process/ — how we work

- [Git workflow](process/git-workflow.md) — branching, committing, the `scripts/committer` safeguards, releases.
- [Distribution](process/distribution.md) — the beta packaging, signing, and release contract.
- [Audit follow-ups](process/audit-followups.md) — the deferred backlog from connector/adapter security reviews.
