using System.Text.Json;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Schema;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryArtifactSourceTests
{
    [TestCase("net8.0", true)]
    [TestCase("net8.0-windows", true)]
    [TestCase("net7.0", false)]
    public void FrameworkSource_ValidatesResolvedRuntimePath(string framework, bool expectedCompatible)
    {
        var source = CreateSource($$"""
                                    {
                                      "Kind": "framework",
                                      "Framework": "{{framework}}"
                                    }
                                    """);
        var actual = new ActualAssemblyIdentity(
            "System.Private.CoreLib",
            "hash",
            "mvid",
            @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.20\System.Private.CoreLib.dll");

        var compatibility = source.GetCompatibility(actual);

        Assert.That(compatibility.IsCompatible, Is.EqualTo(expectedCompatible));
        if (!expectedCompatible)
            Assert.That(compatibility.ReasonCode, Is.EqualTo("effect_summary_framework_source_mismatch"));
    }

    [TestCase("system.collections.immutable", "9.0.0", "lib/net8.0/System.Collections.Immutable.dll", true)]
    [TestCase("system.collections.immutable", "8.0.0", "lib/net8.0/System.Collections.Immutable.dll", false)]
    [TestCase("other.package", "9.0.0", "lib/net8.0/System.Collections.Immutable.dll", false)]
    [TestCase("system.collections.immutable", "9.0.0", "lib/net7.0/System.Collections.Immutable.dll", false)]
    public void PackageSource_ValidatesNuGetCachePath(
        string packageId,
        string packageVersion,
        string relativePath,
        bool expectedCompatible)
    {
        var source = CreateSource($$"""
                                    {
                                      "Kind": "package",
                                      "PackageId": "{{packageId}}",
                                      "PackageVersion": "{{packageVersion}}",
                                      "PackageAssemblyRelativePath": "{{relativePath}}"
                                    }
                                    """);
        var actual = new ActualAssemblyIdentity(
            "System.Collections.Immutable",
            "hash",
            "mvid",
            @"C:\Users\test\.nuget\packages\system.collections.immutable\9.0.0\lib\net8.0\System.Collections.Immutable.dll");

        var compatibility = source.GetCompatibility(actual);

        Assert.That(compatibility.IsCompatible, Is.EqualTo(expectedCompatible));
        if (!expectedCompatible)
            Assert.That(compatibility.ReasonCode, Is.EqualTo("effect_summary_package_source_mismatch"));
    }

    private static EffectSummaryArtifactSource CreateSource(string sourceJson)
    {
        var contract = JsonSerializer.Deserialize<EffectSummaryArtifactSourceContract>(sourceJson);
        return EffectSummaryArtifactSource.FromContract(contract)!;
    }
}
