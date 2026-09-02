# Bug backlog

1 open bug, reprioritized by impact, reachability, and affected scope.

Priority definitions:

- **P0 - Critical:** Can make a false proof or refutation trusted, or break verification/publication integrity.
- **P1 - High:** Can produce a wrong supported-workflow result, hide a mandatory check, or broadly crash, hang, or lose authoritative results.
- **P2 - Medium:** Usually fails closed or causes false positives, incomplete diagnostics, bounded reliability problems, or narrower correctness errors.
- **P3 - Low:** Minor precision, canonicalization, test, documentation, or low-impact operational issue.

## P0 - Critical (1)

- **BUG-146 [P0] - Source-location authority is not bound to claim ownership:** CompilerManifestArtifact validates owner and location pairs against mutable manifest rows and source geometry but never independently binds a predicate or callable to that span. Resealed evidence can report a verified claim at another valid source location while passing authority checks.

## P1 - High (25)


## P2 - Medium (0)

## P3 - Low (0)
