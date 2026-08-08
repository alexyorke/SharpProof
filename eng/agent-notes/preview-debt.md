# SharpProof preview convergence register

This is the finite technical-debt register for the `1.0.0-preview` release.
Feature expansion is frozen until every item below is closed or explicitly
removed from the supported preview contract.

An item is closed only when its focused regression or qualification evidence
and both repository gates are green. New items require an executable failure
in documented supported behavior, or a concrete release/maintenance defect.
Speculative audits stop after two bounded passes without a new demonstrated
release blocker.

## Active item

`PREVIEW-001` is active. No other production item should be implemented in
parallel.

## Debt register

| ID | Status | Debt and supported impact | Required regression or evidence | Closure evidence |
|---|---|---|---|---|
| PREVIEW-001 | Ready | Publication locks only one output. Cooperative builds with distinct result paths but a shared request, manifest, or SARIF path can interleave; partially overlapping publication sets cannot remain coherent. This affects supported local builds. | Concurrency tests for overlap on each output, reverse acquisition order, partial timeout cleanup, disjoint progress, and explicit rejection of partially overlapping configurations. Invalidation and publication must use the same canonical set. | Pending |
| PREVIEW-002 | Ready | Full-framework MSBuild and Visual Studio long-path behavior is not qualified end to end. This affects the supported Windows/Visual Studio host matrix. | Packaged invalidation and publication beyond 260 characters under command-line and Visual Studio MSBuild, including cache and SARIF; either succeed consistently or fail early with a documented classified error. | Pending |
| PREVIEW-003 | Ready | Compiler artifact schema 10 has no real emitted linked-netmodule provenance fixture. A regression could silently hash only the manifest module. | Emit and consume a manifest plus two linked netmodules; assert canonical order, path, MVID, and SHA-256, and reject stale or metadata-mismatched linked modules. | Pending |
| PREVIEW-004 | Ready | Recent trusted-boundary changes have not all been mutation-audited. Existing experience shows tests can pass without reaching the changed branch. | Record one mutation verdict per trusted-boundary change; a surviving mutation requires a stronger test or deletion of redundant production code. | Pending |
| PREVIEW-005 | Ready | The TCB declaration can lose a required production path without an explicit inventory-drift failure. | Delete one required path from a temporary contract fixture and prove the architecture/acceptance check fails for the missing owner. | Pending |
| PREVIEW-006 | Ready | The product is unsigned, so the public-key portion of the analyzer payload identity check is vacuous and overstates the trust boundary. | Remove the public-key claim and check; retain exact assembly identity and embedded payload SHA-256 checks with tamper and identity regressions. | Pending |
| PREVIEW-007 | Ready | The verifier runner/invalidation implementation is embedded C# inside MSBuild XML, making cancellation, host, and path behavior difficult to compile and test directly. | Replace it with a packaged compiled build-task assembly loadable by command-line and Visual Studio MSBuild; direct unit tests cover cancellation, logging, host validation, and invalidation. | Pending |
| PREVIEW-008 | Ready | Intentional release pins and accidental duplicated defaults are not explicitly classified. Complexity ceilings have been raised during bug fixes without a single recorded policy. | Inventory protocol/schema/budget/property/version constants; retain documented pins, generate or verify behavioral defaults from one owner, and require an architecture note for any complexity-cap increase. | Pending |
| PREVIEW-009 | Ready | Deprecated preview aliases and the final pre-release compatibility boundary remain open. | Remove obsolete aliases/shims, perform any final schema/property cleanup in one change, and add rejection tests plus a frozen public-surface snapshot. | Pending |
| PREVIEW-010 | Ready | Windows x64 command-line behavior is covered, but real Visual Studio MSBuild qualification is outstanding. | Run the packaged matrix in installed Visual Studio MSBuild with long/percent paths, cache, SARIF, cancellation, and cooperative concurrency; record exact version and results. | Pending |
| PREVIEW-011 | Ready | Repository branch/tag protection and private/public NuGet environments are not configured or evidenced. | Record protected-tag, environment, trusted-publishing, and dry-run evidence tied to the release commit. | Pending |
| PREVIEW-012 | Ready | No five-library pilot report exists. | Qualify two effect-heavy libraries, two contract-heavy libraries, and one mixed strict-mode library; record diagnostics, Unknown reasons, latency, memory, false positives, and setup friction. | Pending |
| PREVIEW-013 | Ready | The documented two-human TCB review rule is not enforceable for the current solo workflow. | Replace it with the approved solo evidence gate: executable regression, mutation evidence, soundness note where semantics change, exact-commit artifacts, and green acceptance. | Pending |
| PREVIEW-014 | Ready | The trusted-host boundary is spread across several documents and can be mistaken for hostile-filesystem or cross-host publication support. | State one normative boundary: local trusted Windows build host; reject UNC publication; Rider, Windows ARM64, hostile local mutation, and shared-network publication are unsupported for this preview. | Pending |

## Completion rule

The register is complete when every row has status `Closed`, both repository
gates pass, five pilots are reviewed, Windows CLI and Visual Studio
qualification pass, and the release dry run is tied to the exact candidate
commit. Unsupported post-preview features do not reopen this register.
