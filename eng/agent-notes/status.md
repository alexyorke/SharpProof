# SharpProof active status

The active release target is `1.0.0-preview.1` in the canonical Linux amd64
container. The completed finite code-usefulness inventory and cleanup evidence
is recorded in `docs/code-usefulness-audit.md`; historical agent queues are
archived under `eng/agent-notes/archive/`.

Current architecture:

- portable attributes plus analyzer/generator package;
- container-only verifier package and Core MSBuild host;
- one analyzer Core implementation shared by the analyzer, generator, and
  compiler collector;
- compiler artifact schema 18 and worker protocol 11;
- exact three-package release graph: `SharpProof.Attributes`, `SharpProof`, and
  `SharpProof.Verifier`.

Static acceptance is green for deterministic generation, schema/catalog pins,
the 254-entry trusted mutation catalog, the 348-path TCB inventory, frozen
preview interface, and structural complexity. Broad Debug and full Release
acceptance are also green.

The audit document fixes its baseline and records completed cleanup rather than
acting as an active queue. Exact-commit mutation, package, pilot, SBOM, and
publication-plan results are generated after the final source commit and remain
external evidence so that documenting them does not invalidate the commit they
qualify. NuGet credentials, protected tags, release environments, publication,
promotion, and tagging remain separately authorized owner operations.
