# Audit follow-ups (connector / adapters)

Deferred findings from the activity-adapters audits, captured for a future set of
commits. These were **not** blockers for `v0.1.0-alpha.5` — the three prior audit
rounds fixed everything that could hang the app, brick a config, or wreck a
git/Codex/Claude session. What remains is uninstall-safety, edit-race safety,
informed consent, and polish. Ordered by priority.

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

## 2. [P1] Moving or deleting Worklings leaves broken hooks behind

**Problem:** Our hook command points *inside* `Worklings.app`. If the user moves
the app or drags it to the Trash, Codex and Claude keep a command pointing at a
file that no longer exists. Worse, Worklings still reports that hook as
**connected** purely from its file name, even when the saved path is dead. This
contradicts the clean-uninstall / no-impact promise.

**Direction:**
- **Separate two distinct questions** that are currently conflated in
  `HookConfigMerger.hookIsOurs`:
  1. *"Is this hook ours?"* — ownership, matched by the Worklings-namespaced file
     name (this is what must survive an app **relocation**, so we can still find
     and clean up our own hooks).
  2. *"Does this hook point at the currently installed adapter?"* — liveness,
     i.e. does the command's path actually exist and is it executable.
  `isConnected` today answers (1) and calls it "connected"; it should distinguish
  "ours but dead" from "ours and live," and the UI should reflect that.
- **Design an explicit uninstall / disconnect-all experience.** A user removing
  Worklings should have a clear way to remove every hook Worklings ever wrote,
  across both tools, before/independently of deleting the app.
- **Drag-to-Trash needs a deliberate harmless-stale-hook strategy.** We can't run
  code when the app is trashed, so a leftover hook must be *inert* by
  construction: the adapter is content-free and the tools tolerate a missing
  command (Claude logs, Codex is exit-0-only) — verify a missing adapter path
  actually degrades harmlessly in both tools, and document it. Consider whether
  the emitter/adapter should no-op cleanly (never non-zero) when the app is gone.

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
the retries it throws `configChangedDuringWrite` rather than write stale data. A
vanishing read→rename window remains (full closure needs file locking the tools
don't coordinate on) — re-reading right before the write shrinks it to
microseconds, which is what the requirement asks.

---

## 4. [P2] Consent is explicit, but not yet fully informed

**Problem:** Clicking **Connect** immediately edits the external tool's config and
silently enables event acceptance. The action is explicit, but the user isn't
told what's about to happen.

**Direction:** A first-time confirmation that plainly states:
- exactly which file will change (full path),
- exactly what Worklings receives (a content-free kind + source + timestamp — no
  prompt, diff, or path), and that everything stays local,
- how to disconnect / undo (and that a backup is made).
Decide whether "Connect" should also be what flips on **Accept Work Tool Events**,
or whether that stays a separate, visible toggle.

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
