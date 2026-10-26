namespace SharpProof.Tool;

internal static class SdkProbe
{
    internal static async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        // Discovery follows the caller's normal CLI selection, including global.json.
        // The result is diagnostic information and never authorizes a verifier host.
        var (exitCode, output) = await ToolProcess.RunAsync(
            "dotnet", Environment.CurrentDirectory, TimeSpan.FromSeconds(15), cancellationToken, "--version").ConfigureAwait(false);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("SDK discovery failed; check dotnet --version and global.json.");
        }
        return output;
    }
}
