# Contributing to Worklings

Thank you for helping make Worklings feel more alive. Worklings is still experimental, so focused contributions that preserve the product principles are the easiest to review and merge.

## Before starting

Open an issue before investing substantial time when a change would:

- add a dependency;
- change the save format or persistence compatibility;
- collect new data or request a new macOS permission;
- materially change product direction or core interaction behavior;
- introduce a large visual or architectural rewrite.

Small fixes, tests, documentation improvements, and narrowly scoped accessibility improvements can usually go directly to a pull request.

## Development setup

Worklings requires macOS 14 or newer, the Godot 4.7 **.NET** build, the .NET 8 SDK, and Git. See the [repository README](README.md#use-from-the-repository) for clone and run instructions.

Build:

```bash
dotnet build godot/worklings
```

Run the behavioral checks — headless probes, each diffed against its stored reference:

```bash
scripts/godot-probe
```

Set `GODOT` if the Godot binary is not at `/Applications/Godot.app`.

## Design boundaries

Contributions should preserve these boundaries:

- Worklings is a pet first, not a productivity score or surveillance tool.
- Pet simulation belongs in the rules code under `godot/worklings/core/` (pet, combat, progression); scenes and UI should not duplicate Pet Brain rules.
- Activity integrations emit provider-neutral events and must not feed prompts, source code, keystrokes, or screen contents into the Pet Brain.
- State remains local by default, with explicit schema versions and tested migrations.
- The desktop companion must remain controllable, non-obstructive, keyboard accessible where applicable, and respectful of Reduce Motion.
- Creature art and animation remain presentation concerns rather than species-specific branches in the core simulation.

Read [the architecture](docs/engineering/architecture.md), [product brief](docs/product-brief.md), and [interaction model](docs/design/interaction.md) before changing those areas.

## Making a change

1. Fork the repository and branch from an up-to-date `main`.
2. Keep the branch focused on one user-visible or architectural outcome.
3. Add or update behavioral checks for domain, persistence, placement, or presentation changes.
4. Manually verify window, input, and UI behavior that the probes cannot cover.
5. Update relevant documentation and `docs/changelog.md`.
6. Rebase or merge the latest `main` if the branch has become difficult to review.

External contributors may use their normal Git workflow. Maintainers and repository automation follow [the project Git workflow](docs/process/git-workflow.md), including the `scripts/committer` safeguards.

## Pull request acceptance

A pull request is ready for review when it:

- explains the user problem and the intended outcome;
- describes the chosen approach and material tradeoffs;
- builds with `dotnet build godot/worklings`;
- passes `scripts/godot-probe`;
- includes relevant automated checks and manual verification notes;
- preserves existing saves or includes an explicit, tested migration;
- addresses privacy, permissions, accessibility, and Reduce Motion when relevant;
- updates `docs/changelog.md` and affected documentation;
- contains screenshots or a short recording for visible interface changes;
- stays small enough to review as one coherent change.

Meeting this checklist does not guarantee merge. Maintainers may decline changes that conflict with the product direction, create disproportionate maintenance cost, or broaden privacy and permission scope without a compelling user benefit. Review feedback should explain those tradeoffs clearly.

Pull requests are normally squash merged into `main`. A maintainer may ask for a large pull request to be divided into independently reviewable changes.

## Reporting problems

Use the bug report template for reproducible defects and the feature request template for product proposals. Report security issues privately according to [the security policy](SECURITY.md). Community participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Licensing

By submitting a contribution, you agree that it may be distributed under the repository's [Apache License 2.0](LICENSE). Unless stated otherwise alongside a file, this includes contributed first-party visual assets.
