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
        AssertClosedMapping<OutputKind>(
            value => CompilerOptionWireMappings.Map(value));
        AssertClosedMapping<OptimizationLevel>(
            value => CompilerOptionWireMappings.Map(value));
        AssertClosedMapping<Platform>(
            value => CompilerOptionWireMappings.Map(value));
        AssertClosedMapping<NullableContextOptions>(
            value => CompilerOptionWireMappings.Map(value));
        AssertClosedMapping<MetadataImportOptions>(
            value => CompilerOptionWireMappings.Map(value));
        AssertClosedMapping<ReportDiagnostic>(
            value => CompilerOptionWireMappings.Map(value));
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
        AssertInvalid(() => CompilerOptionWireMappings.Map((OutputKind)int.MaxValue));
        AssertInvalid(() => CompilerOptionWireMappings.Map((OptimizationLevel)int.MaxValue));
        AssertInvalid(() => CompilerOptionWireMappings.Map((Platform)int.MaxValue));
        AssertInvalid(() => CompilerOptionWireMappings.Map((NullableContextOptions)int.MaxValue));
        AssertInvalid(() => CompilerOptionWireMappings.Map(
            unchecked((MetadataImportOptions)byte.MaxValue)));
        AssertInvalid(() => CompilerOptionWireMappings.Map((ReportDiagnostic)int.MaxValue));
        AssertInvalid(() => CompilerOptionWireMappings.Map(null!));
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

    private static void AssertClosedMapping<TEnum>(Func<TEnum, object> map)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        Assert.That(
            values.Select(value => map(value).ToString()),
            Is.EqualTo(values.Select(static value => value.ToString())));
    }

    private static void AssertInvalid(Action action)
    {
        Assert.Throws<InvalidOperationException>(action);
    }
}
