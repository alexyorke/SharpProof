using System.Collections.Immutable;

namespace SharpProof.Tool;

internal static class ToolApplication
{
    internal const string Help = """
        SharpProof optional diagnostics tool
        Usage:
          dotnet sharpproof init --project <csproj> [--verification portable|container] [--apply] [--consumer-image <digest-pinned-image>]
          dotnet sharpproof doctor [--project <csproj> [--verification portable|container]]
          dotnet sharpproof explain --result <result.json> [--claim <id>] [--request <json>] [--compiler-manifest <json>]
          dotnet sharpproof compare --baseline <result.json> --current <result.json> [--fail-on-regression]
          dotnet sharpproof export-test --project <csproj> --result <result.json> --claim <id> --output <new-directory> [--workspace <root>]
          dotnet sharpproof --help
        Commands accept --format text|json|markdown (default text). Init previews changes unless --apply is specified.
        doctor without --project discovers SDK/runtime information without evaluating projects. With --project it checks evaluated configuration and existing restore evidence. Neither mode restores, builds, or loads the solver.
        explain validates the stored worker protocol and explains recorded outcomes. It does not verify current source. Without a sibling request.json and compiler-manifest.json it explains the standalone result.
        Install this tool optionally. A normal SharpProof PackageReference already enables build verification.
        """;

    internal static async Task<int> RunAsync(
        ImmutableArray<string> args, TextWriter output, TextWriter error, Func<CancellationToken, Task<string>> discoverSdk,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfMissing(args.IsDefault, nameof(args));
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(discoverSdk);
        cancellationToken.ThrowIfCancellationRequested();
        if (args.Length == 0 || args is ["--help"])
        {
            await output.WriteLineAsync(Help.AsMemory(), cancellationToken).ConfigureAwait(false);
            return 0;
        }
        return await DeveloperCommands.RunAsync([.. args], output, error, discoverSdk, cancellationToken).ConfigureAwait(false);
    }
}
