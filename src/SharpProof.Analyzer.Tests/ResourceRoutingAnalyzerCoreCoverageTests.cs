using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Compiler.Semantics;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceRoutingAnalyzerCoreCoverageTests
{
    private static readonly string[] ExpectedMethodNames = ["Bound", "WrongKind", "MissingBinding"];

    [Test]
    public void QuantitativeResourceClaimsAreNotDecidedByQualitativeAllocationSites()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public class Subject { public void Target() { } }", []);
        AdvisoryEffectSite[] effects = [new(AdvisoryEffectSiteKind.Allocation, Location.None)];
        Assert.That(AdvisoryEffectClaims.FindViolation(EffectEvaluationContractKind.ResourceUpperBound,
            EffectClaimConstraint.Empty, [.. effects], compilation), Is.Null);
        var allocation = AdvisoryEffectClaims.FindViolation(EffectEvaluationContractKind.ZeroAllocations,
            EffectClaimConstraint.Empty, [.. effects], compilation);
        Assert.That(allocation, Is.Not.Null);
        Assert.That(allocation!.Value.Detail, Is.EqualTo("may allocate"));
    }

    [Test]
    public async Task ResourceDiagnosticsRequireAMethodBindingBeforePublishingBounds()
    {
        const string source = "public class Subject { public int Value; public int Bound() => 7; " +
            "public int WrongKind() => 3; public int MissingBinding() => 5; }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0060"]);
        var analyzer = new ResourceBindingBoundaryAnalyzer();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null, analyzer: analyzer);
        Assert.That(analyzer.Seen, Is.EquivalentTo(ExpectedMethodNames));
        AnalyzerTestHost.AssertIds(diagnostics, "SP0060", 1);
        Assert.That(diagnostics[0].Properties["ResourceStatus"], Is.EqualTo("Proven"));
        Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("Bound"));
    }

    [Test]
    public void ResourceBindingValidationRequiresAnInitializedAnalysisContext()
    {
        Assert.Throws<ArgumentException>((Action)(() => ResourceBoundsDiagnostics.AnalyzeBoundDeclaration(default, null)));
    }

#pragma warning disable RS1001, RS1036, RS1041, RS2008
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ResourceBindingBoundaryAnalyzer : DiagnosticAnalyzer
    {
        private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.Ordinal);
        internal ImmutableArray<string> Seen => [.. _seen.Keys];
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ResourceBoundsDiagnostics.Rule];

        public override void Initialize(AnalysisContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(nodeContext =>
            {
                if (nodeContext.Node is null)
                { throw new ArgumentException("Value must be initialized.", nameof(nodeContext)); }
                var declaration = (MethodDeclarationSyntax)nodeContext.Node;
                var name = declaration.Identifier.ValueText;
                ISymbol? binding = name switch
                {
                    "MissingBinding" => null,
                    "WrongKind" => nodeContext.Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Value").Single(),
                    _ => nodeContext.SemanticModel.GetDeclaredSymbol(declaration, nodeContext.CancellationToken)
                };
                _seen.TryAdd(name, 0);
                ResourceBoundsDiagnostics.AnalyzeBoundDeclaration(nodeContext, binding);
            }, SyntaxKind.MethodDeclaration);
        }
    }
#pragma warning restore RS1001, RS1036, RS1041, RS2008
}
