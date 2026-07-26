# Activity adapters

Adapters are the "sending half" of the [activity inbox](architecture.md#the-activity-inbox): small scripts that translate a dev tool's own lifecycle signals into content-free events the pet reacts to. The app never changes — an adapter only ever drops a JSON file into the inbox spool directory, exactly as `scripts/emit-activity-event` demonstrates.

Two adapters ship today, both in `scripts/adapters/`:

| Adapter | Tool | Source id | Mechanism |
| --- | --- | --- | --- |
| `claude-code-hook` | Claude Code | `claude-code` | settings.json lifecycle hooks |
| `codex-hook` | Codex CLI | `codex` | `[hooks]` lifecycle in `hooks.json` / `config.toml` |

Both are self-locating: each finds `scripts/emit-activity-event` beside it, so it works no matter what directory the tool invokes it from. Neither reads the tool's payload for content — see [Privacy](#privacy).

## Prerequisites

1. The Worklings app is running.
2. **"Accept Work Tool Events"** is enabled in the paw menu (off by default). Without it, dropped events are ignored, not queued indefinitely — the monitor only drains while enabled.

Everything below is the **interim/developer path** — you edit your own tool configs by hand. The committed direction is that the **app writes this wiring itself** from an explicit in-app action (with a backup and a clean disconnect), so a user never edits a config file; the manual snippets here are what that writer will produce. See [follow-ups](#follow-ups) for the connector and [Privacy and permissions](architecture.md#privacy-and-permissions) for why an explicit, reversible config-writing convenience fits the principle.

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
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/claude-code-hook workStarted" } ] }
    ],
    "Stop": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/claude-code-hook taskCompleted" } ] }
    ],
    "Notification": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/claude-code-hook awaitingInput" } ] }
    ],
    "SessionEnd": [
      { "hooks": [ { "type": "command", "command": "ABS/scripts/adapters/claude-code-hook workEnded" } ] }
    ]
  }
}
```

### A note on `Stop` frequency

`Stop` fires at the end of **every** assistant turn, not once per "task," so a chatty session emits many `taskCompleted` events. This is bounded by the per-source daily cap (the pet cannot farm unlimited XP from it), but it may still feel busy. If it does, drop the `Stop` mapping and rely on `SessionStart`/`SessionEnd` for the work-block XP. This is a tuning decision to settle during manual testing, not a correctness issue.

## Codex

Codex exposes a full lifecycle through its [`[hooks]` system](https://learn.chatgpt.com/docs/hooks) — the same shape as Claude Code, and it delivers the event JSON on **stdin**, which `codex-hook` drains and ignores. We map three:

| Codex hook | Inbox kind | Pet effect |
| --- | --- | --- |
| `SessionStart` | `workStarted` | starts a work block |
| `Stop` | the agent finishes a turn | `taskCompleted` |
| `SessionEnd` | `workEnded` | ends the block, grants focus XP |

Codex has no documented notification/"needs input" event, so `awaitingInput` is left unmapped.

Add to `~/.codex/config.toml` (or `~/.codex/hooks.json`), replacing `ABS` with the absolute path to this repo:

```toml
[[hooks.SessionStart]]
[[hooks.SessionStart.hooks]]
type = "command"
command = "ABS/scripts/adapters/codex-hook workStarted"

[[hooks.Stop]]
[[hooks.Stop.hooks]]
type = "command"
command = "ABS/scripts/adapters/codex-hook taskCompleted"

[[hooks.SessionEnd]]
[[hooks.SessionEnd.hooks]]
type = "command"
command = "ABS/scripts/adapters/codex-hook workEnded"
```

`[hooks]` is TOML array-of-tables, so these **append** cleanly alongside anything already in your Codex config — including an existing `notify` program (e.g. a Computer Use client). Hooks and `notify` are independent systems, so there is no single-slot collision to work around. Codex treats a hook exit code of `2` as "block the turn," so `codex-hook` always exits `0` and can never disrupt the agent.

> The manual snippet above is the interim/developer path. The intended end state is that the **app writes this wiring itself** (with a backup and a clean disconnect) so a user never edits a config file — see [follow-ups](#follow-ups).

## Privacy

The event contract has no field for content, so the privacy promise is structural, and the adapters reinforce it:

- **`claude-code-hook`** drains and discards the hook's stdin without parsing it, then emits only the fixed kind named on its command line.
- **`codex-hook`** does the same: Codex delivers the event JSON (which carries `last_assistant_message`, `transcript_path`, `cwd` — all real content) on stdin, and the adapter drains and discards it without parsing, emitting only the fixed kind.

An adapter physically cannot hand the pet a prompt, a file path, or a diff — only *what happened* (a kind), *from which tool* (a source id), and *when* (a timestamp). Reserved source ids (`system`, `manual`, `simulated`) are rejected at the boundary, so an adapter cannot impersonate an internal or self-reported signal.

## Verifying a connection

With the app running and "Accept Work Tool Events" enabled:

- **Claude Code:** start a session — the pet should react (`workStarted`). Watch the inbox drain live if you like:
  `ls ~/Library/Application\ Support/Worklings/inbox`
- **Codex:** start a session (`workStarted`) or finish a turn — the pet should celebrate (`taskCompleted`).

If the file appears then vanishes but the pet doesn't react, the event was read and rejected; the reason is logged (filter Console.app for "Worklings discarded inbox file").

You can always exercise an adapter by hand without the tool:

```bash
scripts/adapters/claude-code-hook taskCompleted </dev/null
scripts/adapters/codex-hook taskCompleted </dev/null
```

## Follow-ups

- **In-app connector — the committed direction.** The manual config snippets are the interim. The goal is that the app **bundles these adapters inside its own app bundle** and **writes the tool configs itself** from an explicit in-app action ("Connect Claude Code", "Connect Codex") — backing up the existing file and offering a clean disconnect that restores it. This retires both the copy-paste fragility and the "a DMG user has no repo checkout" problem in one move, and it fits the reframed [privacy principle](architecture.md#privacy-and-permissions): a config-writing convenience is fine when it is explicit, disclosed, backed up, and reversible. Because Codex `[hooks]` and Claude Code `settings.json` are both **append/merge-friendly**, the writer can add its block without clobbering the user's existing config.
- **`taskFailed`.** Signalled by Claude Code's `StopFailure` hook (map it to `taskFailed`); Codex has no clean failure event yet. Left thin on purpose.
- **`awaitingInput` for Codex.** No documented Codex notification event today; revisit if one appears.
- **Local git.** Shipped as an **in-app source** (not an adapter — the app watches connected repos directly), see the [progression plan](progression.md#planned-sources).
