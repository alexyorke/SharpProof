using System.Collections.Immutable;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AnalyzerCorePublicDriverSharedTreeBoundaryTests
{
    private static readonly ImmutableArray<string> RefusalDiagnosticIds = ["SP0047", "SP0047", "SPCF0007"];

    [TestCase(false)]
    [TestCase(true)]
    public async Task RealCompilationReferenceTreeOwnershipReportsPublicRefusal(bool sharedTree)
    {
        const string targetSource = """
            public abstract class Subject
            {
                public abstract int Target();
            }
            """;
        const string companionSource = """
            [SharpProof.Attributes.ContractFor(typeof(Subject))]
            public static class Specification
            {
                public static int Target(Subject receiver) { return 0; }
            }
            """;
        var rootTargetTree = CSharpSyntaxTree.ParseText(targetSource, path: "root-target.cs");
        var referencedTargetTree = CSharpSyntaxTree.ParseText(targetSource, path: "dependency-target.cs");
        var rootCompanionTree = CSharpSyntaxTree.ParseText(companionSource, path: "companion.cs");
        var referencedCompanionTree = sharedTree
            ? rootCompanionTree
            : CSharpSyntaxTree.ParseText(companionSource, path: "companion.cs");
        var dependency = CSharpCompilation.Create("PublicDriverTreeDependency",
            [referencedTargetTree, referencedCompanionTree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(dependency.GetDiagnostics().Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var dependencyImage = new MemoryStream();
        var dependencyEmit = dependency.Emit(dependencyImage);
        Assert.That(dependencyEmit.Success, Is.True, string.Join(Environment.NewLine, dependencyEmit.Diagnostics));
        var reference = dependency.ToMetadataReference();
        Assert.That(reference.Compilation, Is.SameAs(dependency));

        var compilation = CSharpCompilation.Create("PublicDriverTreeConsumer",
            [rootTargetTree, rootCompanionTree], TestMetadataReferences.WithSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compilerDiagnostics = compilation.GetDiagnostics();
        TestContext.WriteLine(string.Join(Environment.NewLine, compilerDiagnostics));
        Assert.That(compilerDiagnostics.Where(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(compilation.ContainsSyntaxTree(rootCompanionTree), Is.True);
        Assert.That(dependency.ContainsSyntaxTree(rootCompanionTree), Is.EqualTo(sharedTree));
        Assert.That(ReferenceEquals(rootCompanionTree, referencedCompanionTree), Is.EqualTo(sharedTree));
        Assert.That(rootCompanionTree.ToString(), Is.EqualTo(referencedCompanionTree.ToString()));
        var companion = compilation.GetTypeByMetadataName("Specification")!;
        var target = compilation.GetTypeByMetadataName("Subject")!;
        Assert.That(SymbolEqualityComparer.Default.Equals(companion.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        var attribute = companion.GetAttributes().Single();
        Assert.That(attribute.ApplicationSyntaxReference!.SyntaxTree, Is.SameAs(rootCompanionTree));
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
            compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute")), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.ConstructorArguments.Single().Value as ITypeSymbol,
            target), Is.True);
        var method = companion.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var targetMethod = target.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(rootCompanionTree));
        Assert.That(compilation.GetSemanticModel(rootCompanionTree).Compilation, Is.SameAs(compilation));
        var referencedAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(referencedAssembly, compilation.Assembly), Is.False);
        Assert.That(referencedAssembly.Name, Is.EqualTo("PublicDriverTreeDependency"));

        var analyzer = new SharpProofAnalyzer();
        var exceptions = new ConcurrentQueue<string>();
        var driver = compilation.WithAnalyzers([analyzer], new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([], new OptionsProvider()),
            (exception, _, _) => { System.ArgumentNullException.ThrowIfNull(exception); exceptions.Enqueue(exception.ToString()); },
            concurrentAnalysis: true, logAnalyzerExecutionTime: true,
            reportSuppressedDiagnostics: true));
        Assert.That(driver.Analyzers.Single(), Is.SameAs(analyzer));
        var diagnostics = await driver.GetAnalyzerDiagnosticsAsync();
        TestContext.WriteLine(string.Join(Environment.NewLine, diagnostics));
        Assert.That(exceptions, Is.Empty);
        if (sharedTree)
        {
            Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
                Is.EquivalentTo(RefusalDiagnosticIds));
            var companionDiagnostics = diagnostics.Where(diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (ReferenceEquals(diagnostic.Location.SourceTree, rootCompanionTree))).ToArray();
            Assert.That(companionDiagnostics, Has.Length.EqualTo(2));
            foreach (var diagnostic in companionDiagnostics)
            {
                Assert.That(diagnostic.IsSuppressed, Is.False);
                Assert.That(diagnostic.Location.SourceTree, Is.SameAs(rootCompanionTree));
                Assert.That(diagnostic.Location.SourceSpan, Is.EqualTo(method.Locations.Single().SourceSpan));
                Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("Target"));
            }
            Assert.That(companionDiagnostics.Single(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "SP0047"))
                .GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("ContractApiIdentityRejected"));
            var targetDiagnostic = diagnostics.Single(diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (ReferenceEquals(diagnostic.Location.SourceTree, rootTargetTree)));
            Assert.That(targetDiagnostic.Id, Is.EqualTo("SP0047"));
            Assert.That(targetDiagnostic.IsSuppressed, Is.False);
            Assert.That(targetDiagnostic.Location.SourceSpan, Is.EqualTo(targetMethod.Locations.Single().SourceSpan));
            Assert.That(targetDiagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo("SharpProof could not completely analyze selected method 'Target': MissingOperationRoot"));
        }
        else
        {
            Assert.That(diagnostics, Is.Empty);
        }
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
