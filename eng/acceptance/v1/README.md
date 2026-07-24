# SharpProof reduction acceptance baseline

This directory freezes the compatibility inputs for the 10,000-line production
C# reduction. The reference state is commit
`0397caa789231b6ce8a49edd7eb15e6be14b7190` with .NET SDK `9.0.315`.

The bundle distinguishes inputs that must remain byte-for-byte stable from
production files that are expected to change:

- `frozen-tests.gitblob` freezes the test projects, shared test harness, package
  consumers, and every existing test source. New test files are allowed;
  changing a listed file requires separate evidence that the baseline test or
  harness was invalid. The net472 project hash includes its approved analyzer
  loader repair.
- `approved-test-updates.gitblob` preserves the original frozen hashes while
  allowing exact, reviewed post-baseline test blobs. Each approval records its
  evidence commits in comments, and the verifier rejects missing, stale, or
  unlisted updates.
- `dependency-references.txt` freezes package and project references without
  freezing unrelated project-file formatting.
- `production-inventory.tsv` records the pinned baseline's counted files and
  line counts. It is checked against the Git object, not against the changing
  working tree.
- `analyzer-contract.json` freezes the 20 descriptors and 19 supported
  configuration options.
- `public-metadata-signatures.txt` freezes every exported type and declared
  public member in the five shipped assemblies.
- `corpus-inventory.gitblob` freezes the 22 README examples, their normalized
  outputs, the five symbolic command definitions, and the 70-shape fuzz
  registry used with seed `12345`.
- `package-contract.json` freezes release metadata and required package layout.

Run the verifier after a Release build:

```powershell
.\eng\acceptance\v1\Verify.ps1
```

During the refactor, the command validates all frozen contracts but reports the
line target as pending. For final acceptance, require the line gate and inspect
the newly packed artifacts:

```powershell
.\eng\acceptance\v1\Verify.ps1 `
  -RequireLineTarget `
  -PackageDirectory .\artifacts\packages
```

`Verify.ps1` does not restore, build, test, pack, or modify the repository.
