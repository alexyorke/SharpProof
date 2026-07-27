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
5. Run the relevant focused tests, then the full acceptance contract:

   ```powershell
   .\eng\acceptance\Verify.ps1
   ```

Long-lived local .NET commands on Windows must run through
`scripts/Invoke-SharpProofDotnet.ps1`, which places the process tree in a Job
Object. The acceptance script already uses the repository wrapper.

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
profile, feature set, verification policy, host OS/architecture, SDK version,
diagnostic IDs, and worker result when available. Report security-sensitive
issues through [SECURITY.md](SECURITY.md), not a public issue.

By contributing, you agree that your contribution is licensed under the
repository's [MIT License](LICENSE).
