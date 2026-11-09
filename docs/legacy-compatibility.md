# Older Windows, Visual Studio, and application frameworks

The verifier's build host, the application's target framework, the IDE's compiler,
and the application's deployment OS are separate compatibility boundaries.
Installing one NuGet package cannot supply an unsupported CLR or change the compiler
embedded in an old Visual Studio installation.

## What the package tests establish

The single-reference default-verification tests build SDK-style libraries against
these targets using the canonical .NET 10 SDK and the actual packaged verifier:

| Application targets | Qualification |
| --- | --- |
| .NET Framework 4.6.1, 4.6.2, 4.7.2, 4.8, 4.8.1 | Compile and complete a nonvacuous proof |
| .NET Standard 2.0 | Compile and complete a nonvacuous proof |
| .NET Core 2.0, 3.1 | Compile and complete a nonvacuous proof |
| .NET 5, 6, 7, 8, 9, 10 | Compile and complete a nonvacuous proof |

Each consumer authors exactly one `SharpProof` reference and no SharpProof
properties. SDK-added implicit framework references are allowed. Separate negative
cases require a false contract to produce a Refuted result and fail the build for
Framework 4.6.1/4.7.2/4.8.1, Standard 2.0, Core 2.0, and .NET 10. This checks that an
older target does not accidentally skip verification. The legacy Verifier entrypoint
retains its existing controls.

The current [release workflow](../.github/workflows/release.yml) runs one native
smoke consumer on `windows-latest` and one on `macos-latest`. Each targets net10.0
and checks a Proven postcondition and its Refuted broken counterpart against the
exact packed packages. It does not execute a net48 downstream application or run
MSBuild.exe. The older-target tests above run under Core MSBuild in the canonical
Linux container and establish compilation and verifier behavior, not deployment
on an original Framework runtime or old Windows OS.

These are integration tests using a small supported contract. They do not expand
[language/API coverage](coverage-and-limits.md) or establish that arbitrary project
types, runtime APIs, deployment systems, or application dependencies are supported.

## Build-machine and deployment Windows versions

The verifier requires a supported .NET 10 installation, either Core MSBuild or the
Windows x64 MSBuild 18+ task-host integration described below, a native x64 or ARM64
execution environment, and the filesystem prerequisites in the
[host support matrix](preview-support.md). Microsoft lists the supported Windows
editions and versions in its [.NET installation table](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
This includes qualifying Windows 10 releases; it is not a claim about every Windows
10 edition or a qualification run on every release.

| Windows version | Running the verifier locally | Deploying an application built elsewhere |
| --- | --- | --- |
| Supported Windows 10/11 releases | Subject to the host prerequisites and native qualification | Depends on the application's own target and dependencies |
| Windows 7 / 8 / 8.1 | Unsupported: the required .NET 10 host is unavailable | Some Framework targets fit historical runtime limits; old-OS execution is unqualified |
| Windows XP | Unsupported | The current package cannot target XP's Framework generation |

Microsoft's [historical Framework table](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server#windows-81-8-7-vista-xp)
lists Framework 4.8 for Windows 7/8.1, 4.6.1 for Windows 8, and 4.0.3 for XP SP3.
The package's API assembly targets Standard 2.0, whose NuGet compatibility starts at
Framework 4.6.1. Microsoft recommends Framework 4.7.2 or later for Standard 2.0
libraries because older Framework combinations have integration issues. See the
[Standard compatibility table](https://learn.microsoft.com/en-us/dotnet/standard/net-standard).
Building against 4.6.1 is tested here; successful execution on an original 4.6.1
installation still needs its own qualification.

## Visual Studio and project formats

Command-line verification uses `dotnet build` from a supported SDK, or supported
Windows MSBuild.exe with the .NET task-host bridge. A project's older application
target does not require an older SDK. Visual Studio builds and editor diagnostics
also depend on the Visual Studio MSBuild and embedded Roslyn versions. The portable
analyzer currently needs Roslyn 4.14 or newer; the verifier uses .NET 10 build tasks.
See Microsoft's
[SDK/MSBuild/Visual Studio versioning policy](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs).

| IDE / project | Current boundary |
| --- | --- |
| Modern SDK-style C# project, built with `dotnet build` | Target matrix above; one main-package reference |
| Visual Studio 2026 / Windows x64 MSBuild 18+ | Implemented .NET 10 out-of-process task-host path; actual MSBuild.exe qualification remains separate from current CI |
| Visual Studio 2022 / MSBuild 17.14 | Explicit advisory-only operation with a compatible SDK/Roslyn host; default strict verification requires upgrading the build host |
| Visual Studio 2019 and older | Existing analyzer compiler minimum is not met; no compatibility promise |
| Classic non-SDK project or `packages.config` | Not covered by the one-reference SDK-style tests; requires separate package import/compiler integration |

Full-framework Windows MSBuild 18+ launches the four verifier tasks through
`UsingTask` declarations with `Runtime="NET"` and `TaskHostFactory`, while the
`dotnet` CLI retains its Core MSBuild execution path. The package requires a
compatible .NET 10 SDK/runtime on that machine and rejects older full-framework
MSBuild for default strict verification. The SDK MSBuild apphost and loaded runtime
must belong to the same .NET installation; custom split SDK/runtime layouts are
rejected. This uses Microsoft's out-of-process
[.NET task host for VS 2026](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk#use-net-msbuild-tasks-with-net-framework-msbuild).

The package layout and host-selection tests exercise the task registrations,
version gates and .NET-installation identity checks. They do not run a real
Visual Studio MSBuild.exe host. The current release workflow has no MSBuild 17.14
advisory-only job, no MSBuild 18+ full-verifier job, and no full-framework ARM64
qualification job. Its native Windows smoke uses `dotnet build` on the hosted
runner's architecture.

To qualify those full-framework routes, run actual MSBuild.exe against the exact
packages: require the single-reference proof and broken-contract failure, pack a
library, execute its downstream net48 application on the intended CLR, and test
clean, incremental and cancellation behavior. Retain the SDK/MSBuild.exe version,
source SHA, architecture and complete result evidence. Native Core MSBuild results
cannot stand in for those full-framework host results.

Design-time builds do not run proofs. Command-line host qualification does not
establish interactive Visual Studio diagnostic discovery, code-fix menus, or typing latency.
Installing a newer command-line SDK does not replace an IDE's embedded compiler or
establish that its analyzer/code-fix loader works. Those interactive workflows have
not been manually qualified by this matrix.

## Work required to extend the boundary

1. Complete the exact-artifact MSBuild.exe qualification above and qualify additional
   build-host versions explicitly before extending support. Cover cancellation,
   incremental builds, errors, and publication integrity in the relevant workflows;
   a passing proof alone does not establish every MSBuild integration behavior.
2. For older editors, provide a compatible analyzer/generator build or a separate
   compiler service. Test diagnostic discovery and code fixes inside those IDEs;
   command-line tests cannot establish that behavior.
3. For XP-era application targets, add an API assembly for the older framework,
   compatible NuGet asset/dependency groups, reference packs, and runtime tests.
   That would support targeting old applications from modern hosts; running the
   verifier on XP additionally requires porting its runtime and native dependencies.
4. For old Windows deployment guarantees, use versioned Windows VMs with the exact
   CLR/update/architecture combinations. Run a downstream application, validate its
   complete dependency closure, and retain OS/runtime provenance. A current Windows
   runner, Linux cross-compilation, or a mocked OS version is insufficient.

Unsupported hosts must fail visibly. Compatibility work must preserve strict proof,
replay, completeness, integrity, and negative controls; falling back to an
unverified build does not satisfy the default one-package contract.
