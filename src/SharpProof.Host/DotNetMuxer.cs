namespace SharpProof.Host;

internal static class DotNetMuxer
{
    internal static bool IsFileName(string? fileName, bool isWindows)
    {
        return string.Equals(fileName, isWindows ? "dotnet.exe" : "dotnet", PathComparison(isWindows));
    }

    internal static StringComparison PathComparison(bool isWindows)
    {
        return isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
