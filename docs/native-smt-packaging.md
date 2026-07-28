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
package layouts and dependency ranges, then restores consumers only from that
feed. Package-consumer CI promotes those same bytes to Windows x64
(`windows-latest`), Linux x64 (`ubuntu-latest`), macOS x64
(`macos-15-intel`), and macOS ARM64 (`macos-15`). Every host runs the portable
consumer checks. Only Windows x64 additionally enables verification, launches
the packaged worker, and validates its versioned JSON result; unsupported
hosts reject requested verification explicitly.

The worker receives compiler artifact schema version 3 with portable lowered
CFG/IR. It does not construct a Roslyn compilation, parse source, or reread
reference files; compiler and reference identities are provenance only.
Independent whole-body counterexample replay is implemented for the admitted
program subset. SARIF projection and release provenance remain production
work.
