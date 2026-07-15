# Repo Guidelines 

1. All documentation is in the `docs/` folder.
2. Do not delete any database files
3. Ensure all git commands are reversible. Commit in small logical chunks using the workflow described below.
4. Run available tests before committing. Eg: npm run build, pnpm test, etc... 
5. You are a senior technical architect. Ask for clarifications on what the end objective of a feature is, push back on decisions you don't agree with. 
6. Always plan first before executing.
7. Always update `docs/changelog.md` after any changes. Use the following format:
   - **Date > File name > methods or functions > what the change does**
   - Each change should be on a new bullet point
8. Before installing dependencies or creating additional files, get user permission and explain why they are needed.
9. Git branching/release process is documented in `docs/git_workflow.md` and must be followed.

## Commit Workflow
  - Always commit and push using `scripts/committer`.
  - Do not use direct `git add` / `git commit` unless explicitly asked.
  - Default branch policy:
    - work on feature/fix branches
    - never commit to `main` unless explicitly instructed
  - Commit command format:
    - `scripts/committer "commit message" "<file1>" "<file2>" ...`
  - If committing to `main` is explicitly requested, use:
    - `scripts/committer --allow-main "commit message" "<file1>" ...`
