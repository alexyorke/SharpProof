using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceProducerGuardControls
{
    private const string Finite = "public static bool Target(bool value) => value;";
    private const string Partial = Finite + " public static bool NormalCompletion(bool value) => true;";

    [Test]
    public void ActualDeadCallClauseCannotSupplyItsPlaceholderAsACompleteFormula()
    {
        const string finite = "public static bool Target(bool value) => true ? value : Tail(value); " +
            "private static bool Tail(bool value) => value;";
        var produced = InspectActualSource(finite);
        Assert.That(produced.Classification.IsExact, Is.True);
        Assert.That(produced.Term, Is.TypeOf<IrVariableTerm>());
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, finite, [true], Partial, [[true]])]);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(replay);
        Assert.That(exception!.Message, Is.EqualTo(
            "An incomplete Total clause has no authentic opaque expression for rejection."));
    }

    [Test]
    public void ActualPatternLocalCannotBeBoundAsAMethodParameter()
    {
        const string partial = "public static bool Target(bool value) => value is var captured && captured; " +
            "public static bool NormalCompletion(bool value) => true;";
        var produced = InspectActualSource(partial);
        Assert.That(produced.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(produced.Variables.Any(binding => binding.Symbol is ILocalSymbol), Is.True);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, Finite, [true], partial, [[true]])]);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(replay);
        Assert.That(exception!.Message, Is.EqualTo("Source replay requires actual method parameter bindings."));
    }

    private static FrontendLoweringResult InspectActualSource(string members)
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
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
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
        return legacy;
    }
}
