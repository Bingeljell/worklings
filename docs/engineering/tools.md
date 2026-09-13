# Tools

> Evolving doc, not a frozen spec — see [docs/README](../README.md).

Every tool in the repo, what it answers, and how to run it. Nothing here is part
of the shipped app: these are the things you reach for to check that something
works, to see what a change looks like, or to measure a cost before building on
it.

Two homes:

- **`godot/worklings/tools/`** — 37 scenes run by the Godot binary.
- **`scripts/`** — shell and Python helpers run from the repo root.

---

## Running a Godot tool

Nine of them have a **stored reference** and are diffed automatically:

```bash
scripts/godot-probe                  # every probe with a reference
scripts/godot-probe persistence      # just that one
scripts/godot-probe --record status  # re-record, only after verifying the output
```

That script builds the assembly first, because Godot runs the compiled DLL and a
stale build silently tests the previous version of the code.

Everything else is run directly, and read rather than diffed:

```bash
dotnet build godot/worklings                                            # first, always
Godot --headless --path godot/worklings res://tools/<name>.tscn         # prints only
Godot --path godot/worklings res://tools/<name>.tscn                    # needs a window
```

**Headless is not always an option.** Anything that renders, captures a frame, or
touches the GPU must run windowed — `--headless` uses a dummy renderer, so a
screenshot comes back empty and a GPU timing measures nothing. The GPU column
below says which. Tools taking arguments read them after a bare `--`.

---

## Port probes — diffed against a reference

These compare the C# port against output captured from the Swift original. A
reference committed next to the probe is what turns "verified once" into "stays
verified"; see [godot-port-status](godot-port-status.md) for how one is captured.

| tool | what it compares |
|---|---|
| `activity_probe` | `ActivityContext`'s reducer |
| `care_probe` | the care simulation |
| `connector_probe` | the hook merger |
| `inbox_probe` | the inbox's trust boundary |
| `observe_probe` | the activity half of the brain |
| `persistence_probe` | the save file, both the JSON on the wire and the state behind it |
| `placement_probe` | the pet window's placement math |
| `sources_probe` | the activity sources' pure decision logic |
| `status_probe` | condition and presentation |

## Port probes — print and read

Same purpose, no stored reference yet: run them and read the output.

| tool | what it compares |
|---|---|
| `bounded_draw_probe` | `SeededGenerator`'s bounded draw |
| `character_sheet_probe` | the character-sheet readout |
| `combat_rewards_probe` | the reward layer |
| `combatant_bridge_probe` | the `PetState` → `Combatant` bridge |
| `daily_tally_probe` | `DailyTally`'s same-day check |
| `delve_probe` | the delve chain |
| `items_probe` | the gear layer |
| `pet_state_probe` | `PetState` |
| `progression_probe` | the progression layer |
| `resolve_probe` | resolved strikes |
| `rng_probe` | `SeededGenerator` against captured values |
| `fight_probe` | runs whole encounters and prints the complete event log |
| `timing_probe` | where each attack's contact frame falls |
| `two_window_probe` | proves or disproves the two-window architecture |

## Live checks — drive a real system

These touch real files, real windows, or a real clock, which is the point: they
catch the half of a feature that unit-level comparison cannot reach.

| tool | GPU | what it drives |
|---|---|---|
| `audio_check` | — | loads every dungeon sound and reports it |
| `connect_check` | — | a real connect and disconnect against a real file on disk |
| `git_check` | — | the git watcher against a real repository |
| `presence_check` | — | the presence watcher against a driven idle clock |
| `hover_check` | yes | the hover summary over a stand-in window |
| `picker_check` | yes | the repository picker and its signal, with nobody clicking |

## Pictures — see it rather than infer it

All of these need a window.

| tool | what it renders |
|---|---|
| `stage_shot` | the dungeon stage to a PNG, without opening the editor |
| `fight_shot` | frames as a fight plays in the Cache Warren scene |
| `character_shot` | a frame of the character window's contents |
| `grey_shot` | any `.glb` from the locked dungeon angle — `--textured`, `--az=<deg>` |
| `ghost_trail_preview` | the motion trail as stills, to judge the effect before building it |
| `contact_sheet` | stitches stills into one side-by-side strip |
| `ghost_bake_probe` | *(measures, doesn't draw)* the cost of a skeleton-pose bake |

`grey_shot` and `contact_sheet` take arguments:

```bash
Godot --path godot/worklings res://tools/grey_shot.tscn -- <outdir> --textured --az=59.7 a.glb b.glb
Godot --headless --path godot/worklings res://tools/contact_sheet.tscn -- out.png a.png b.png
```

`grey_shot` loads a `.glb` at runtime rather than through the editor importer, so
candidate exports can be compared without being committed to the project first.

> `run_build` is a one-off that emitted `scenes/dungeon_stage.tscn` and still has
> a dead scratch path hard-coded. Kept for its history; don't reach for it.

---

## `scripts/`

**Day to day**

| script | what it does |
|---|---|
| `committer` | the commit helper — use it rather than raw `git add`/`commit` |
| `godot-probe` | runs the probes above and diffs each against its reference |
| `godot-pet` | runs the desktop pet window |
| `emit-activity-event` | drops one activity event into the inbox, the way a real adapter would |

**Release**

| script | what it does |
|---|---|
| `godot-export` | builds a real, runnable `Worklings.app` |
| `build_app_bundle` | `--version <v> --build-number <n>` |
| `build_dmg` | `--version <v>` |
| `verify_release` | `--version <v>` |

**Blender**

| script | what it does |
|---|---|
| `blender_export_character.py` | exports a rigged character to glTF for Godot |
| `blender_bake_kit_tile.py` | bakes a procedural surface into a seamlessly tileable texture pair |
| `blender_rpc.py` | talks to a running Blender over its `execute_code` RPC |

`blender_export_character.py` carries the hard-won rules in its module docstring
— weld before decimating, apply the decimate destructively, unhide before
selecting — and a per-character triangle budget in `TRI_BUDGETS`. Read it before
changing an export.

**Adapters** — `scripts/adapters/` holds the Claude Code and Codex activity
hooks that feed the inbox.
