using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AnalyzerCorePublicDriverNetModuleBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RealNetModuleAssemblyAttributeRetainsPublicAliasRefusal(bool aliasOnly)
    {
        var payload = ImmutableArray.CreateRange(await File.ReadAllBytesAsync(
            typeof(Contract).Assembly.Location).ConfigureAwait(false));
        var globalReference = MetadataReference.CreateFromImage(payload);
        var aliasReference = globalReference.WithAliases(["contracts"]);
        Assert.That(((AssemblyMetadata)aliasReference.GetMetadata()).GetModules().Single().GetModuleVersionId(),
            Is.EqualTo(((AssemblyMetadata)globalReference.GetMetadata()).GetModules().Single().GetModuleVersionId()));
        Assert.That(globalReference.Properties.Aliases, Is.Empty);
        Assert.That(aliasReference.Properties.Aliases, Has.Length.EqualTo(1));
        Assert.That(aliasReference.Properties.Aliases[0], Is.EqualTo("contracts"));

        var moduleTree = CSharpSyntaxTree.ParseText("""
            [assembly: SharpProof.Attributes.SharpProofTrusted("Reviewed ordinary module boundary.")]
            public static class ImportedBoundary { }
            """, path: "module.cs");
        var moduleCompilation = CSharpCompilation.Create("PublicDriverTrustedBoundary",
            [moduleTree], TestMetadataReferences.WithoutSharpProof.Add(globalReference),
            new CSharpCompilationOptions(OutputKind.NetModule));
        Assert.That(moduleCompilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var sourceAttribute = moduleCompilation.Assembly.GetAttributes().Single(static attribute =>
            attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass?.ToDisplayString() == "SharpProof.Attributes.SharpProofTrustedAttribute"));
        Assert.That(sourceAttribute.ApplicationSyntaxReference!.SyntaxTree, Is.SameAs(moduleTree));
        Assert.That(SymbolEqualityComparer.Default.Equals(sourceAttribute.AttributeClass!.ContainingAssembly,
            moduleCompilation.GetAssemblyOrModuleSymbol(globalReference)), Is.True);
        using var moduleImage = new MemoryStream();
        var moduleEmit = moduleCompilation.Emit(moduleImage);
        Assert.That(moduleEmit.Success, Is.True, string.Join(Environment.NewLine, moduleEmit.Diagnostics));
        var moduleName = moduleCompilation.Assembly.Modules.Single().Name;
        var moduleReference = MetadataReference.CreateFromImage(ImmutableArray.Create<byte>(moduleImage.GetBuffer().AsSpan(0, checked((int)moduleImage.Length))),
            MetadataReferenceProperties.Module, filePath: moduleName);
        Assert.That(moduleReference.Properties.Kind, Is.EqualTo(MetadataImageKind.Module));

        var attributesReference = aliasOnly ? aliasReference : globalReference;
        var tree = CSharpSyntaxTree.ParseText("""
            public abstract class Fixture
            {
                public abstract int Target();
            }
            """, path: "input.cs");
        const string consumerName = "PublicDriverNetModuleConsumer";
        var compilation = CSharpCompilation.Create(consumerName,
            [tree], TestMetadataReferences.WithoutSharpProof.Add(attributesReference).Add(moduleReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));

        var module = (IModuleSymbol)compilation.GetAssemblyOrModuleSymbol(moduleReference)!;
        Assert.That(compilation.AssemblyName, Is.EqualTo(consumerName));
        Assert.That(module.Name, Is.EqualTo(moduleName));
        Assert.That(SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(compilation.Assembly.Modules.Count(), Is.EqualTo(2));
        Assert.That(module.Locations, Is.Not.Empty);
        Assert.That(module.Locations.All(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInMetadata)), Is.True);
        var attribute = compilation.Assembly.GetAttributes().Single(static candidate =>
            candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.AttributeClass?.ToDisplayString() == "SharpProof.Attributes.SharpProofTrustedAttribute"));
        Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo("Reviewed ordinary module boundary."));
        var attributesAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(attributesReference)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass!.ContainingAssembly,
            attributesAssembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeConstructor!.ContainingAssembly,
            attributesAssembly), Is.True);
        var canonical = compilation.GetTypeByMetadataName("SharpProof.Attributes.SharpProofTrustedAttribute");
        Assert.That(canonical, aliasOnly ? Is.Null : Is.Not.Null);
        if (!aliasOnly)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(canonical, attribute.AttributeClass), Is.True);
        }
        var sourceLocation = compilation.Assembly.Locations.Single(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource));
        Assert.That(sourceLocation.SourceTree, Is.SameAs(tree));
        Assert.That(compilation.GetSemanticModel(tree).Compilation, Is.SameAs(compilation));

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
            var diagnostic = diagnostics.Single();
            Assert.That(diagnostic.Id, Is.EqualTo("SP0047"));
            Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain(consumerName).And.Contain("ContractApiIdentityRejected"));
            Assert.That(diagnostic.IsSuppressed, Is.False);
            Assert.That(diagnostic.Location.SourceTree, Is.SameAs(tree));
            Assert.That(diagnostic.Location.SourceSpan, Is.EqualTo(sourceLocation.SourceSpan));
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
