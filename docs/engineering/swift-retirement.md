# Retiring the Swift App

## How the original Swift app left the repo once the Godot build had taken over

**Date:** 2026-09-24
**PR:** [#69](https://github.com/Bingeljell/worklings/pull/69)
**Tag:** `swift-final` — the last commit that still has the Swift code
**Outcome:** about 19,000 lines deleted; CI now checks the code that actually ships

---

## Why now

Worklings started as a Swift/SwiftUI app. The game moved to Godot and C# on
2026-09-01, and every release since alpha.11 has been built from Godot. The
Swift code stayed behind anyway, and it wasn't harmless:

- **CI was checking the wrong app.** The check on `main` ran `swift build` and
  the Swift tests. It went green on code nobody shipped, while the Godot code had
  no check at all.
- **It was clutter.** 70 Swift files to search past, plus docs describing an app
  that no longer existed.

Alpha.14 had just shipped and nothing half-built depended on the Swift code, so
this was the quiet moment to do it.

## Delete it, don't move it to a legacy folder

A `legacy/` folder is code nobody runs but everyone still has to search past.
Git already keeps every old version, so the Swift code was deleted and the last
commit that has it was tagged `swift-final`. To look at it:

```bash
git worktree add ../worklings-swift swift-final
```

## What was checked first

| Question | Answer |
| --- | --- |
| Does anything exist only in Swift? | No. The [port status doc](godot-port-status.md) says the old rules code (`CompanionCore`) and the app around it are rebuilt in C#, including the hook installer. The one gap is signing, deferred to beta on purpose. |
| Do the Godot checks pass? | Yes. 9 probes, about 6 seconds, including from a fresh clone with no Godot cache. |
| Do releases still need Swift? | No. Releases go `godot-export` → `build_dmg` → `verify_release`. Only the old `build_app_bundle` script used Swift. |
| Does `main`'s merge rule need changing? | No. It requires a check by job name, "Build and behavioral checks", and the new Godot job keeps that name. |

## What changed

1. **CI.** The same job now installs .NET 8 and Godot 4.7.2 (the .NET build) on
   a Mac runner, builds the project, and runs `scripts/godot-probe`. About a
   minute a run.
2. **Deleted:** `Sources/`, `Tests/`, `Package.swift`, `scripts/build_app_bundle`.
3. **Docs and templates** now say `dotnet build godot/worklings` and
   `scripts/godot-probe`. The design docs' tuning tables point at the C# files.

Left alone on purpose: about 40 "Ported from `Sources/…swift`" comments in the
C# code. They still say where something came from, and the tag keeps those
files findable.

## One rule that changed

**C# is now the source of truth.** Before, a probe's saved output could only be
recorded after checking it against the running Swift app. Now:

- **a ported probe** is checked against a capture from the `swift-final` tag;
- **new behaviour** is checked by hand before `scripts/godot-probe --record`.

Recording a bug as the expected output is exactly as easy as recording a fix.

## Still open

- **`architecture.md` still describes the Swift app.** It has an "out of date"
  note at the top until it's rewritten.
- **9 of the 18 probes have no saved output to compare against**, so CI protects
  only half of them. Capture the rest from the `swift-final` tag.
- **Three debug settings from the Swift app were never ported:** a faster idle
  timeout, a faster presence check, and sped-up needs. `WORKLINGS_SAVE` and
  `WORKLINGS_INBOX_DIR` still redirect the save and the inbox.
- **Swift leftovers on disk:** a local `.build/` folder (not in git) and the
  Swift lines in `.gitignore`.
