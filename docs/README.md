# Documentation

These guides describe the current checkout. Source, protocol validators, and executable tests are the implementation authority. No guide promises support for all C# constructs.

## Use SharpProof

| Guide | Contents |
| --- | --- |
| [Progressive tutorials](tutorials/README.md) | Eight runnable lessons with captured diagnostics and proof results | | Package references, example, profiles, verifier policies |
| [Public API](public-api.md) | Contract clauses, closed attributes, effect declarations, trust controls |
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
| [Release constants](release-constants.md) | Version ownership and compatibility changes |
| [Native SMT packaging](native-smt-packaging.md) | Payload pins, loading, and package graph |

## Develop and qualify

- [Container development](container-development.md): all commands and workspace modes.
- [Contributing](../CONTRIBUTING.md) and [agent notes](../AGENTS.md): development rules.
- [Corpus gate](../SharpProof.Gates/README.md) and [source provenance](../SharpProof.Gates/Corpus/README.md): canonical snapshots and support ratchets.
- [Golden tests](../tests/golden/README.md): stage fixtures and explicit expectation updates.
- [Release process](../eng/release/README.md): tag checks, consumer matrix, and publication.
- [Security policy](../SECURITY.md): private reporting and scope.

The analyzer's [unshipped](../SharpProof.Analyzer/AnalyzerReleases.Unshipped.md) and [shipped](../SharpProof.Analyzer/AnalyzerReleases.Shipped.md) rule tables are tooling metadata. They are not publication evidence.

## Documentation validation

`SharpProof.Analyzer.Test/DocumentationSnippetTests.cs` compiles every C# fence in the root README, this directory recursively, and the samples README. `SharpProof.Attributes.Test/PublicApiDocumentationTests.cs` validates the packaged XML documentation member set. These checks do not validate prose or Markdown links; those need a separate review against current source.
