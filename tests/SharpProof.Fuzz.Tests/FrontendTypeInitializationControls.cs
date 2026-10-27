using System.Collections.Immutable;
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

public sealed class FrontendTypeInitializationControls
{
    private static readonly GeneratedCSharpCase Input = new(GeneratedCSharpExpression.Left(), 7, 11, true);

    [TestCase(false)]
    [TestCase(true)]
    public void ActualTypeInitializationPreservesItsClrAndExpressionOutcomes(bool fault)
    {
        var initializer = fault
            ? "State = 7; throw new System.InvalidOperationException(\"actual-type-initializer\");"
            : "State = 7;";
        var source = "#nullable enable\npublic static class SharpProofGeneratedFrontend {\n" +
            "public static int State; static SharpProofGeneratedFrontend() { " + initializer + " }\n" +
            "public static long Target0(long left, long right, bool condition, string? text, " +
            "long[]? values, object? reference) => left;\n}\n";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("FrontendTypeInitializationPremise", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var symbol = model.GetDeclaredSymbol(syntax);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.IsStatic, Is.True);
        Assert.That(symbol.Parameters.Length, Is.EqualTo(6));
        Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        var operation = model.GetOperation(syntax.ExpressionBody!.Expression);
        Assert.That(operation, Is.Not.Null);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lowered = FrontendTotalLowering.Lower(factory, operation!);
        Assert.That(lowered.IsExact, Is.True);
        var binding = lowered.Variables.Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(binding.Symbol, symbol.Parameters[0]), Is.True);
        var interpreted = new IrInterpreter(factory).Evaluate(lowered.Term,
            ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(binding.Variable, factory.CreateIntegerValue(factory.GetVariableInfo(binding.Variable).Type, 7)));
        Assert.That(interpreted.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(interpreted.Value!.Integer, Is.EqualTo(7));
        var context = new AssemblyLoadContext("FrontendTypeInitializationPremise", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofGeneratedFrontend");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target0", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            ImmutableArray<object?> arguments = [7L, 11L, true, null, null, null];
            if (fault)
            {
                Action invoke = () => { target!.Invoke(null, arguments.ToArray()); };
                var exception = Assert.Throws<TargetInvocationException>(invoke);
                Assert.That(exception!.InnerException, Is.TypeOf<TypeInitializationException>());
                Assert.That(exception.InnerException!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(exception.InnerException.InnerException!.Message, Is.EqualTo("actual-type-initializer"));
            }
            else
            {
                Assert.That(target!.Invoke(null, arguments.ToArray()), Is.EqualTo(7L));
                Assert.That(runtime.GetField("State")!.GetValue(null), Is.EqualTo(7));
            }
        }
        finally
        {
            context.Unload();
        }
        var result = FrontendDifferentialOracle.CompareSourceBatch(source, [Input], cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(result.Status, Is.EqualTo(fault ? FuzzOracleStatus.Mismatch : FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.ExceptionKind, Is.Null);
        if (fault)
        {
            Assert.That(result.Detail, Does.StartWith("Compiled C# threw TypeInitializationException while the lowered program reported "));
        }
        else
        {
            Assert.That(result.Detail, Is.Empty);
        }
    }
}
