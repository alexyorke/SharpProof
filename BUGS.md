# SharpProof current convergence register

This is the finite active code and technical-debt register for the
container-only `1.0.0-preview.1` candidate. Historical audit queues and
completed Windows-hosted qualification records are archived under
`eng/agent-notes/archive/`; they are not active backlogs.

Exact-commit mutation, package, pilot, SBOM, and publication-plan artifacts
are generated after the final source commit. They are external qualification
evidence, not checked-in debt rows: recording their result in this file would
change the commit they qualify.

## Active bounded audit

- [x] Pass A: audit analyzer/generator/collector parity, final-compilation
  ContractFor behavior, compiler-artifact schema 12 provenance, and mutation
  discrimination for the consolidated semantic authority. The bounded review
  accepted no defect; 99 analyzer/core/collector, 51 ContractFor, and 32
  schema-12 worker cases passed in the canonical container.
- [x] Pass B: audit Linux host containment/publication, cancellation and exact
  Z3 loading, the three-package graph, packaged consumers, and release-evidence
  commit binding. The bounded review accepted no defect; 21 host/worker,
  16 package/release-authority, and 2 exact native-boundary cases passed in the
  canonical container.
- [x] Close every accepted supported-surface reproduction with a focused
  regression and, for trusted-boundary behavior, a discriminating mutation.
  Neither bounded pass admitted a supported-surface or certifier defect, so no
  production fix, new regression, wire change, or mutation entry was required.

Only an executable failure in the documented container-supported surface, or
a demonstrated certifier defect that can admit invalid release evidence, may
add a row. The audit stops after these two passes; unsupported roadmap features
and speculative hostile-host races do not reopen the preview.

## Closed architecture and supported behavior

- [x] The canonical Linux amd64 container is the only full-verifier host.
  Native Windows/Visual Studio execution and Windows runtime primitives are
  removed from the supported verifier.
- [x] `SharpProof.Analyzer` and `SharpProof.ContractForGenerator` are thin
  entry assemblies over one `SharpProof.Analyzer.Core` implementation; the
  package exposes exactly one analyzer, one generator, and one collector.
- [x] Generated companions are validated on the final compilation without
  duplicating handwritten generator diagnostics.
- [x] Compiler artifact schema 12 binds canonical module order, image sizes,
  MVIDs, hashes, metadata identity, warning policy, and realized diagnostics;
  per-module, closure-byte, and module-count limits are enforced.
- [x] Publication locks and ownership markers cover request, result, compiler
  manifest, and optional SARIF paths as one canonical set; partial overlap,
  unowned files, symlinks, and recognized network filesystems fail closed.
- [x] The launcher owns one direct Linux worker child with a bounded startup
  barrier, parent-death signal, cancellation deadline, and exact packaged Z3
  resolver.
- [x] The release graph is exactly `SharpProof.Attributes`, `SharpProof`, and
  `SharpProof.Verifier`; interface, schema, generated-output, TCB, coverage,
  mutation-catalog, documentation, and package inventories are drift-gated.
- [x] CI and local repository commands execute in the pinned container; Docker
  owns CPU and memory isolation.

## External qualification and publication

After the final source commit, the release procedure must generate evidence at
that exact commit: Debug and Release gates, coverage, all 136 trusted
mutations, local packages and isolated consumers, five pilots, SBOM/package
validation, and publication plan-only. Any subsequent tracked change
invalidates that evidence and requires regeneration.

NuGet credentials, protected release tags, private/public environments, and
external publication are owner-controlled release operations. They are not
locally controlled code debt and require separate authorization. This audit
does not authenticate, publish, promote, or tag.

## Explicit preview boundaries

Native host execution, ARM64 verifier containers, Rider integration,
shared/network publication, hostile concurrent host filesystem mutation,
loops, mutable-heap reasoning, virtual dispatch, and general source-callee
verification remain explicit roadmap items rather than release blockers.
