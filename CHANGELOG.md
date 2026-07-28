# Changelog

All notable user-visible changes are recorded here. SharpProof follows
semantic versioning once a stable `1.0.0` release exists; preview releases may
contain documented breaking changes.

## Unreleased

### Added

- Compiler-produced claim manifests and lowered verification artifacts.
- Exact manifest/result accountability and deterministic worker summaries.
- Independent SMT-term and whole-body counterexample replay.
- Advisory and strict profiles with explicit incomplete-analysis diagnostics.
- Windows x64 worker containment, cache validation, and resource budgets.
- Three exact-version packages for the contract API, portable analyzer and
  generator, and Windows x64 verifier.
- Portable-PDB symbol packages with SourceLink bound to the packaged commit.
- Deterministic SHA-256 release manifests, SPDX SBOM generation, and GitHub
  build-provenance and SBOM attestations.

### Changed

- The verifier consumes the final compiler compilation artifact instead of
  reconstructing a compilation from source files.
- Cache schema 6 invalidates results produced before whole-body replay.
- Constructor postconditions report `UnsupportedBody` until constructor
  initialization semantics are represented.
- Package-consumer CI restores the exact same packed bytes across Windows x64,
  Linux x64, macOS x64, and macOS ARM64.
- Package builds run SDK package validation, and GitHub Actions dependencies
  are pinned to immutable commits.

### Security

- SAT models must exactly match the requested scalar model closure and pass
  independent replay before SharpProof emits `Refuted`.

[Unreleased]: https://github.com/alexyorke/SharpProof/compare/master...HEAD
