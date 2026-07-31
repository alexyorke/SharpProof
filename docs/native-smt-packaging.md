# Native SMT packaging

The Roslyn analyzer package directory contains no Z3 managed assembly, native
library, native-locator file, or verifier assembly. This is enforced by package
and architecture tests.

SharpProof ships three exact-version packages:

- `SharpProof.Attributes`: the compiler-visible contract API only;
- `SharpProof`: the portable analyzer, generator, and their managed runtime
  closure; and
- `SharpProof.Verifier.Win-x64`: verifier MSBuild integration, worker,
  launcher, managed Z3, and one `win-x64` native Z3 payload.

The dependency graph is
`SharpProof.Verifier.Win-x64 -> SharpProof -> SharpProof.Attributes`, with an
exact current-version range at each edge. The portable package has no `lib`
payload, worker, `tools/net9`, Z3, or native file. The verifier package has no
analyzer directory or Attributes DLL.

Z3 exists only in the verifier package below `tools/net9`, alongside
`SharpProof.Worker` and `SharpProof.Worker.Launcher`. Windows arm64 and
non-Windows worker execution are unsupported. MSBuild starts the launcher only
when `SharpProofVerify=true`; this is optional in the default advisory profile
and mandatory in the strict profile. Design-time builds skip it. On the
supported Windows x64 host, the launcher holds the worker at a startup barrier,
assigns it to a Job Object with the configured memory and process limits, and
then releases work. Job Object creation, configuration, and assignment all
fail closed; the launcher never intentionally continues with an uncapped
worker.

Package validation obtains one isolated three-package feed, either by packing
the graph once locally or by consuming the exact CI artifacts. It inspects
package layouts, portable SourceLink PDBs, repository commits, and dependency
ranges, then restores consumers only from that feed. SDK package validation is
enabled for all three package projects. Each `.nupkg` is PDB-free and has a
matching `.snupkg`; together the release set is exactly three packages and
three symbol packages at one version.

Package-consumer CI creates a deterministic SPDX 2.3 package/component SBOM
plus `SHA256SUMS` and `SharpProof.release.json` evidence. Main package hashes
and bundled third-party versions are validated. Canonical `master` builds
receive separate SLSA provenance and SBOM attestations; pull requests have no
OIDC or attestation-write permission. CI then promotes those
same package bytes to Windows x64
(`windows-latest`), Linux x64 (`ubuntu-latest`), macOS x64
(`macos-15-intel`), and macOS ARM64 (`macos-15`). Every host runs the portable
consumer checks. Only Windows x64 additionally enables verification, launches
the packaged worker, and validates its versioned JSON result; unsupported
hosts reject requested verification explicitly.

The worker receives compiler artifact schema version 8 with portable lowered
CFG/IR. It does not construct a Roslyn compilation, parse source, or reread
reference files; compiler and reference identities are provenance only.
Independent whole-body postcondition-counterexample replay and immutable
tagged-byte promotion through a trusted-publishing workflow are implemented
for the admitted scalar subset. Effect refutations remain fail-closed pending
an independently replayable compiler artifact trace. Tags must match the
checked-in version, belong to
`master`, and follow the predecessor-tag sequence. The publication helper
validates the release manifest and hashes, preflights each of the three target
V3 main-package identities at the release version for absence, and publishes
`SharpProof.Attributes -> SharpProof -> SharpProof.Verifier.Win-x64`.
Main and symbol packages are pushed separately. Publication fails closed if
any main package already exists, and no push uses duplicate skipping. Because
the V3 protocol does not provide an equivalent symbol-package existence
query, a symbol collision also fails the push and leaves the version
unreusable; a partial or conflicting publication therefore requires a new
version. `-PlanOnly` validates and emits the ordered plan without network or
publication, and `-RemotePackageDirectory` supplies offline remote-payload
fixtures.
Optional deterministic SARIF 2.1.0 projects validated worker responses. Owner
configuration of an HTTPS read/push-capable private V3 source, the protected
private/public NuGet environments and tag policy, plus the first publications,
remain production work.
