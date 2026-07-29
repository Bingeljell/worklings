# Audit follow-ups (connector / adapters)

Deferred findings from the activity-adapters audits, captured after `v0.1.0-alpha.5`
(they were **not** release blockers — the three prior audit rounds fixed everything
that could hang the app, brick a config, or wreck a git/Codex/Claude session).

**Status (2026-07-29, after a second review — now all addressed):** edit-race incl.
the backup-window refinement (#3); live-vs-stale hooks + Disconnect All + the
`.unknown` state (#2); informed consent now per-tool + dropping the Accept Work Tool
Events toggle (#4); Codex `{}` output (#5); and the drag-to-Trash sub-point of #2 —
the written command is now **guarded** so a deleted-app hook is a silent no-op.
**Blocked (recorded, not doable):** item 1 (hook naming) — no tool documents a name
field.

Guiding bar (unchanged): never hang the app, brick a system, or wreck a
git/Codex/Claude session.

---

## 1. Name the hooks we write — BLOCKED (no schema field exists)

**Observed:** In the Codex app's hook approval UI, our hooks show up as generic
`Hook 1`, `Hook 2`, … Every other Worklings-facing artifact is named; these
should be too.

**Investigated 2026-07-29 (docs for both tools):** neither Codex `[hooks]` nor
Claude Code `settings.json` documents a `name`/label field on a command-hook
entry. Claude's `/hooks` browser and Codex's approval UI identify hooks by source
file + command text only; Codex's "Hook N" is auto-numbering with no documented
override. So there is **no clean schema field to set** — this is not a quick win.

**If revisited:** options are (a) check whether Codex's `statusMessage` field
influences the approval-UI label (untested — verify empirically, it is documented
only as an in-progress spinner message), or (b) accept the auto-numbering. Do
**not** invent a `name` key the tools may reject. Whatever we do, the hook's
identity for ownership stays the adapter file name (see item 2), not a label.

---

## 2. [P1] Moving or deleting Worklings leaves broken hooks behind — DONE (2026-07-29)

**Problem:** Our hook command points *inside* `Worklings.app`. Moving or trashing
the app left Codex and Claude with a command pointing at a missing file, and
Worklings still reported that hook as **connected** purely from its file name.

**Done:**
- **Ownership and liveness are now separate.** `HookConfigMerger.ourHookExecutablePaths`
  reports the path of every hook of ours; `ToolConnector.connectionState()` returns
  `.notConnected` / `.live` / `.stale` — *ours and the path resolves* vs *ours but
  the adapter is gone*. The paw menu shows a live connection with a checkmark and a
  stale one as **"Reconnect … — adapter moved"**, so one click repoints it at the
  current adapter (ownership still matched by the namespaced file name, so a moved
  bundle is still recognized).
- **Explicit disconnect-all.** A **Disconnect All Tools** paw-menu item removes every
  Worklings hook from both tools in one step (each config backed up first), incl.
  stale entries. The clean pre-uninstall path. Documented in `adapters.md`.
- *Second-review refinement (2026-07-29):* a `.unknown` connection state was added
  for a config that exists but is unreadable or malformed. Previously that collapsed
  to `.notConnected`, so Disconnect All could skip a tool and report "nothing found"
  when it simply could not inspect the file. `.unknown` is now surfaced as an
  explicit cleanup failure (and in the menu as "… — can't read config").
- **Drag-to-Trash — DONE (guarded command, 2026-07-29).** *(An earlier note here
  wrongly claimed leftover hooks were already inert; in fact a deleted-app hook
  errored — Codex `127`, Claude a non-blocking launch failure. That is now actually
  fixed.)* The written command is **guarded** with an `[ -x <adapter> ]` test, so a
  missing adapter is a silent, valid no-op:
  - Codex: `if [ -x '<adapter>' ]; then '<adapter>' <kind>; else printf '{}'; fi`
  - Claude: `/bin/sh -c 'if [ -x "$1" ]; then exec "$1" "$2"; fi' sh <adapter> <kind>`
    (path passed as a quoted positional arg, so path-safety is kept even though
    Claude moves off pure exec form).

  Ownership/liveness matching was reworked to find the adapter path anywhere it can
  appear (the `command`, a single-quoted shell word, or an `args` element), keyed on
  the distinctive `worklings-…-activity-hook` basename — so old and guarded forms are
  both recognized and cleaned up. Two checks **run the generated commands with a
  missing adapter** and assert exit 0 / `{}` (the reviewer's 127 reproduction, now a
  regression test). This is the dotfile-tool convention (`nvm`'s `[ -s … ] &&` line):
  guard the injected line rather than rely on catching uninstall — which macOS gives
  no reliable hook for anyway (no drag-to-Trash event; a running app can't even be
  trashed). The consent dialog also now names "Disconnect All Tools before you delete
  Worklings," and onboarding will reiterate it ([[onboarding-experience]]).

---

## 3. [P1] A simultaneous configuration edit could be overwritten (TOCTOU) — DONE (2026-07-29)

**Problem:** `ToolConnector` read the config, prepared the merge, backed up
whatever existed, then wrote a version derived from the **earlier** read. If
Claude, Codex, another program, or the user edited the file during that window,
their live change was replaced (recoverable from backup, but still "in question").

**Done:** A `commit(transform:)` helper re-reads the config immediately before
writing and compares it to the bytes the merge was computed from. If they differ,
it recomputes the transform on the new contents and retries (bounded by
`maxWriteAttempts`), so a concurrent edit is merged rather than clobbered; after
the retries it throws `configChangedDuringWrite` rather than write stale data.

*Second-review refinement (2026-07-29):* the first cut compared **before** backing
up, leaving the backup step itself inside the check→write window — a mid-backup
edit could still be overwritten. Reordered so each attempt backs up, then does the
confirming re-read, then writes, with **nothing but the atomic rename between the
compare and the write** (a stale backup from a losing attempt is deleted before
retrying). A vanishing read→rename window remains (full closure needs file locking
the tools don't coordinate on).

---

## 4. [P2] Consent is explicit, but not yet fully informed — DONE (2026-07-29)

**Problem:** Clicking **Connect** immediately edited the external tool's config and
silently enabled event acceptance. Explicit, but the user wasn't told what happens.

**Done:** An informed-consent dialog appears before a tool's first connection, with
**Connect** / **Cancel** — declining writes nothing. It states which file changes
(full path, backed up, existing settings preserved), what Worklings receives (an
activity kind + source + time, never a prompt/diff/path/content), that everything
stays local, and how to disconnect.

*Second-review refinement (2026-07-29):* the acknowledgement is remembered **per
tool** (`toolConnectionConsentAcknowledged.<tool>`), not globally. Because each tool
edits a different file, approving Claude no longer suppresses Codex's own
"exact file being changed" disclosure — connecting Codex later still shows it once.

The **"Accept Work Tool Events" toggle was dropped entirely** (it was testing
scaffolding). Connecting a tool is now the opt-in — exactly how connecting a repo
already works — so there is no separate global switch to reason about; the inbox
monitor always drains and delivers. This removed the "silently enables event
acceptance" gap at the root rather than papering over it.

---

## 5. [P2] Harden Codex `Stop` hook output — DONE (2026-07-29)

**Problem:** The Codex adapter exited 0 with **empty** output. Codex docs say a
successful `Stop`/`SubagentStop` hook must return JSON on stdout — empty output is
invalid there — so it could surface a hook-output warning.

**Done:** `worklings-codex-activity-hook` now prints `{}` on stdout on the success
path (after draining/discarding stdin, still exit 0, still content-free). `{}` is
the minimal valid success payload for `Stop` and is harmless for `SessionStart` /
`SessionEnd`, which accept empty output too. Docs (`adapters.md`) synced.

---

*Captured 2026-07-29, after `v0.1.0-alpha.5`. See `docs/adapters.md` for the
shipped connector and `docs/changelog.md` for the audit rounds already landed.*
