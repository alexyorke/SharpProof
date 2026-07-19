using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Identity;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummarySchemaV5Tests
{
    [Test]
    public async Task WriterAndAnalyzer_UseStructuralIdentityAndTypedExceptionProvenance()
    {
        const string source = """
                              using System;
                              using System.Diagnostics.CodeAnalysis;

                              public static class V5Fixture
                              {
                                  public static string Leaf(string value) =>
                                      value ?? throw new InvalidOperationException();

                                  public static string Outer(string value) => Leaf(value);

                                  [return: NotNullIfNotNull(nameof(value))]
                                  public static string? Echo(string? value) => value;

                                  public static bool IsPresent([NotNullWhen(true)] string? value) =>
                                      value is not null;
                              }
                              """;

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummarySchemaV5Fixture",
            source);
        using var summary = await EffectSummaryToolTests.RunEffectSummaryAsync(
            fixture.AssemblyPath,
            includeTransitiveRoots: true,
            classifyPurity: true);

        var root = summary.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(5));
        Assert.That(root.GetProperty("EvidenceSchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(root.GetProperty("EvidenceSchemaCompatibility").GetString(), Is.EqualTo("exact-v2"));

        var outer = root.GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Single(method => string.Equals(
                method.GetProperty("DisplayName").GetString(),
                "V5Fixture.Outer(string)",
                StringComparison.Ordinal));
        Assert.That(outer.TryGetProperty("Symbol", out _), Is.False);
        Assert.That(outer.TryGetProperty("ExactSymbolKey", out _), Is.False);
        Assert.That(StructuralMethodIdentityJson.TryReadMethod(outer, out var outerIdentity, out var outerKey),
            Is.True);
        Assert.That(outerKey, Does.StartWith(StructuralMethodIdentity.KeyPrefix + "|"));
        Assert.That(outerIdentity.ContainingMetadataType, Is.EqualTo("V5Fixture"));

        var edge = outer.GetProperty("TransitiveThrownExceptionEdges").EnumerateArray().Single();
        Assert.That(edge.GetProperty("ExceptionType").GetString(), Is.EqualTo("System.InvalidOperationException"));
        Assert.That(edge.TryGetProperty("SourcePath", out _), Is.False);
        Assert.That(edge.GetProperty("CallChain").GetArrayLength(), Is.EqualTo(2));
        Assert.That(StructuralMethodIdentityJson.TryReadIdentity(
            edge.GetProperty("CalleeIdentity"),
            out var calleeIdentity), Is.True);
        Assert.That(calleeIdentity.Name, Is.EqualTo("Leaf"));

        var generatedEntry = root.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Single(entry => string.Equals(
                entry.GetProperty("DisplayName").GetString(),
                "V5Fixture.Outer(string)",
                StringComparison.Ordinal));
        Assert.That(generatedEntry.TryGetProperty("Symbol", out _), Is.False);
        Assert.That(generatedEntry.TryGetProperty("ExactSymbolKey", out _), Is.False);
        Assert.That(StructuralMethodIdentityJson.TryReadMethod(generatedEntry, out _, out _), Is.True);

        var methods = root.GetProperty("Assemblies")[0].GetProperty("Methods").EnumerateArray().ToArray();
        var echoContracts = methods.Single(method => string.Equals(
                method.GetProperty("DisplayName").GetString(),
                "V5Fixture.Echo(string)",
                StringComparison.Ordinal))
            .GetProperty("NullableContracts");
        Assert.That(
            echoContracts.GetProperty("ReturnNotNullIfNotNullParameter").GetString(),
            Is.EqualTo("value"));

        var isPresentContracts = methods.Single(method => string.Equals(
                method.GetProperty("DisplayName").GetString(),
                "V5Fixture.IsPresent(string)",
                StringComparison.Ordinal))
            .GetProperty("NullableContracts");
        var parameterContract = isPresentContracts.GetProperty("Parameters")[0];
        Assert.That(parameterContract.GetProperty("Ordinal").GetInt32(), Is.EqualTo(0));
        Assert.That(parameterContract.GetProperty("Name").GetString(), Is.EqualTo("value"));
        Assert.That(parameterContract.GetProperty("NotNullWhen").GetBoolean(), Is.True);

        var compilation = CSharpCompilation.Create(
            "EffectSummarySchemaV5Consumer",
            references: AnalyzerTestHost.GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var methodSymbol = compilation.GetTypeByMetadataName("V5Fixture")!
            .GetMembers("Outer")
            .OfType<IMethodSymbol>()
            .Single();
        var additionalText = new AnalyzerTestHost.InMemoryAdditionalText(
            "Fixture.SharpProof.EffectSummary.json",
            root.GetRawText());
        var options = AnalyzerTestHost.CreateAnalyzerOptions(
            additionalFiles: ImmutableArray.Create<AdditionalText>(additionalText));
        var catalog = EffectSummaryCatalog.FromOptions(options, CancellationToken.None);

        Assert.That(catalog.TryGetExceptionInfos(methodSymbol, compilation, out var exceptionInfos), Is.True);
        var exceptionInfo = exceptionInfos.Single(info =>
            string.Equals(info.ExceptionType, "System.InvalidOperationException", StringComparison.Ordinal));
        var catalogEdge = exceptionInfo.Edges.Single();
        Assert.That(catalogEdge.SourcePath, Is.Null);
        Assert.That(catalogEdge.CallChain, Has.Length.EqualTo(2));
        Assert.That(catalogEdge.CalleeIdentity, Is.Not.Null);
        Assert.That(catalogEdge.CalleeIdentity!.Name, Is.EqualTo("Leaf"));
    }

}
