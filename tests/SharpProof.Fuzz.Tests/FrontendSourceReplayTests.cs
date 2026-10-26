using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FrontendSourceReplayTests
{
    private const string Parameters =
        "long left, long right, bool condition, string? text, long[]? values, object? reference";
    private const string Header = "#nullable enable\npublic static class SharpProofGeneratedFrontend {\n";
    private static readonly GeneratedCSharpCase First = new(GeneratedCSharpExpression.Left(), 7, 11, true);
    private static readonly GeneratedCSharpCase Second = new(GeneratedCSharpExpression.Right(), 23, 31, false);

    [Test]
    public void ActualExpressionBodyReplaysItsCompiledArgument()
    {
        const string source = Header + "public static long Target0(" + Parameters + ") => left;\n}\n";
        AssertActualCompilation(source, [First], [7], [true]);
        var result = FrontendDifferentialOracle.CompareSourceBatch(source, [First], cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.ExceptionKind, Is.Null);
    }

    [Test]
    public void ActualBlockBodyKeepsTheOperationRejection()
    {
        const string source = Header + "public static long Target0(" + Parameters + ") { return left; }\n}\n";
        AssertActualCompilation(source, [First], [7], [false]);
        var result = FrontendDifferentialOracle.CompareSourceBatch(source, [First], cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(result.Detail, Is.EqualTo("Roslyn did not expose the generated method operation."));
        Assert.That(result.ExceptionKind, Is.Null);
    }

    [Test]
    public void ActualExtraMethodRejectsTheSmallerInputCohort()
    {
        var source = TwoMethodSource();
        AssertActualCompilation(source, [First, Second], [7, 31], [true, true]);
        var result = FrontendDifferentialOracle.CompareSourceBatch(source, [First], cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(result.Detail, Is.EqualTo("Roslyn exposed an unexpected generated method count."));
        Assert.That(result.ExceptionKind, Is.Null);
    }

    [Test]
    public void ActualTwoMethodCohortRetainsMethodAndInputOrder()
    {
        var source = TwoMethodSource();
        AssertActualCompilation(source, [First, Second], [7, 31], [true, true]);
        var results = FrontendDifferentialOracle.CompareSourceBatch(source, [First, Second], cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(results.Length, Is.EqualTo(2));
        Assert.That(results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Status)), Is.All.EqualTo(FuzzOracleStatus.Agreement),
            string.Join(Environment.NewLine, results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Detail))));
        Assert.That(results.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ExceptionKind)), Is.All.Null);
    }

    [Test]
    public void ActualCompilerRejectionStaysWithTheReplayedSourceCohort()
    {
        const string source = "#error actual-source-replay-rejection\n";
        var compilation = CreateCompilation(source);
        Assert.That(compilation.GetDiagnostics().Any(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id == "CS1029")), Is.True);
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.False);
        var results = FrontendDifferentialOracle.CompareSourceBatch(source, [First, Second], cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(results.Length, Is.EqualTo(2));
        foreach (var result in results)
        {
            Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
            Assert.That(result.Detail, Does.StartWith("Generated C# did not compile: "));
            Assert.That(result.Detail, Does.Contain("CS1029"));
            Assert.That(result.Detail, Does.Contain("actual-source-replay-rejection"));
        }
        Assert.That(FrontendDifferentialOracle.CompareSourceBatch(source, [], cancellationToken: TestContext.CurrentContext.CancellationToken), Is.Empty);
    }

    private static string TwoMethodSource()
    {
        return Header +
            "public static long Target1(" + Parameters + ") => right;\n" +
            "public static long Target0(" + Parameters + ") => left;\n}\n";
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        return CSharpCompilation.Create("FrontendSourceReplayPremise", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AssertActualCompilation(string source, ImmutableArray<GeneratedCSharpCase> inputs,
        ImmutableArray<long> expected, ImmutableArray<bool> hasExpressionBody)
    {
        if (expected.IsDefault)
        { throw new ArgumentNullException(nameof(expected)); }
        if (hasExpressionBody.IsDefault)
        { throw new ArgumentNullException(nameof(hasExpressionBody)); }
        if (inputs.IsDefault)
        { throw new ArgumentNullException(nameof(inputs)); }
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = CreateCompilation(source);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .OrderBy(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText), StringComparer.Ordinal).ToImmutableArray();
        Assert.That(methods.Length, Is.EqualTo(inputs.Length));
        var context = new AssemblyLoadContext("FrontendSourceReplayPremise", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofGeneratedFrontend");
            Assert.That(runtime, Is.Not.Null);
            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                var symbol = model.GetDeclaredSymbol(method);
                Assert.That(symbol, Is.Not.Null);
                Assert.That(symbol!.Parameters.Length, Is.EqualTo(6));
                Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int64));
                Assert.That(method.ExpressionBody != null, Is.EqualTo(hasExpressionBody[index]));
                var operation = FrontendDifferentialOracle.GetExpressionOperation(
                    model, method.ExpressionBody?.Expression, TestContext.CurrentContext.CancellationToken);
                Assert.That(operation != null, Is.EqualTo(hasExpressionBody[index]));
                var compiled = runtime!.GetMethod(method.Identifier.ValueText,
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(compiled, Is.Not.Null);
                var input = inputs[index];
                ImmutableArray<object?> arguments = [input.Left, input.Right, input.Condition,
                    input.Text, input.Values, input.Reference];
                Assert.That(compiled!.Invoke(null, arguments.ToArray()), Is.EqualTo(expected[index]));
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
