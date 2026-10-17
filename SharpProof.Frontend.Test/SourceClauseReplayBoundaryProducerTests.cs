using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class SourceClauseReplayBoundaryProducerTests
{
    [Test]
    public void ActualConstantTrueConditionalHasIncompleteTotalClauseAndExactLegacyVariable()
    {
        const string source = "public static bool Target(bool value) => true ? value : Tail(value); " +
            "private static bool Tail(bool value) => value;";
        var produced = InspectActualSource(source);
        Assert.That(produced.Total.Classification.IsExact, Is.False);
        Assert.That(produced.Legacy.Classification.IsExact, Is.True);
        Assert.That(produced.Legacy.Term, Is.TypeOf<IrVariableTerm>());
    }

    [Test]
    public void ActualDeclarationPatternHasIncompleteTotalClauseAndOpaqueLegacyLocalBinding()
    {
        const string source = "public static bool Target(bool value) => value is var captured && captured; " +
            "public static bool NormalCompletion(bool value) => true;";
        var produced = InspectActualSource(source);
        Assert.That(produced.Total.Classification.IsExact, Is.False);
        Assert.That(produced.Legacy.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(produced.Legacy.Variables.Any(binding => binding.Symbol is ILocalSymbol), Is.True);
    }

    private static (GuardedExpression Total, FrontendLoweringResult Legacy) InspectActualSource(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(
            "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n",
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image, cancellationToken: CancellationToken.None);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Identifier.ValueText == "Target"));
        var model = compilation.GetSemanticModel(tree);
        var symbol = model.GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.IsStatic, Is.True);
        Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(symbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        var operation = model.GetOperation(method.ExpressionBody!.Expression);
        Assert.That(operation, Is.Not.Null);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = new TotalLoweringContext(factory, symbol) { Compilation = model.Compilation };
        var total = new RoslynTotalExpressionLowerer(owner).LowerClause(operation!, TotalParameterState.Entry);
        var legacy = new RoslynOperationLowerer(factory).Lower(operation!);
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Invoke(null, [true]), Is.EqualTo(true));
        }
        finally
        {
            context.Unload();
        }
        return (total, legacy);
    }
}
