using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AnalyzerCorePublicDriverAliasBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task GenuineAttributesReferenceRetainsPublicAliasRefusal(bool aliasOnly)
    {
        var payload = ImmutableArray.CreateRange(await File.ReadAllBytesAsync(
            typeof(Contract).Assembly.Location).ConfigureAwait(false));
        var globalReference = MetadataReference.CreateFromImage(payload);
        var aliasReference = globalReference.WithAliases(["contracts"]);
        var globalMetadata = (AssemblyMetadata)globalReference.GetMetadata();
        var aliasMetadata = (AssemblyMetadata)aliasReference.GetMetadata();
        Assert.That(aliasMetadata.GetModules().Single().GetModuleVersionId(),
            Is.EqualTo(globalMetadata.GetModules().Single().GetModuleVersionId()));
        Assert.That(globalReference.Properties.Aliases, Is.Empty);
        Assert.That(aliasReference.Properties.Aliases, Has.Length.EqualTo(1));
        Assert.That(aliasReference.Properties.Aliases[0], Is.EqualTo("contracts"));
        var reference = aliasOnly ? aliasReference : globalReference;
        var source = aliasOnly
            ? """
                extern alias contracts;
                [contracts::SharpProof.Attributes.SharpProofTrusted("Reviewed ordinary boundary.")]
                public abstract class Fixture
                {
                    public abstract int Target();
                }
                """
            : """
                [SharpProof.Attributes.SharpProofTrusted("Reviewed ordinary boundary.")]
                public abstract class Fixture
                {
                    public abstract int Target();
                }
                """;
        var tree = CSharpSyntaxTree.ParseText(source, path: "input.cs");
        var compilation = CSharpCompilation.Create("PublicDriverAliasBoundary",
            [tree], TestMetadataReferences.WithoutSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var owner = compilation.GetTypeByMetadataName("Fixture")!;
        var method = owner.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.IsAbstract, Is.True);
        Assert.That(method.GetAttributes(), Is.Empty);
        var attribute = owner.GetAttributes().Single();
        var attributeSyntax = (AttributeSyntax)await attribute.ApplicationSyntaxReference!
            .GetSyntaxAsync().ConfigureAwait(false);
        var model = compilation.GetSemanticModel(tree);
        var referenceAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        Assert.That(model.Compilation, Is.SameAs(compilation));
        Assert.That(attributeSyntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(attribute.AttributeClass!.ToDisplayString(),
            Is.EqualTo("SharpProof.Attributes.SharpProofTrustedAttribute"));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass.ContainingAssembly, referenceAssembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            model.GetSymbolInfo(attributeSyntax).Symbol, attribute.AttributeConstructor), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly), Is.True);
        var canonical = compilation.GetTypeByMetadataName("SharpProof.Attributes.SharpProofTrustedAttribute");
        Assert.That(canonical, aliasOnly ? Is.Null : Is.Not.Null);
        if (!aliasOnly)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(canonical, attribute.AttributeClass), Is.True);
        }

        var analyzer = new SharpProofAnalyzer();
        var exceptions = ImmutableArray.CreateBuilder<string>();
        var driver = compilation.WithAnalyzers([analyzer], new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([], new OptionsProvider()),
            (exception, _, _) => { System.ArgumentNullException.ThrowIfNull(exception); lock (exceptions) { exceptions.Add(exception.ToString()); } },
            concurrentAnalysis: true, logAnalyzerExecutionTime: true,
            reportSuppressedDiagnostics: true));
        Assert.That(driver.Analyzers.Single(), Is.SameAs(analyzer));
        var diagnostics = await driver.GetAnalyzerDiagnosticsAsync();
        Assert.That(exceptions.ToImmutable(), Is.Empty);
        if (aliasOnly)
        {
            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics.Single().Id, Is.EqualTo("SP0047"));
            Assert.That(diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Fixture").And.Contain("ContractApiIdentityRejected"));
            Assert.That(diagnostics.Single().IsSuppressed, Is.False);
            Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(tree));
            Assert.That(diagnostics.Single().Location.SourceSpan, Is.EqualTo(attributeSyntax.Span));
        }
        else
        {
            Assert.That(diagnostics, Is.Empty);
        }
        var telemetry = await driver.GetAnalyzerTelemetryInfoAsync(analyzer, TestContext.CurrentContext.CancellationToken);
        Assert.That(telemetry.CompilationStartActionsCount, Is.EqualTo(1));
        Assert.That(telemetry.SymbolActionsCount, Is.EqualTo(2));
    }

    private sealed class OptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new DictionaryOptions();
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            System.ArgumentNullException.ThrowIfNull(tree);
            return _options;
        }
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            System.ArgumentNullException.ThrowIfNull(textFile);
            return _options;
        }
    }

    private sealed class DictionaryOptions : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.SharpProofProfile")
            {
                value = "strict";
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
