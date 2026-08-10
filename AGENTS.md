# SharpProof Agent Notes

- Run repository .NET, PowerShell, packaging, and acceptance commands in the canonical Linux amd64 container.
- Preferred entrypoint: `docker compose run --rm tooling <command>`.
- Use `tooling test -Target <project> -TestFilter <filter>` for isolated targeted tests; task commands copy the worktree into a private container workspace.
- Compose normally isolates worktrees by directory name. Set a distinct `COMPOSE_PROJECT_NAME` when directory basenames collide. Never run concurrent builds against one worktree's bind-mounted `bin`, `obj`, or `artifacts` directories.
- Docker owns CPU and memory isolation. Do not add host Job Objects, cgroup readers, RSS monitors, or process-memory command-line budgets.
