# Git Workflow

## Branching

- `main` is the stable integration and release branch.
- Perform work on focused `feature/<description>` or `fix/<description>` branches.
- Do not commit directly to `main` unless the user explicitly authorizes it.
- Branch from an up-to-date `main` and keep commits small enough to review and revert independently.

## Before committing

1. Review `git status --short` and the intended diff.
2. Run the available tests, builds, and formatting checks relevant to the changed files.
3. Update `docs/changelog.md` for every repository change.
4. List every path explicitly when committing.

## Committing

Always use `scripts/committer`; do not use raw `git add` or `git commit` unless the user explicitly requests it:

```bash
scripts/committer "commit message" "file1" "file2" ...
```

For an explicitly authorized commit to `main`:

```bash
scripts/committer --allow-main "commit message" "file1" "file2" ...
```

The script:

- requires explicit file paths and rejects `.`;
- refuses paths inside `node_modules`;
- stages the named paths, including intentionally ignored files;
- commits only the named paths;
- refuses direct commits to `main` or `master` without `--allow-main`;
- refuses commits from a detached `HEAD`;
- can remove a verified stale Git index lock when `--force` is supplied.

The script does not discard unrelated staged or working-tree changes. Always inspect the repository status after committing.

## Pushing

- Do not push without explicit user instruction.
- Never force-push `main`.
- When pushing a feature branch for the first time, use:

```bash
git push --set-upstream origin feature/<description>
```

- If no remote is configured, keep commits local until the repository owner provides one.

## Pull requests and releases

- Open pull requests against `main`.
- Keep pull requests concise and review-focused:
  - use a clear title that describes the outcome;
  - summarize only the material changes;
  - list the checks that were run;
  - include caveats only when they affect review or merge decisions.
- Omit commit-by-commit narration, implementation diaries, repeated roadmap content, and generic boilerplate.
- Run the full available test suite before merge.
- Prefer squash merging unless preserving a sequence of independently valuable commits improves the history.
- Create release artifacts from a clean, tagged commit on `main`.
