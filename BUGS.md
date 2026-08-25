# SharpProof current convergence register

This is the finite active code and technical-debt register for the
container-only `1.0.0-preview.1` candidate. Historical audit queues and
completed Windows-hosted qualification records are archived under
`eng/agent-notes/archive/`; they are not active backlogs.

Exact-commit mutation, package, pilot, SBOM, and publication-plan artifacts
are generated after the final source commit. They are external qualification
evidence, not checked-in debt rows: recording their result in this file would
change the commit they qualify.

## Priority rubric

- **P0:** Release blockers: false proofs, missing verifier obligations,
  destructive supported behavior, verifier bypasses, or release authority
  accepting invalid candidate bytes.
- **P1:** Material supported-surface defects: incorrect verdicts or
  diagnostics, missing required qualification, or workflows that produce the
  wrong result.
- **P2:** Fail-closed reliability and evidence-integrity defects: lifecycle,
  provenance, canonicality, resource, or reporting failures without a
  demonstrated false proof or invalid release.
- **P3:** Precision, documentation, and developer-experience debt that does
  not change a supported proof or release decision.

The active backlog contains 0 unresolved root-cause rows.
