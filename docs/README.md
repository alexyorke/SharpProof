# Documentation

These guides describe the current checkout. Source, protocol validators, and executable tests are the implementation authority. No guide promises support for all C# constructs.

For a first consumer, start with [getting started](getting-started.md) and the [tutorials](tutorials/README.md). To enable a CI proof gate, use [configuration](configuration.md), then [read its evidence](verification-results.md). To investigate support for a particular construct, use the [language reference](language-reference.md). To change SharpProof itself, follow the [development workflows](development-workflows.md).

## Use SharpProof

| Guide | Contents |
| --- | --- |
| [Getting started](getting-started.md) | Package references, first contract, profiles, and verifier policies |
| [Progressive tutorials](tutorials/README.md) | Eight runnable lessons with captured diagnostics and proof results |
| [Configuration](configuration.md) | Consumer MSBuild properties, selection, IDE behavior, budgets, cache, and report paths |
| [Verification evidence](verification-results.md) | Find reports, join claims to selection, interpret proofs, replay, assumptions, vacuity, and cache records |
| [Public API](public-api.md) | Contract clauses, closed attributes, effect declarations, trust controls |
| [Language reference](language-reference.md) | Construct-by-construct model, support boundaries, and regression evidence |
| [Diagnostic examples](diagnostic-examples.md) | Portable analyzer diagnostics and worker reporting |
| [Coverage and limits](coverage-and-limits.md) | Modeled operations, conservative boundaries, regression coverage |
| [Unknown reasons](unknown-reasons.md) | Typed abstentions and infrastructure failures |
| [Analysis limits](analysis-limits.md) | Current defaults and bounded execution |
| [Preview support](preview-support.md) | Container, filesystem, and threat-model boundary |
| [Samples](../samples/README.md) | Executable package-consumer examples |

## Understand the implementation

| Reference | Contents |
| --- | --- |
| [Semantics](../SEMANTICS.md) | Meaning of outcomes, contracts, effects, assumptions, and replay |
| [Architecture](architecture.md) | Compiler, IR, solver, worker, and build boundaries |
| [SMT lifecycle](smt-lifecycle.md) | Native solver ownership, budgets, cancellation, replay |
| [Default API catalog](api-spec-catalog.generated.md) | Hand-maintained projection of the checked-in catalog |
| [Source declaration reference](generated/source-reference.md) | Generated literal configuration, API, diagnostic, and command declarations |
| [Release constants](release-constants.md) | Version ownership and compatibility changes |
| [Native SMT packaging](native-smt-packaging.md) | Payload pins, loading, and package graph |
| [Native Windows verifier plan](windows-native-verifier-plan.md) | Steps and acceptance gate for adding Windows x64 verification |

## Develop and qualify

- [Development workflows](development-workflows.md): choose targeted, PR, coverage, corpus, packaging, and qualification workflows.
- [Container development](container-development.md): all commands and workspace modes.
- [Documentation maintenance](documentation-maintenance.md): source-reference generation, local link checks, and automatic update PRs.
- [Contributing](../CONTRIBUTING.md) and [agent notes](../AGENTS.md): development rules.
- [Corpus gate](../SharpProof.Gates/README.md) and [source provenance](../SharpProof.Gates/Corpus/README.md): canonical snapshots and support ratchets.
- [Golden tests](../tests/golden/README.md): stage fixtures and explicit expectation updates.
- [Release process](../eng/release/README.md): tag checks, consumer matrix, and publication.
- [Security policy](../SECURITY.md): private reporting and scope.

The analyzer's [unshipped](../SharpProof.Analyzer/AnalyzerReleases.Unshipped.md) and [shipped](../SharpProof.Analyzer/AnalyzerReleases.Shipped.md) rule tables are tooling metadata. They are not publication evidence.

## Documentation validation

`SharpProof.Analyzer.Test/DocumentationSnippetTests.cs` compiles every C# fence in the root README, this directory recursively, and the samples README. `SharpProof.Attributes.Test/PublicApiDocumentationTests.cs` validates the packaged XML documentation member set. `tooling docs` checks generated source-reference drift, tracked local Markdown targets/heading anchors, and documentation automation regressions; `pr-gates` includes this check. External links and authored behavioral claims still need review against their supporting sources. See [documentation maintenance](documentation-maintenance.md).
