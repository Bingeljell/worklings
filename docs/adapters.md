# Activity adapters

Adapters are the "sending half" of the [activity inbox](architecture.md#the-activity-inbox): small scripts that translate a dev tool's own lifecycle signals into content-free events the pet reacts to. The app never changes — an adapter only ever drops a JSON file into the inbox spool directory, exactly as `scripts/emit-activity-event` demonstrates.

Two adapters ship today, both in `scripts/adapters/`:

| Adapter | Tool | Source id | Mechanism |
| --- | --- | --- | --- |
| `claude-code-hook` | Claude Code | `claude-code` | settings.json lifecycle hooks |
| `codex-notify` | Codex CLI | `codex` | `notify` program in `config.toml` |

Both are self-locating: each finds `scripts/emit-activity-event` beside it, so it works no matter what directory the tool invokes it from. Neither reads the tool's payload for content — see [Privacy](#privacy).

## Prerequisites

1. The Worklings app is running.
2. **"Accept Work Tool Events"** is enabled in the paw menu (off by default). Without it, dropped events are ignored, not queued indefinitely — the monitor only drains while enabled.

Everything below is opt-in: you edit your own tool configs. Nothing in this repo modifies files in your home directory for you.

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

Codex's documented lifecycle signal is its [`notify` program](https://learn.chatgpt.com/docs/config-file/config-advanced), which currently emits **only** `agent-turn-complete`. So the Codex adapter maps that one event to `taskCompleted`; the other kinds have no dependable Codex signal yet.

Add to `~/.codex/config.toml`, replacing `ABS`:

```toml
notify = ["ABS/scripts/adapters/codex-notify"]
```

Codex appends the event JSON as the final argument; the adapter matches only the event `type` and ignores everything else.

### Why not more Codex events?

Codex has a newer `[hooks]` system (`PreToolUse`, etc.), but its session-lifecycle events aren't exhaustively documented, and building on them risks silent breakage as Codex changes. Wiring `[hooks]` to approximate `workStarted`/`awaitingInput` is deliberately **deferred** until those signals are stable and documented. See [follow-ups](#follow-ups).

## Privacy

The event contract has no field for content, so the privacy promise is structural, and the adapters reinforce it:

- **`claude-code-hook`** drains and discards the hook's stdin without parsing it, then emits only the fixed kind named on its command line.
- **`codex-notify`** pattern-matches the event `type` only. The notify payload also carries `last-assistant-message` and `input-messages` (real content); the adapter never reads or forwards them.

An adapter physically cannot hand the pet a prompt, a file path, or a diff — only *what happened* (a kind), *from which tool* (a source id), and *when* (a timestamp). Reserved source ids (`system`, `manual`, `simulated`) are rejected at the boundary, so an adapter cannot impersonate an internal or self-reported signal.

## Verifying a connection

With the app running and "Accept Work Tool Events" enabled:

- **Claude Code:** start a session — the pet should react (`workStarted`). Watch the inbox drain live if you like:
  `ls ~/Library/Application\ Support/Worklings/inbox`
- **Codex:** finish a turn — the pet should celebrate (`taskCompleted`).

If the file appears then vanishes but the pet doesn't react, the event was read and rejected; the reason is logged (filter Console.app for "Worklings discarded inbox file").

You can always exercise an adapter by hand without the tool:

```bash
scripts/adapters/claude-code-hook taskCompleted </dev/null
scripts/adapters/codex-notify '{"type":"agent-turn-complete"}'
```

## Follow-ups

- **Distribution.** These adapters live in the repo; an end user who installed the DMG has no copy. Shipping the adapters with the app (or an installer) is out of scope for this pass — for now the source id and config snippets assume a local checkout.
- **Codex `[hooks]`.** Revisit for `workStarted`/`awaitingInput` once Codex documents stable lifecycle events.
- **`taskFailed`.** Neither tool signals failure cleanly today (Claude Code's `StopFailure` is version-dependent; Codex has none). Left thin on purpose.
- **Local git adapter.** A commit → `milestone` adapter is a natural third, per the [progression plan](progression.md#planned-sources).
