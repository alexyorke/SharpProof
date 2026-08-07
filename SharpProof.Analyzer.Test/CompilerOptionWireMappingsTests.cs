using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerOptionWireMappingsTests
{
    [Test]
    public void EveryCurrentRoslynCompilerOptionHasAClosedWireMapping()
    {
        Assert.That(
            Enum.GetValues<OutputKind>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<OutputKind>()
                .Select(static value => value.ToString())));
        Assert.That(
            Enum.GetValues<OptimizationLevel>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<OptimizationLevel>()
                .Select(static value => value.ToString())));
        Assert.That(
            Enum.GetValues<Platform>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<Platform>()
                .Select(static value => value.ToString())));
        Assert.That(
            Enum.GetValues<NullableContextOptions>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<NullableContextOptions>()
                .Select(static value => value.ToString())));
        Assert.That(
            Enum.GetValues<MetadataImportOptions>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<MetadataImportOptions>()
                .Select(static value => value.ToString())));
        Assert.That(
            Enum.GetValues<ReportDiagnostic>()
                .Select(value =>
                    CompilerOptionWireMappings.Map(value).ToString()),
            Is.EqualTo(Enum.GetValues<ReportDiagnostic>()
                .Select(static value => value.ToString())));
        Assert.That(
            CompilerOptionWireMappings.Map(AssemblyIdentityComparer.Default),
            Is.EqualTo(CompilerAssemblyIdentityComparer.Default));
        Assert.That(
            CompilerOptionWireMappings.Map(
                DesktopAssemblyIdentityComparer.Default),
            Is.EqualTo(CompilerAssemblyIdentityComparer.Desktop));
    }

    [Test]
    public void FutureCompilerOptionValuesFailClosed()
    {
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map((OutputKind)int.MaxValue)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map((OptimizationLevel)int.MaxValue)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map((Platform)int.MaxValue)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map((NullableContextOptions)int.MaxValue)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map(
                unchecked((MetadataImportOptions)byte.MaxValue))));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map(
                (ReportDiagnostic)int.MaxValue)));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.Map(null!)));
    }

    [Test]
    public void InternalReferenceSupersessionOptionIsReadByProperty()
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        Assert.That(
            CompilerOptionWireMappings.ReadInternalBoolean(
                options,
                "ReferencesSupersedeLowerVersions"),
            Is.False);
        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerOptionWireMappings.ReadInternalBoolean(
                options,
                "MissingCompilerOption")));
    }
}
