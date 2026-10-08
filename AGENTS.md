# Working directory and Git workflow

- The canonical working directory is `/home/tea/github/lms`.
- Work in that directory on `main`, sequentially, unless the user explicitly requests another branch or parallel work.
- Do not create additional Git worktrees. Start new Codex tasks in Local mode using the saved project directory.
- Older tasks may still point to `.codex/worktrees/*/lms`; do not continue implementation in those snapshots. Use the canonical directory explicitly.
- Keep existing checkpoint documentation and run checks appropriate to each change.
