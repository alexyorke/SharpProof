using System.Security;
using System.Text;

namespace SharpProof.Package.Test;

internal static class IsolatedPackageFeedConfiguration
{
    private const string ApprovedPackageSource =
        "https://api.nuget.org/v3/index.json";

    internal static string Write(
        string directory,
        string productSource,
        string? offlineFrameworkSource = null)
    {
        var path = Path.Combine(directory, "NuGet.Config");
        var escapedProductSource = Escape(Path.GetFullPath(productSource));
        var frameworkSource = offlineFrameworkSource == null
            ? $"""
                  <add key="nuget.org"
                       value="{ApprovedPackageSource}"
                       protocolVersion="3" />
              """
            : $"""
                  <add key="FrameworkOffline"
                       value="{Escape(Path.GetFullPath(offlineFrameworkSource))}" />
              """;
        var frameworkMapping = offlineFrameworkSource == null
            ? """
                  <packageSource key="nuget.org">
                    <package pattern="Microsoft.*" />
                    <package pattern="NETStandard.*" />
                    <package pattern="runtime.*" />
                    <package pattern="System.*" />
                  </packageSource>
              """
            : """
                  <packageSource key="FrameworkOffline">
                    <package pattern="Microsoft.NETCore.Platforms" />
                    <package pattern="Microsoft.NETFramework.ReferenceAssemblies*" />
                    <package pattern="NETStandard.*" />
                  </packageSource>
              """;
        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="SharpProofLocal"
                     value="{escapedProductSource}" />
            {frameworkSource}
              </packageSources>
              <packageSourceMapping>
                <packageSource key="SharpProofLocal">
                  <package pattern="SharpProof*" />
                </packageSource>
            {frameworkMapping}
              </packageSourceMapping>
            </configuration>
            """,
            new UTF8Encoding(false));
        return path;
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ??
            throw new InvalidOperationException(
                "Failed to escape an isolated package source.");
    }
}
