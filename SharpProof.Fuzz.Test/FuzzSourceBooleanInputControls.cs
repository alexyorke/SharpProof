using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceBooleanInputControls
{
    private static readonly ImmutableArray<bool> FalsePair = [false, false];
    private const string Finite = "public static bool Target(bool left, bool right) => left || right;";
    private const string Partial = Finite +
        " public static bool NormalCompletion(bool left, bool right) => true;";

    [Test]
    public async Task ActualBooleanInputsPreserveAllFourClrAndInterpreterOutcomes()
    {
        AssertActualBooleanParameters(Finite, FalsePair.ToArray(), false);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var result = await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, Finite, [false, false], Partial,
                [[false, false], [false, true], [true, false], [true, true]])], cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Agreements, Is.EqualTo(1));
        Assert.That(result.Failures, Is.Empty);
        var observed = result.PartialResults.Single();
        Assert.That(observed.ScenarioCount, Is.EqualTo(4));
        Assert.That(observed.UndefinedCount, Is.Zero);
        Assert.That(observed.DefinedFalseCount, Is.EqualTo(1));
        Assert.That(observed.DefinedTrueCount, Is.EqualTo(3));
    }

    [Test]
    public void ActualSeventeenBooleanParametersRespectTheExistingFiniteDomainCap()
    {
        var names = Enumerable.Range(0, 17).Select(index =>
            FormattableString.Invariant($"value{index}")).ToArray();
        var finite = "public static bool Target(" +
            string.Join(", ", names.Select(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : ("bool " + name))) + ") => " +
            string.Join(" || ", names) + ";";
        var inputs = new bool[names.Length];
        AssertActualBooleanParameters(finite, inputs, false);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, finite, inputs.Select(input => (object?)input).ToArray(),
                Partial, [[false, false]])], cancellationToken: TestContext.CurrentContext.CancellationToken);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(replay);
        Assert.That(exception!.Message, Is.EqualTo(
            "The finite source safety condition is not defined and true for every bounded assignment."));
    }

    private static void AssertActualBooleanParameters(string members, bool[] inputs, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        System.ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(
            "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n",
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbol = compilation.GetSemanticModel(tree).GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.IsStatic, Is.True);
        Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(symbol.Parameters, Has.Length.EqualTo(inputs.Length));
        Assert.That(symbol.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType == SpecialType.System_Boolean)), Is.True);
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Invoke(null, inputs.Select(input => (object?)input).ToArray()), Is.EqualTo(expected));
        }
        finally
        {
            context.Unload();
        }
    }
}
