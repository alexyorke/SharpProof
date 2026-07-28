# SharpProof documentation map

SharpProof 0.2 is a soundness-first preview. The documents below have different
jobs; they are not interchangeable sources of truth.

## Start here

| Document | Audience | Role |
|---|---|---|
| [Project README](../README.md) | Package users | Installation, activation, examples, and the short product overview |
| [Coverage and limits](coverage-and-limits.md) | Users and contributors | Authoritative inventory of the currently implemented analyzer, worker, language, contract, and API-spec surface |
| [Supported public API](public-api.md) | Library authors | Supported contract types, package boundary, and XML-documentation guarantee |
| [Diagnostics](diagnostic-examples.md) | Analyzer and verifier users | Current `SP`, `SPCF`, SP0047, SP0048, and SP0049 diagnostics, defaults, policies, and examples |
| [Package-backed samples](../samples/README.md) | Evaluators and CI owners | Passing, diagnostic, mixed-outcome, strict-library, and host-rejection examples against packed artifacts |
| [Analysis limits](analysis-limits.md) | Build and CI owners | Shipping profile/feature/policy properties, worker bounds, and acceptance-only budgets |
| [Typed abstention reasons](unknown-reasons.md) | Tool integrators | Exact typed reasons, run statuses, callable coverage, claim outcomes, and cache states |

## Normative and architectural documents

| Document | Status | Role |
|---|---|---|
| [SEMANTICS.md](../SEMANTICS.md) | Normative | Defines the soundness boundary. It wins over descriptive prose when documents conflict. |
| [SharpProof architecture](architecture.md) | Maintained design | Describes the enforced dependency graph, trusted boundaries, proof construction, and package split. |
| [SMT lifecycle](smt-lifecycle.md) | Maintained implementation reference | Describes solver ownership, proof and replay checks, and cache eligibility. |
| [Native SMT packaging](native-smt-packaging.md) | Maintained packaging reference | Describes the Windows x64 worker payload and the analyzer/solver separation. |

The implementation remains the authority for enumerated surfaces:

- `SharpProof.Analyzer/LanguageSubsetGate.cs` classifies analyzer callables,
  types, operation kinds, and operation shapes.
- `SharpProof.Specs/ApiSpecTable.cs` declares typed API specifications. Not
  every witnessed facet is consumed by the worker.
- `SharpProof.Analyzer/GeneratedDiagnosticDescriptors.cs` and
  `SharpProof.ContractForGenerator/GeneratedDiagnosticDescriptors.cs` declare
  diagnostic IDs, severities, defaults, and messages.
- `SharpProof.Worker.Protocol/ProtocolModel.cs` declares protocol version 5,
  manifest schema version 2, cache schema version 6, policies, run statuses,
  callable coverage, claim outcomes/reasons, and summary records.
- `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs` declares compiler
  artifact schema version 3 and the closed compiler-evidence envelope.
- `eng/acceptance/contract.json` declares release-gate budgets. Package
  defaults that are not release-gate fields live in the portable and verifier
  build-transitive props and targets.

## Acceptance and evidence

| Document | Status | Role |
|---|---|---|
| [Acceptance contract](../eng/acceptance/README.md) | Active | Defines the release checks for the 0.2 preview. |
| [Release gates](../SharpProof.Gates/README.md) | Active | Documents the corpus, metamorphic, performance, and cancellation runners. |
| [Open-source corpus](../SharpProof.Gates/Corpus/README.md) | Active | Records corpus provenance, licensing, instrumentation, and update procedure. |
| [2026-07-27 product bug sweep](soundness-notes/2026-07-27-product-sweep.md) | Dated evidence | Records analyzer, contract, effect, and worker adversarial fixes plus exact validation evidence. |
| [2026-07-25 hardening audit](soundness-notes/2026-07-25-hardening.md) | Dated evidence | Records one completed hardening tranche and its remaining checkpoints. |
| [2026-07-25 API-spec result domains](soundness-notes/2026-07-25-api-spec-result-domains.md) | Dated evidence | Records the bounded worker result-projection tranche. |

Soundness notes record what was reviewed at a point in time. They do not replace
the current coverage inventory or normative semantics.

## Known production gaps

During Windows verification, the production analyzer emits a deterministic
schema-3 artifact from the final post-generator Roslyn `Compilation`. It
contains the selected-claim manifest and portable lowered whole-body CFG/IR for
supported selected callables, plus bound contract/spec metadata, compiler
diagnostics, generated-tree hashes, bounded options, mapped locations, and
identity/provenance evidence. It contains no source text.

The worker validates and hydrates that closed artifact without constructing a
Roslyn compilation or rereading reference files. Exact manifest/lowered
callable equality and the compiler-visible expression-depth match are required
before cache or backend work. Compiler and reference identities are provenance,
not a runtime Roslyn-build gate. The compiler reconstruction portion of
production-plan Step 4 is complete for the bounded verifier subset.

Independent whole-body counterexample replay is implemented for the admitted
program subset. The proof kernel checks exact model closure and the lowered
assumptions/goal before the worker independently executes the compiler-produced
whole-body CFG. The three-package split, portable SourceLink symbols, package
validation, deterministic hashes, SPDX SBOM generation, GitHub build/SBOM
attestations, package-backed sample matrix, and exact public API XML coverage
are implemented. SARIF projection, protected-tag promotion, trusted NuGet
publishing, pilot-library evidence, and the remaining release reviews are
future work. Current behavior and limits are recorded in
[Coverage and limits](coverage-and-limits.md#closed-compiler-artifact-and-remaining-limits).

## Machine-owned Markdown

- `SharpProof.Analyzer/AnalyzerReleases.Shipped.md` and
  `AnalyzerReleases.Unshipped.md` are Roslyn release-tracking inputs. Tests
  reconcile them with the active descriptor catalog; edit them only as part of
  a diagnostic release change.

## Maintenance

Markdown is hand-maintained. `scripts/Generate-Readme.ps1 -Verify` validates
code-derived versions, acceptance-contract versions, configuration values,
diagnostics, API-spec IDs, worker properties, protocol enums, local links,
anchors, XML and PowerShell fences, line endings, and BOM policy; the analyzer
test suite compiles every maintained C# fence. The script does not
generate these files. When behavior changes, update the relevant source-owned
table first, then update the coverage, diagnostic, limit, or reason reference
that mirrors it. Dated soundness notes remain subject to link and file-format
checks but are excluded from current-version drift checks.
