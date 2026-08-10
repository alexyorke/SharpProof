# Contributing to SharpProof

SharpProof is a soundness-first verifier. Changes that broaden accepted C#
must preserve fail-closed behavior: unsupported or incompletely modeled code
must remain visible as a typed incomplete result, never silent success.

## Before opening a pull request

1. Create a focused branch from `master`.
2. Keep analyzer, compiler artifact, worker protocol, and documentation
   changes synchronized when a public behavior or schema changes.
3. Add a regression test for every correctness or soundness fix.
4. Use LF line endings and do not commit generated build outputs.
5. Run the relevant focused tests, then the full acceptance contract inside
   the canonical container:

   ```text
   sp test-changed
   sp check
   sp acceptance -Configuration Release
   ```

   For a clean disposable qualification run from the host:

   ```text
   docker compose build tooling
   docker compose run --rm tooling acceptance -Configuration Release
   ```

Do not install or invoke repository .NET, PowerShell, MSBuild, Z3, test, pack,
mutation, or release tooling on the host. Open the `dev` service for permanent
work, or use the finite `tooling` commands for disposable validation. The
container owns Git initialization, process cleanup, and all wall deadlines.
The host-side repository contract is Docker Compose only; Make, Just, and host
bootstrap scripts are deliberately unnecessary.

## Pull request expectations

A pull request should explain:

- the behavior changed and why;
- the trusted-computing-base impact;
- the new or updated tests;
- any compatibility, package, cache, or protocol consequence;
- the exact validation commands and outcomes.

Do not raise size, performance, timeout, or trusted-computing-base limits only
to make a change pass. Explain and review any necessary limit change
independently.

## Reporting bugs

Use a minimal source example and include the SharpProof package version,
profile, feature set, verification policy, container contract, SDK version,
diagnostic IDs, and worker result when available. Report security-sensitive
issues through [SECURITY.md](SECURITY.md), not a public issue.

By contributing, you agree that your contribution is licensed under the
repository's [MIT License](LICENSE).
