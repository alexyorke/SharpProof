# Optional SharpProof developer tool

The normal `SharpProof` PackageReference already enables build verification. This
tool is optional. Its read-only commands inspect setup and stored evidence; `init`
can apply a reviewed setup plan, and `export-test` writes a new regression project.
Neither command runs verification or executes consumer code.

Install the matching version from your configured feed. The official
`1.0.0-preview.1` path is the private `alexyorke/SharpProof-preview` release, with
NuGet source `https://nuget.pkg.github.com/alexyorke/index.json` and initial owner-only
access. Configure the named `SharpProofPreview` source and read-only credentials
through your credential provider or process environment; do not commit credentials.
Publication and real-editor qualification are pending, and public CI validation
versions are distinct from official private-preview artifacts.

Generated templates already pin the matching local tool: use `dotnet tool restore`.
For an existing project, create a manifest only if one does not already exist, then
install the matching tool. Teammates restore that manifest with `dotnet tool restore`.

```sh
dotnet new tool-manifest
dotnet tool install SharpProof.Tool --version 1.0.0-preview.1
dotnet sharpproof doctor
dotnet sharpproof doctor --format json
dotnet sharpproof explain --result obj/Debug/net10.0/SharpProof/result.json
```

Tool execution requires .NET 10; local-manifest installation/restore also requires a
.NET SDK. `doctor` without `--project` runs a bounded `dotnet --version`
discovery using the caller's normal CLI selection (including `global.json`) and
reports its own runtime and architecture. It does not evaluate projects, restore,
build, or load the solver.
It cannot certify package configuration or native solver availability.

`explain --result <result.json>` validates the stored worker protocol and
displays recorded claim and callable outcomes, coverage, vacuity, assumptions,
elapsed time, cache status, and errors. It always labels the output as stored
evidence. Neither successful parsing nor a recorded Proven result establishes
current source freshness or independent request authority. Run your normal consumer
build to obtain fresh proof evidence.
Use your build's printed result path when outputs have been customized.

Without a sibling `request.json` and `compiler-manifest.json` (or explicit
`--request`/`--compiler-manifest`), `explain` reads the standalone result. Exit 0 means the diagnostic command completed, not that a contract was
proven. Invalid syntax, malformed/missing evidence, and failed SDK discovery exit 2.
Results can contain private paths, contracts, and counterexample values; review
them before sharing. This tool never rewrites results or runs verification.

Initialize a supported existing SDK-style project with a preview-first plan:

```sh
dotnet sharpproof init --project MyLibrary.csproj --verification portable
dotnet sharpproof init --project MyLibrary.csproj --verification portable --apply
```

Portable is the default; omit `--apply` to leave files unchanged. The plan pins
the public facade, private editor reference, and local tool to the installed tool's
version and configures the credential-free private feed. It may reuse ancestor
NuGet/tool/central-version files, so review every listed path. Conflicting versions,
ambiguous imports/settings, inline feed credentials, or linked targets are rejected.
Initialization does not restore, build, pull images, or rewrite the selected SDK.

Container setup uses the following syntax:

```text
dotnet sharpproof init --project MyLibrary.csproj --verification container --consumer-image <qualified-image> [--apply]
```

Supply the exact SHA-256 reference from the private receipt, such as
`ghcr.io/alexyorke/sharpproof-preview@sha256:<64-lowercase-hex-digits>`; a compatible
existing pinned Compose file may supply it instead. Canonical
`ghcr.io/alexyorke/sharpproof@sha256:<digest>` references remain accepted for compatible
existing setups. Mutable tags and other registries are not accepted. Preview the
Compose/output-isolation/evidence plan before applying it.

Project-aware setup checks are available with
`dotnet sharpproof doctor --project MyLibrary.csproj --verification portable`.
Use `--verification container` for the starter's canonical container configuration.
This mode checks SDK selection, evaluated package/configuration settings, and existing
restore evidence. Explicit container mode checks Docker and the local consumer image
when run outside the canonical container; a build still validates that container's
contract. For a generated container starter, restore/run the tool through its service
to use the same container-owned assets, package feeds, and SDK as the consumer build.
It evaluates a trusted project's MSBuild imports without invoking build targets and
does not restore, build, pull images, or run verification. Exit 1 means a setup check
failed; it does not replace the SDK-only `doctor` mode without `--project`.

Named-option `init`, `doctor --project`, `explain --result`, `compare`, and `export-test`
accept `--format text|json|markdown`, defaulting to text:

```sh
dotnet sharpproof doctor --project MyLibrary.csproj --format json
dotnet sharpproof explain --result artifacts/sharpproof/result.json --format markdown
```

JSON/Markdown output is one document on stdout, including setup/regression status 1.
Invalid arguments or evidence return 2 with stderr only and empty stdout.

`compare` and `export-test` require a complete matching evidence set: the result plus sibling
`request.json` and `compiler-manifest.json`. Override companion paths with `--request`
and `--compiler-manifest`; comparison also accepts `--baseline-request` and
`--baseline-compiler-manifest` for its baseline.

```sh
dotnet sharpproof explain --result artifacts/sharpproof/result.json --claim <id>
dotnet sharpproof compare --baseline baseline/result.json --current artifacts/sharpproof/result.json --fail-on-regression
dotnet sharpproof export-test --project MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output ../MyLibrary.Regressions
```

`explain` explains claims and typed Unknown detail from validated evidence.
Comparison requires the same assembly identity, framework, and specification selection.
Its regression gate is enabled only by `--fail-on-regression` and does not replace the
consumer's verifier policy. Exit 1 means that enabled gate found a regression.

Export creates a new NUnit project for a replayed direct scalar `Refuted` postcondition
on a public synchronous static method without type parameters. It requires matching
source/reference hashes, supported scalar expressions, and source recorded with C# 13
or earlier. It writes source without executing consumer code or enabling runtime
contracts. Keep the test project outside the consumer's source tree, and run it in a
canonical container workspace exposing both project paths. Exports use a relative
`ProjectReference` where possible; preserve the directory relationship when moving
them. Standalone Windows exports across drive roots retain the legacy absolute
reference and do not gain that relocation behavior.
The output must be new and outside the consumer project directory, its parent must
exist, and workspace mode requires contained paths with a relative project reference.

For the generated `sharpproof-workspace` layout, export from its root through the
existing service:

```sh
docker compose run --rm verify tool run dotnet-sharpproof -- export-test --project src/app/MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --workspace . --output tests/MyLibrary.Regression
docker compose run --rm verify test tests/MyLibrary.Regression/SharpProof.Regression.csproj
```

Workspace mode requires the project, new output, and remapped source inputs inside the
root, with the compatible pinned Linux amd64 Compose service and isolated outputs.
It emits a regression README using that service, without copying credentials or
creating another image/Compose definition. Move the whole workspace together;
captured input hashes still apply while exporting. The generated assertion should
fail before the implementation fix and pass afterward, followed by a fresh strict
consumer build. See the
[developer tools guide](https://github.com/alexyorke/SharpProof/blob/master/docs/developer-experience.md)
for supported frameworks, exact layout, and export limits.

For these evidence commands, exit 0 means processing completed, and exit 2 means invalid
arguments/evidence or unsupported export. Cancellation exits 130. Stored evidence does
not establish current source freshness or authenticate an arbitrary worker binary.
The private preview's publication, signed receipt, and Visual Studio stable/VS Code
C#/Rider Windows x64 interactive acceptance remain pending. Installation or a successful
diagnostic command does not establish any of those release/host results.
