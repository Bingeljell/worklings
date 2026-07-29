# Activity adapters

Adapters are the "sending half" of the [activity inbox](architecture.md#the-activity-inbox): small scripts that translate a dev tool's own lifecycle signals into content-free events the pet reacts to. The app never changes — an adapter only ever drops a JSON file into the inbox spool directory, exactly as `scripts/emit-activity-event` demonstrates.

Two adapters ship today, both in `scripts/adapters/`:

| Adapter | Tool | Source id | Mechanism |
| --- | --- | --- | --- |
| `worklings-claude-code-activity-hook` | Claude Code | `claude-code` | settings.json lifecycle hooks |
| `worklings-codex-activity-hook` | Codex CLI | `codex` | `[hooks]` lifecycle in `hooks.json` / `config.toml` |

Both are self-locating: each finds `scripts/emit-activity-event` beside it, so it works no matter what directory the tool invokes it from. Neither reads the tool's payload for content — see [Privacy](#privacy).

The names are deliberately Worklings-namespaced (`worklings-…-activity-hook`). The in-app connector decides which hooks are its own by the adapter's file name — so that a moved or reinstalled app bundle still recognizes and cleans up the hooks it wrote, even though its absolute path changed — and a distinctive name keeps that from ever matching an unrelated executable that happens to share a generic stem.

## Prerequisites

1. The Worklings app is running.
2. The tool is connected — via **Connect Claude Code** / **Connect Codex** in the paw menu, or the manual wiring below. Connecting is itself the opt-in; there is no separate global switch. The inbox monitor always drains the spool so files never accumulate, and delivers every event it finds to the pet.

Everything below is the **manual/developer path** — you can wire your own tool configs by hand. In the app, **Connect Claude Code** / **Connect Codex** in the paw menu writes equivalent wiring for you (parsing the existing config, backing it up, merging without disturbing your other keys or hooks, and offering a clean disconnect), so a user need never edit a config file. See [Privacy and permissions](architecture.md#privacy-and-permissions) for why an explicit, reversible config-writing convenience fits the principle.

The app's wiring differs from these hand-written snippets in two safe ways: it points at the adapters **bundled inside the app** (`Worklings.app/Contents/Resources/adapters/…`) rather than a repo checkout, and it writes each path in a shell-safe form — Claude Code as a separate `command` + `args` (no shell, so spaces and metacharacters in the path are never interpreted), Codex as a single-quoted shell command. The snippets below use those same safe forms so they hold up when `ABS` contains a space.

## Claude Code

Claude Code exposes a full lifecycle through [hooks](https://code.claude.com/docs/en/hooks). Each hook runs a shell command; we map four events (a fifth is optional):

| Hook event | Fires when | Inbox kind | Pet effect |
| --- | --- | --- | --- |
| `SessionStart` | a session begins or resumes | `workStarted` | starts a work block |
| `Stop` | Claude finishes a response turn | `taskCompleted` | celebrates, grants XP |
| `Notification` | Claude needs input or permission | `awaitingInput` | waits on you |
| `SessionEnd` | a session ends | `workEnded` | ends the block, grants focus XP |
| `StopFailure` | a turn ends on an API error | `taskFailed` | shared setback *(optional; newer Claude Code only)* |

Add to `~/.claude/settings.json`, replacing `ABS` with the absolute path to this repo:

```json
{
  "hooks": {
    "SessionStart": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/worklings-claude-code-activity-hook", "args": ["workStarted"] } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/worklings-claude-code-activity-hook", "args": ["taskCompleted"] } ] }
    ],
    "Notification": [
      { "matcher": "permission_prompt|idle_prompt|elicitation_dialog|agent_needs_input", "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/worklings-claude-code-activity-hook", "args": ["awaitingInput"] } ] }
    ],
    "SessionEnd": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/worklings-claude-code-activity-hook", "args": ["workEnded"] } ] }
    ]
  }
}
```

### A note on `Stop` frequency

`Stop` fires at the end of **every** assistant turn, not once per "task," so a chatty session emits many `taskCompleted` events. This is bounded by the per-source daily cap (the pet cannot farm unlimited XP from it), but it may still feel busy. If it does, drop the `Stop` mapping and rely on `SessionStart`/`SessionEnd` for the work-block XP. This is a tuning decision to settle during manual testing, not a correctness issue.

## Codex

Codex exposes a full lifecycle through its [`[hooks]` system](https://learn.chatgpt.com/docs/hooks) — the same shape as Claude Code, and it delivers the event JSON on **stdin**, which `worklings-codex-activity-hook` drains and ignores. We map three:

| Codex hook | Inbox kind | Pet effect |
| --- | --- | --- |
| `SessionStart` | `workStarted` | starts a work block |
| `Stop` | `taskCompleted` | celebrates a finished turn, grants XP |
| `SessionEnd` | `workEnded` | ends the block, grants focus XP |

Codex has no documented notification/"needs input" event, so `awaitingInput` is left unmapped.

Add to `~/.codex/config.toml` (or `~/.codex/hooks.json`), replacing `ABS` with the absolute path to this repo:

```toml
[[hooks.SessionStart]]
[[hooks.SessionStart.hooks]]
type = "command"
command = "'ABS/scripts/adapters/worklings-codex-activity-hook' workStarted"

[[hooks.Stop]]
[[hooks.Stop.hooks]]
type = "command"
command = "'ABS/scripts/adapters/worklings-codex-activity-hook' taskCompleted"

[[hooks.SessionEnd]]
[[hooks.SessionEnd.hooks]]
type = "command"
command = "'ABS/scripts/adapters/worklings-codex-activity-hook' workEnded"
```

`[hooks]` is TOML array-of-tables, so these **append** cleanly alongside anything already in your Codex config — including an existing `notify` program (e.g. a Computer Use client). Hooks and `notify` are independent systems, so there is no single-slot collision to work around. Codex treats a hook exit code of `2` as "block the turn," so `worklings-codex-activity-hook` always exits `0` and can never disrupt the agent. It also prints `{}` on stdout: Codex's `Stop` hook expects JSON on a `0` exit (empty output is invalid there), and an empty JSON object is a valid, content-free success payload for every event.

> The manual snippet above is the developer path. In a normal install, **Connect Codex** in the paw menu writes this wiring for you — pointing at the bundled adapter, backing up your existing config, and offering a clean disconnect — so you never edit a config file.

## Privacy

The event contract has no field for content, so the privacy promise is structural, and the adapters reinforce it:

- **`worklings-claude-code-activity-hook`** drains and discards the hook's stdin without parsing it, then emits only the fixed kind named on its command line.
- **`worklings-codex-activity-hook`** does the same: Codex delivers the event JSON (which carries `last_assistant_message`, `transcript_path`, `cwd` — all real content) on stdin, and the adapter drains and discards it without parsing, emitting only the fixed kind.

An adapter physically cannot hand the pet a prompt, a file path, or a diff — only *what happened* (a kind), *from which tool* (a source id), and *when* (a timestamp). Reserved source ids (`system`, `manual`, `simulated`) are rejected at the boundary, so an adapter cannot impersonate an internal or self-reported signal.

## Verifying a connection

With the app running and the tool connected:

- **Claude Code:** start a session — the pet should react (`workStarted`). Watch the inbox drain live if you like:
  `ls ~/Library/Application\ Support/Worklings/inbox`
- **Codex:** start a session (`workStarted`) or finish a turn — the pet should celebrate (`taskCompleted`).

If the file appears then vanishes but the pet doesn't react, the event was read and rejected; the reason is logged (filter Console.app for "Worklings discarded inbox file").

You can always exercise an adapter by hand without the tool:

```bash
scripts/adapters/worklings-claude-code-activity-hook taskCompleted </dev/null
scripts/adapters/worklings-codex-activity-hook taskCompleted </dev/null
```

## Disconnecting and removing Worklings

The paw menu owns its wiring in both directions. Each **Connect** item toggles: a
connected tool shows a checkmark and clicking it disconnects. **Disconnect All
Tools** removes every Worklings hook from both Claude Code and Codex in one step
(each config is backed up first) — use it before you move or delete the app so no
stale hooks are left behind.

Because a hook command points inside `Worklings.app`, **moving** the app makes the
menu show *"Reconnect … — adapter moved"*: the wiring is still recognized as ours
(ownership is the adapter file name, not its path), but it points at a location
that no longer exists, so one click repoints it at the app's new spot.

If the app is **dragged to the Trash without disconnecting first**, its hooks stay
in the tool configs pointing at a file that is gone. This is **not** silent: the
command no longer exists, so the tool that tries to run it reports an error each
time the hook fires — Codex's shell command exits `127` ("No such file or
directory"), and Claude Code logs a non-blocking launch failure. Neither bricks the
tool, but the errors persist until the entries are removed, so the tidy path is to
run **Disconnect All Tools** *before* removing the app. To clean up afterwards,
reinstall and use Disconnect All Tools, or remove the `worklings-…-activity-hook`
entries from `~/.claude/settings.json` / `~/.codex/hooks.json` by hand.

> A future change could make a deleted-app hook degrade to a silent no-op by
> guarding the written command (`[ -x <adapter> ] && … `); see
> `audit_followups.md`. Until then, disconnect before uninstalling.

## Follow-ups

- **In-app connector — shipped.** The app now **bundles these adapters inside its own app bundle** (`Contents/Resources/adapters/`) and **writes the tool configs itself** from an explicit in-app action ("Connect Claude Code", "Connect Codex") — backing up the existing file, merging without clobbering the user's other keys or hooks, and offering a clean disconnect that removes only its own hooks. This retired both the copy-paste fragility and the "a DMG user has no repo checkout" problem, and it fits the reframed [privacy principle](architecture.md#privacy-and-permissions): a config-writing convenience is fine when it is explicit, disclosed, backed up, and reversible. The manual snippets above remain for hand-wiring a repo checkout.
- **`taskFailed`.** Signalled by Claude Code's `StopFailure` hook (map it to `taskFailed`); Codex has no clean failure event yet. Left thin on purpose.
- **`awaitingInput` for Codex.** No documented Codex notification event today; revisit if one appears.
- **Local git.** Shipped as an **in-app source** (not an adapter — the app watches connected repos directly), see the [progression plan](progression.md#planned-sources).
