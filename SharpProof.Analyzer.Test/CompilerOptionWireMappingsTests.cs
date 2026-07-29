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
                .Select(CompilerOptionWireMappings.Map),
            Is.All.TypeOf<CompilerOutputKind>());
        Assert.That(
            Enum.GetValues<OptimizationLevel>()
                .Select(CompilerOptionWireMappings.Map),
            Is.All.TypeOf<CompilerOptimizationLevel>());
        Assert.That(
            Enum.GetValues<Platform>()
                .Select(CompilerOptionWireMappings.Map),
            Is.All.TypeOf<CompilerPlatform>());
        Assert.That(
            Enum.GetValues<NullableContextOptions>()
                .Select(CompilerOptionWireMappings.Map),
            Is.All.TypeOf<CompilerNullableContext>());
        Assert.That(
            Enum.GetValues<MetadataImportOptions>()
                .Select(CompilerOptionWireMappings.Map),
            Is.All.TypeOf<CompilerMetadataImportOptions>());
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
