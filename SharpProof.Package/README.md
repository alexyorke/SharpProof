# SharpProof

Add one package to an SDK-style C# project and build to run contract/effect analysis and native verification:

```text
dotnet add package SharpProof
dotnet build -c Release
```

Use an available version from your configured feed. This preview's source version does not establish public feed availability. No SharpProof activation properties or separate Attributes/Verifier references are required. The exact-version graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`.

```csharp
using SharpProof.Attributes;

public static class Counter
{
    public static int Next(int value)
    {
        Contract.Requires(value >= 0 && value < int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + 1);
        return checked(value + 1);
    }
}
```

The default is strict verification with `require-proven` and assumption policy `error`. Inspect selected claims, incomplete callable coverage, assumptions, and vacuity; diagnostic silence or a build with no selected claims is not a proof. Contracts are static declarations and do not replace runtime validation. Do not define `SHARPPROOF_CONTRACTS`.

Full verification requires a compatible .NET 10 SDK/runtime and supported build host. Native payloads cover Windows, macOS, and glibc Linux on x64/ARM64; filesystem, ABI and build-host limits still apply. Windows payloads require no separate Visual C++ redistributable. musl and arbitrary OS/CPU combinations are unsupported. See the [support matrix](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/preview-support.md), including Visual Studio build-host and legacy-target qualification boundaries.

Keep this reference without `PrivateAssets="all"` when your application or packed library needs its transitive contract dependency. Explicit advisory/off profiles are advanced opt-outs; SDK 9 portable analysis requires deliberate advisory configuration and cannot run the default verifier.

Tooling carries embedded portable symbols and source documents; matching extracted symbols support debugging and coverage without rebuilding a different binary. Attributes is fully strong-named for Framework compatibility using a deliberately public stable identity key, not a publisher-authentication credential. See [NuGet debugging](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/nuget-debugging.md) and [identity policy](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/eng/signing/README.md).

The optional [SharpProof.Tool](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/SharpProof.Tool/README.md) provides SDK diagnostics and stored-result explanation through a local tool manifest; normal application adoption does not require it. Public release workflows provide an SPDX payload SBOM, source/artifact manifests and provenance attestations. Check the actual release evidence using the [verification instructions](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/eng/release/README.md); source settings do not establish a published or attested release.

Start with [getting started](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/getting-started.md), then [read verification evidence](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/verification-results.md). Use [Unknown reasons](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/unknown-reasons.md) and [safe support bundles](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/support.md) for troubleshooting. This package is the metadata-only entrypoint; implementation assets belong to Verifier and the application API belongs to Attributes. Preview limitations and [MIT licensing](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/LICENSE) apply.
