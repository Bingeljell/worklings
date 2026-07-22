# Beta Distribution

## Purpose

This document defines how Worklings is assembled and published for early macOS testers. The initial goal is a repeatable experimental build, not a production-grade App Store or notarized release.

Generated application bundles, disk images, and checksums are release artifacts. They must not be committed to Git.

## Current release status

The packaging and verification scripts are fully renamed to Worklings. The current public prerelease is `v0.1.0-alpha.4` (build number 4), published through GitHub Releases for Apple Silicon, bringing the progression system (XP, levels, class, and class-weighted stat growth), the condition multiplier surfaced as a learning-rate line on the care card, the Condition/Stats care-card tabs, and the off-by-default activity inbox behind the "Accept Work Tool Events" toggle. `v0.1.0-alpha.3` (build number 3) was the prior release, bringing real activity awareness (dailyWake, presence-driven reactions, Log Work, Focus Session), pet renaming, and the persistent name pill; it remains available. `v0.1.0-alpha.2` was the first Worklings-branded DMG and remains available as an earlier release. The older `v0.1.0-alpha.1` prerelease was created before the rebrand and still contains a Build Companion app and filename; it remains as a historical artifact. Each subsequent public version must use a new tag and an increased build number rather than replacing an existing release.

The first Worklings-branded installation is a transition rather than an in-place app replacement: `Build Companion.app` and `Worklings.app` have different names and bundle identifiers. Quit Build Companion, install Worklings, launch it once, and verify that Pixel's state was copied forward. The old application can then be removed without deleting either Application Support directory. Later Worklings versions replace `Worklings.app` normally.

## Initial release scope

- **Release channel:** GitHub Releases.
- **Minimum system:** macOS 14 or newer.
- **Initial architecture:** Apple Silicon (`arm64`).
- **Bundle identifier:** `com.bingeljell.worklings`.
- **Signing:** ad-hoc signing with macOS `codesign`.
- **Notarization:** deferred until the experiment justifies an Apple Developer membership and certificate management.
- **Packaging dependencies:** Apple Command Line Tools and built-in macOS utilities only.

Source builds remain supported. Intel and universal release artifacts may be added after the first packaging flow is proven.

## Artifact contract

The packaging flow produces artifacts under the ignored `dist/` directory:

```text
dist/
└── <version>/
    ├── Worklings.app/
    ├── Worklings-<version>-macos-arm64.dmg
    └── Worklings-<version>-macos-arm64.dmg.sha256
```

The application bundle follows the standard macOS layout:

```text
Worklings.app/
└── Contents/
    ├── Info.plist
    ├── MacOS/
    │   └── Worklings
    └── Resources/
        ├── worklings-wildkin-spritesheet.png
        ├── worklings-elemental-spritesheet.png
        ├── worklings-relicborn-spritesheet.png
        └── worklings-smoke-effects.png
```

The disk image contains the application and a shortcut to `/Applications` so the user can install it by dragging the app.

## Versioning

Use semantic versions with prerelease labels while the product is experimental:

```text
0.1.0-alpha.1
0.1.0-alpha.2
0.1.0-beta.1
```

Git tags add a leading `v`, for example `v0.1.0-alpha.1`. The version in the app metadata, disk-image filename, checksum filename, Git tag, and GitHub Release must agree.

The bundle build number is a positive integer supplied separately from the user-facing version. It must increase for every published build.

## Trust and Gatekeeper

Ad-hoc signing lets the packaging checks verify that the app has not changed since it was assembled. It does not identify the publisher to Apple and does not replace Developer ID signing or notarization.

Consequently, a downloaded alpha may be blocked on first launch. Testers should use Finder's **Open** command from the app's context menu and confirm the prompt, or use **System Settings > Privacy & Security > Open Anyway**. The project must not instruct users to disable Gatekeeper globally.

A public beta intended for broad non-technical use should eventually be signed with a Developer ID Application certificate and notarized by Apple.

## Build and verification contract

Every release candidate must:

1. Come from a clean commit on `main` with a matching version tag.
2. Pass the full Swift build and `CompanionCoreChecks` suite.
3. Build the executable in release configuration for the declared architecture.
4. Contain valid `Info.plist` version, identifier, executable, and minimum-system metadata.
5. Pass strict `codesign` verification after ad-hoc signing.
6. Produce a DMG that passes `hdiutil verify`.
7. Mount successfully and contain both the app and Applications shortcut.
8. Contain the Wildkin, Elemental, Relicborn, and shared smoke-effect sprite sheets in the app's Resources directory.
9. Produce a SHA-256 checksum beside the DMG.

Application launch remains a manual smoke test because launching a foreground macOS application is not reliable in every automated or remote environment.

Build the next Worklings application bundle with:

```bash
scripts/build_app_bundle --version 0.1.0-alpha.3 --build-number 3
```

The builder refuses to replace an existing application bundle. Choose a new output directory for an isolated test, or deliberately remove an obsolete generated artifact before rebuilding it.

Package the application bundle as a DMG with:

```bash
scripts/build_dmg --version 0.1.0-alpha.3
```

The DMG builder validates the existing app's version, architecture, and signature before packaging it. It creates a compressed read-only image, verifies the image, and writes a SHA-256 checksum beside it.

Verify the complete release artifact with:

```bash
scripts/verify_release --version 0.1.0-alpha.3
```

The verifier confirms the external checksum and DMG integrity, mounts the image read-only, checks the Applications shortcut, and validates the packaged app's identifier, version, build number, minimum system, architecture, and code signature.

## Release flow

1. Merge the packaging or product PR into `main`.
2. Update local `main` with a fast-forward-only pull.
3. Run the complete verification suite.
4. Create the versioned app bundle and DMG.
5. Perform the manual launch and installation smoke test.
6. Create an annotated version tag on the verified commit.
7. Push the tag and create a GitHub prerelease.
8. Attach the DMG and checksum to the GitHub Release.
9. Confirm the public download and installation instructions.

Tagging and publishing are deliberate release actions. Packaging scripts must not create Git tags, push commits, or publish GitHub Releases automatically.

## Deferred production hardening

- Developer ID signing and Apple notarization.
- Universal or separately published Intel builds.
- A project-specific icon and final artwork.
- Automatic release creation after protected CI checks.
- Update checks or an in-app updater.
- Reproducible builds across multiple machines and Xcode versions.
