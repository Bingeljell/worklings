# Rendering Engine Fork — SceneKit, RealityKit, or Godot

> Evolving doc, not a frozen spec — see [docs/README](../README.md).

## Status

**OPEN. Not decided.** Recorded 2026-09-01 so the choice gets made deliberately rather
than by drift. Nothing here overrides the current build; SceneKit is what ships today.

This sits under [Cross-platform architecture](cross-platform-architecture.md), which
already names Godot as the first non-macOS candidate and scopes a disposable spike. What
is new is that three things landed at once and turned a Phase-3 question into a now
question.

## What forced it

1. **The dungeon went live 3D** (2026-09-01, see [dungeons](../design/dungeons.md)'s
   "Rendering approach"). Actors are real geometry, not baked sprites. That unblocks a
   large body of *engine-specific* effects work — per-character shaders (the Ram's
   electricity, the Flicker's glitching, the Pangolin's rune glow), particle systems,
   impact frames.
2. **SceneKit went into soft deprecation at WWDC 2025** — critical bug fixes only, no new
   features or optimisations, with RealityKit named as the migration path. This is now a
   stated Apple position, not an inference from the release cadence.
3. **Windows is a firm want**, Linux a maybe. macOS-first is a consequence of the
   developer's machine, not a product decision. **The menubar is not sacred** (2026-09-01)
   — it is a macOS-only affordance and the app's controls may live elsewhere on other
   platforms, which removes the strongest argument for staying with a native Apple UI
   host. Recent Godot has a cross-platform status-indicator (tray icon) API that would
   cover the control surface; verify the version in the spike.

## The core asymmetry

**Game logic already carries. Rendering does not.**

`CompanionCore` is a pure deterministic module with no rendering dependency — combat
resolution, progression, the pet brain, save data.

**Correction (2026-09-01): "carries" means architecturally, not literally, and the
difference is expensive.** `CompanionCore` is Swift. It carries as *code* to RealityKit,
which is also Swift. It does **not** carry to Godot, which runs GDScript or C# — there it
means a rewrite, or exposing the module as a C-compatible shared library through
GDExtension, which is exotic and multiplies build complexity across three platforms.
This is shipped, tested logic, and it is the largest single number in the Godot column.
An earlier version of this doc claimed the core was engine-agnostic "under any option
here"; that was wrong and understated the cost of switching.

Rendering carries **nothing**. Not one line of `DungeonStage3D.swift`,
`DungeonStageCameraTool.swift`, or any future effects layer survives a move to Godot.
There is no partial credit and no abstraction that would create any.

What *does* carry, and is worth stating because it is more than it looks:

- **The assets.** Blender models, rigs, actions, the baked floor tile. Godot reads glTF
  natively — better support than SceneKit, which needs a third-party library.
- **The numbers.** The locked Cache Warren camera (az 59.7° / el 39.7° / r 27.95, 32°
  vertical FOV), slot positions, floor height. These are data, not code.
- **The design knowledge.** Whether the angle reads, whether the combat feels weighty,
  what the room needs. This is the expensive knowledge and it is engine-independent.

So the question is not "will the work be wasted" — most of the *knowledge* survives
either way. It is "how much engine-specific code do we write before choosing".

## The timing argument

Everything built so far is cheap to abandon: room geometry, a tiled floor material, the
actor seam, the camera tool. Roughly 1,200 lines, most of it a dev tool.

The next phase is not cheap. Effects code is tuned by eye over many iterations — that is
its whole nature — and it is the most engine-specific code in any game. **We are standing
exactly at the line where throwaway work starts.** That is the argument for deciding now
rather than after.

## The reframe: this is not a dungeon question

The tempting version is "use Godot for the dungeon, keep the Mac app for everything
else." That is probably not available. Godot expects to own its window and run loop;
embedding it inside a SwiftUI menubar app means either a separate process with an IPC
boundary, or fighting both frameworks.

So the honest framing is: **does Worklings become a Godot application?** That question
reaches the menubar host, the activity adapters and git watchers, the character screen,
the inventory UI — a shipped alpha.9 product, not a prototype.

