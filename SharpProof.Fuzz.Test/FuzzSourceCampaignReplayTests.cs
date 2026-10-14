using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceCampaignReplayTests
{
    private static readonly FuzzOracleStatus[] FrontendStatuses =
        [FuzzOracleStatus.Agreement, FuzzOracleStatus.Mismatch, FuzzOracleStatus.Agreement, FuzzOracleStatus.Agreement];
    private static readonly FuzzOracleStatus[] FiniteStatuses =
        [FuzzOracleStatus.Abstained, FuzzOracleStatus.Agreement, FuzzOracleStatus.Agreement, FuzzOracleStatus.Agreement];
    private static readonly FuzzOracleStatus[] PartialStatuses =
        [FuzzOracleStatus.Agreement, FuzzOracleStatus.Agreement, FuzzOracleStatus.Mismatch, FuzzOracleStatus.Agreement];
    private static readonly (int Case, string Oracle)[] FailureKeys =
        [(0, "finite-domain-smt"), (1, "frontend"), (2, "partial-term-smt")];
    private static readonly string[] FaultMethodNames = ["Target", "NormalCompletion"];

    private const string HealthyFinite = "public static bool Target(long value) => value >= 0;";
    private const string OpaqueFinite = "public static bool Target(long value) => IsPositive(value + 1); " +
        "private static bool IsPositive(long value) => value > 0;";
    private const string HealthyPartial = "public static bool Target(long value) => value >= 0; " +
        "public static bool NormalCompletion(long value) => true;";
    private const string OpaquePartial = "public static bool Target(long value) => IsPositive(value + 1); " +
        "private static bool IsPositive(long value) => value > 0; " +
        "public static bool NormalCompletion(long value) => true;";

    [Test]
    public async Task ActualSourceCohortSharesCountsAndRetainsEachRealOracleFailure()
    {
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var surrogate = new string('\uD800', 1);
        var negative = new GeneratedCSharpCase(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Equal,
            GeneratedCSharpExpression.String(surrogate), GeneratedCSharpExpression.Text()), 0, 0, true)
        {
            Text = surrogate
        };
        FuzzSourceCampaignCase[] sources =
        [
            new(positive, OpaqueFinite, [0L], HealthyPartial, [[0L]]),
            new(negative, HealthyFinite, [0L], HealthyPartial, [[0L]]),
            new(positive, HealthyFinite, [0L], OpaquePartial, [[0L]]),
            new(positive, HealthyFinite, [0L], HealthyPartial, [[0L]])
        ];
        var result = await FuzzRunner.ReplaySourceCasesAsync(sources);
        Assert.That(result.Cases, Is.EqualTo(4));
        Assert.That(result.Agreements, Is.EqualTo(1));
        Assert.That(result.Abstentions, Is.EqualTo(1));
        Assert.That(result.FrontendAgreements, Is.EqualTo(3));
        Assert.That(result.SmtAgreements, Is.EqualTo(3));
        Assert.That(result.PartialSmtAgreements, Is.EqualTo(3));
        Assert.That(result.FrontendResults.Select(value => value.Status), Is.EqualTo(FrontendStatuses));
        Assert.That(result.FiniteResults.Select(value => value.Status), Is.EqualTo(FiniteStatuses));
        Assert.That(result.PartialResults.Select(value => value.Status), Is.EqualTo(PartialStatuses));
        Assert.That(result.FiniteResults[0].Detail, Is.EqualTo("SMT abstained: UnsupportedEncoding."));
        Assert.That(result.PartialResults[2].Detail,
            Is.EqualTo("The guarded interpreter disagrees with C# execution in scenario 0."));
        Assert.That(result.Failures.Select(value => (value.Case, value.Oracle)), Is.EqualTo(FailureKeys));
        Assert.That(result.Failures.Select(value => value.Detail), Is.All.Not.Empty);
        var empty = await FuzzRunner.ReplaySourceCasesAsync([]);
        Assert.That(empty.Cases, Is.Zero);
        Assert.That(empty.Failures, Is.Empty);
    }

    [Test]
    public void ActualFaultDoesNotAdmitAnIncorrectNormalCompletionSource()
    {
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        const string partial = "public static bool Target(long value) => 1L / value == 0; " +
            "public static bool NormalCompletion(long value) => true;";
        AssertActualFaultSource(partial);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, HealthyFinite, [0L], partial, [[0L]])]);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(replay);
        Assert.That(exception!.Message,
            Is.EqualTo("Actual C# normal completion disagrees with its reference execution."));
    }

    private static void AssertActualFaultSource(string members)
    {
        var source = "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(methods.Select(method => method.Identifier.ValueText),
            Is.EqualTo(FaultMethodNames));
        var model = compilation.GetSemanticModel(tree);
        foreach (var method in methods)
        {
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.IsStatic, Is.True);
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        }
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            var completion = runtime.GetMethod("NormalCompletion", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            Assert.That(completion, Is.Not.Null);
            Action invokeTarget = () =>
            {
                target!.Invoke(null, [0L]);
            };
            var fault = Assert.Throws<TargetInvocationException>(invokeTarget);
            Assert.That(fault!.InnerException, Is.TypeOf<DivideByZeroException>());
            Assert.That(completion!.Invoke(null, [0L]), Is.EqualTo(true));
        }
        finally
        {
            context.Unload();
        }
    }
}
