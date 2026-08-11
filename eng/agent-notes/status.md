# SharpProof active status

The active release target is `1.0.0-preview.1` in the canonical Linux amd64
container. The authoritative finite work list is `/BUGS.md`; historical agent
queues are archived under `eng/agent-notes/archive/`.

Current architecture:

- portable attributes plus analyzer/generator package;
- container-only verifier package and Core MSBuild host;
- one analyzer Core implementation shared by the analyzer, generator, and
  compiler collector;
- compiler artifact schema 12 and worker protocol 11;
- exact three-package release graph: `SharpProof.Attributes`, `SharpProof`, and
  `SharpProof.Verifier`.

Static acceptance is green for deterministic generation, schema/catalog pins,
the 136-entry mutation catalog identity, the 263-path TCB inventory, frozen
preview interface, and structural complexity. Broad Debug and full Release
acceptance are also green. Exact-commit mutation evidence, package/pilot
qualification, and external release configuration remain as listed in
`/BUGS.md`.
