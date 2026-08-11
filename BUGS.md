# SharpProof current convergence register

This is the finite active register for the container-only `1.0.0-preview.1`
candidate. Historical audit queues and completed Windows-hosted qualification
records are under `eng/agent-notes/archive/`; they are not active backlogs.

## Locally controlled work

- [x] Run the complete Debug solution gate after the analyzer/package
  consolidation. The container run passed all projects on 2026-08-10;
  Package passed 159 cases with one expected unsupported-host skip.
- [x] Run full Release acceptance after the Debug gate is green. Acceptance
  passed on 2026-08-10, including deterministic generation, Release tests,
  fuzz, corpus, package validation, and performance.
- [ ] Commit the coherent tranche, then run the exact-commit-bound 136-case
  trusted mutation campaign. Any survivor is a blocker.
- [ ] Build the three release packages from that immutable commit and rerun
  isolated-feed consumers, five pilots, SBOM validation, and publication dry
  run against those exact bytes.

## Owner-controlled release configuration

- [ ] Configure and validate `NUGET_PRIVATE_SOURCE`.
- [ ] Configure and validate `NUGET_PRIVATE_API_KEY`.
- [ ] Configure and validate `NUGET_USER` for public promotion.
- [ ] Confirm protected release tags and the private/public NuGet environments
  before external publication.

## Closed in the current consolidation

- [x] Compiler-reference provenance is bounded to 256 MiB per module, 1 GiB
  per closure, and 4096 modules; artifact schema 12 records and validates each
  module size.
- [x] The duplicated `SharpProof.PortableAnalyzer` compilation is removed.
  `SharpProof.Analyzer` and `SharpProof.ContractForGenerator` are thin entry
  assemblies over one `SharpProof.Analyzer.Core` implementation.
- [x] Companions emitted by source generators are validated on the final
  compilation without duplicating handwritten generator diagnostics.
- [x] The `SharpProof` package carries one analyzer entry, one generator entry,
  one collector entry, and one shared dependency closure.
- [x] TCB paths reject noncanonical spellings, and the 263-path inventory is
  digest-pinned.
- [x] Pull-request CI restores/builds once inside the canonical container and
  reuses that output for performance, forced-termination, and broad tests.
- [x] Docker build layers use the GitHub Actions cache; redundant PR security
  work is removed and PR runs cancel superseded executions.
- [x] Compiler schema, package inventory, third-party notices, maintained docs,
  frozen interface, generated outputs, and mutation catalog are synchronized.

## Explicitly unsupported for preview

These are roadmap boundaries, not bugs: native Windows or Visual Studio
verifier execution, native-host Linux installs, ARM64 verifier containers,
Rider integration, shared/network publication, hostile concurrent host
filesystem mutation, loops, heap reasoning, and general source-callee
verification.

## Stop rule

After the register above is green, new release blockers require an executable
reproduction in the documented container-supported surface. Speculative audit
findings do not reopen the preview indefinitely.
