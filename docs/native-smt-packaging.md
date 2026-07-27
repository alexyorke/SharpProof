# Native SMT packaging

The Roslyn analyzer package directory contains no Z3 managed assembly, native
library, native-locator file, or verifier assembly. This is enforced by package
and architecture tests.

Z3 is packaged only below `tools/net9`, alongside `SharpProof.Worker` and
`SharpProof.Worker.Launcher`. The current preview ships the native solver and
out-of-process verifier only for `win-x64`; Windows arm64 and non-Windows
packaged worker execution are unsupported. MSBuild starts the launcher only
when `SharpProofVerify=true`; this is optional in the default advisory profile
and mandatory in the strict profile. Design-time builds skip it. On the
supported Windows x64 host, the launcher holds the worker at a startup barrier,
assigns it to a Job Object with the configured memory and process limits, and
then releases work. Job Object creation, configuration, and assignment all fail
closed; the launcher never intentionally continues with an uncapped worker.

`SharpProof.Package.Test` packs the NuGet package and inspects the exact
analyzer and worker layouts. Current package-consumer CI restores and runs
analyzer consumers on Windows x64 (`windows-latest`), Linux x64
(`ubuntu-latest`), macOS x64 (`macos-15-intel`), and macOS ARM64 (`macos-15`).
Only Windows x64 additionally enables verification, launches the packaged
worker, and validates its versioned JSON result. The full acceptance job also
runs on `windows-latest`; a verification request on a non-Windows host fails
with an explicit unsupported-host build error.

The planned three-package release split has not happened yet. Two package IDs
exist today: `SharpProof.Attributes` and `SharpProof`. The main package still
embeds an attributes payload and bundles the portable analyzer/generator with
Windows verifier tools instead of depending exactly on the matching attributes
package and placing the worker in `SharpProof.Verifier.Win-x64`. The worker now
receives compiler artifact schema version 3 with portable lowered CFG/IR. It
does not construct a Roslyn compilation, parse source, or reread reference
files; compiler and reference identities are provenance only. The package
split and SARIF projection remain production work. Independent whole-body
counterexample replay is implemented for the admitted program subset.