That is a much larger decision than the dungeon, and it is why this is a fork worth
writing down rather than a preference to act on.

## What "a Godot app" actually means

Not a runtime users install. Godot exports a **native executable per platform** — a
`.app` bundle on macOS, an `.exe` on Windows, an ELF binary on Linux — with the engine
linked into the binary. It is a native app built with a different toolchain, not a game
running inside a shipped engine.

Costs: roughly 30–70 MB of engine in the binary, and the loss of Apple-native affordances
(menubar extra, native menus, system accessibility) unless platform glue is written.

## The options

| | Windows/Linux | visionOS | Editor & tooling | Cost from here |
| --- | --- | --- | --- | --- |
| **SceneKit** (status quo) | No — full rewrite | No | Weakest. Xcode's `.scn` editor is minimal; the Dungeon Stage Camera Tool exists *because* there is no usable editor | Lowest today, but every effect built is throwaway if we ever leave Apple |
| **RealityKit** | No — full rewrite | Yes, comparatively cheap | Reality Composer Pro is real tooling; USD is native, and it can cut a combined animation into separate clips | Migration from SceneKit is real (ECS architecture); assets carry |
| **Godot** | Yes, one codebase | No | Strongest. Full visual 3D scene editor, particle and shader editors, live preview | Highest — reaches the whole app, and `CompanionCore` is a rewrite (Swift → GDScript/C#) |

**The trade, sharpened:** Godot costs a logic rewrite and buys three platforms plus a
real editor. RealityKit keeps the Swift logic and buys visionOS, but never Windows.

**RealityKit and Windows are mutually exclusive.** Both are Apple-only. If Windows is a
firm requirement, RealityKit rules itself out on those terms regardless of SceneKit's
deprecation, and the fork is really SceneKit-for-now versus Godot.

## The editor point, stated plainly

Godot's advantage is not only portability. The queued work — assembling room kit pieces,
placing lights, tuning particles, iterating on effect timing — is exactly what a visual
scene editor is for. The current plan assembles rooms in Xcode's `.scn` editor, which is
minimal and neglected.

Sharpest evidence: the **Dungeon Stage Camera Tool is ~970 lines of Swift that
reimplements what a game engine editor provides out of the box** — orbit, framing, actor
placement, live preview. The planned "test impact" button is more of the same. We have
been hand-building editor tooling to compensate for a framework that has none.

That is a real, ongoing tax, separate from the platform question.

## Audience data (added 2026-09-01)

The instinct "casual desktop-pet and MMO players are Windows-first" is right about
gaming but is the wrong frame for this product, and the right frame argues *harder* for
cross-platform.

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Steam hardware survey (gamers), Jul 2026 | 93.7% | 2.3% | 4.0% |
| Stack Overflow 2025, professional use | 49.5% | 32.9% | 27.7% |

Worklings' hook is *developer activity* — commits, Claude Code, Codex feeding pet
progression. That audience is roughly a third macOS, not two percent. This does not
rescue Mac-only; it condemns it harder, since Mac-only excludes about two thirds of the
actual audience.

**The consequence worth pricing in: Linux is not a rounding error here.** At ~28% of
developers it is nearly level with macOS, where under a gaming frame it would be 4% and
ignorable. That promotes Linux from "maybe" to a real target, and it is an argument for
Godot specifically — the only option on this table that reaches Linux at all.

(Developer figures are multi-select and sum past 100; read them as reach, not share.)

## On the cost of rewriting (corrected 2026-09-01)

An earlier version of this doc implied the rewrite was a year-scale risk. That was a
human-team estimate applied to a case that does not resemble one, and it was wrong.

- **`CompanionCore` is the best case for a port**: pure, deterministic, no platform APIs,
  and covered by a check suite that acts as a correctness oracle. Port, run the checks,
  know whether the logic held. Days, not months.
- **The real asymmetry is that logic has an oracle and UI does not.** Checks confirm the
  combat maths survived; nothing confirms the character screen feels right or the pet
  moves correctly on the desktop. That work is taste-driven iteration gated on human
  judgement, and speed does not remove that bottleneck.
- **Activity adapters, git watchers and hook integration must be written for Windows and
  Linux under *any* option.** That is a cross-platform cost, not a Godot cost, and should
  not be charged to the engine decision.

**Timing:** rewriting now is cheaper than rewriting later — the codebase only grows, and
the effects layer is precisely the expensive engine-specific work still ahead.

## Recommendation (not a decision)

**Make the Godot spike be the dungeon vertical slice**, rather than running them in
sequence.

The cross-platform doc already scopes a disposable Godot spike as Phase 4. The dungeon
vertical slice — room, one actor, one combat round, impact frames — is a near-perfect
spike: it exercises 3D rendering, animation import, particles, shaders, and the fixed
camera all at once, and it is the thing we want built anyway.

- If Godot handles it *and* the transparent always-on-top pet window works on Windows,
  we have built the real thing and answered the platform question together.
- If it does not, we have lost days rather than months, and Apple-only becomes the
  default — at which point RealityKit, not SceneKit, is the conversation.

The alternative — build the effects layer in SceneKit first — spends the most
engine-specific effort we will spend on the framework least likely to be the destination.

**Sequencing, if Godot is chosen:** spend a day on an asset probe *before* porting any
logic — load a character's glTF, put it on a floor at the locked camera, play a walk
action, fire one particle burst. Not because the port is expensive, but because a broken
rig or animation import is something to discover before the logic move rather than after.
Godot reads glTF natively so this should be clean; the USDZ import was expected to be
clean too and cost an afternoon over a Z-up axis mismatch.

## What is not in question

- `CompanionCore` stays pure and free of rendering dependencies, and should get *more*
  disciplined, not less, while the fork is open — but see the correction above: purity
  makes it portable in *design*, not in *language*. Under Godot it is a rewrite.
- Blender stays the authoring tool under every option.
- The desktop pet remains a 2D bake ([bake-spec](../design/bake-spec.md)); the character
  screen and dungeon are the 3D modes.

## Decision gate

Answer before committing either way:

1. Is Windows a real ship target with a date, or an aspiration? This single answer
   collapses most of the table.
2. Can Godot deliver a transparent, always-on-top, click-through companion window on
   **Windows specifically**? Researched 2026-09-01 and it looks well-trodden, but it is
   still unproven *for this app*:
   - Godot has it built in — borderless + transparent + always-on-top in project
     settings, per-pixel transparency in rendering, and
     `DisplayServer.window_set_mouse_passthrough(region)` for the clickable polygon.
     Events outside the region pass through; on Windows that area is not even drawn,
     which is what a pet wants. There is a published Godot-4 desktop-pet tutorial.
   - Native Win32 does it with `WS_EX_LAYERED` + `WS_EX_TRANSPARENT`. Note `WS_EX_LAYERED`
     alone only makes pixels *look* transparent — Windows still hit-tests them.
   - **Known wart to test, not assume:** godotengine/godot#91588 — setting a borderless
     window to always-on-top *at runtime* can make it non-interactive with all clicks
     falling through. Workaround is toggling visibility off and on.
3. What is the honest cost of moving the menubar host, activity adapters, and character
   screen to Godot — or of running two processes?
4. Does the dungeon actually feel good? An engine choice made before knowing whether the
   design works is a choice made on the wrong axis.

## Sources

- [SceneKit deprecation and RealityKit migration (WWDC 2025)](https://dev.to/arshtechpro/wwdc-2025-scenekit-deprecation-and-realitykit-migration-a-comprehensive-guide-for-ios-developers-o26)
- [USDZ cannot store multiple animations](https://developer.apple.com/forums/thread/650515)
- [Animation-only USDZ pattern for SceneKit](https://developer.apple.com/forums/thread/797061)
- [GLTFKit2](https://github.com/warrenm/GLTFKit2) — third-party glTF for SceneKit
- [Godot feature list](https://docs.godotengine.org/en/stable/about/list_of_features.html)
