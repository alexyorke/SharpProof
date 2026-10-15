using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendCoreElisionTerminalTests
{
    [TestCase("conditional-both-terminal", false)]
    [TestCase("conditional-both-terminal", true)]
    [TestCase("coalesce-before-both-terminal", false)]
    [TestCase("coalesce-before-both-terminal", true)]
    [TestCase("coalesce-terminal-fallback", false)]
    [TestCase("coalesce-terminal-fallback", true)]
    [TestCase("consecutive-both-terminal", false)]
    [TestCase("consecutive-both-terminal", true)]
    public void OmittedCallsRetainTheirRuntimeContinuationWhenArgumentPathsTerminate(string shape, bool partial)
    { Check(shape, partial, enabled: false); }

    [TestCase(false)]
    [TestCase(true)]
    public void EnabledDirectCallsExecuteTheSelectedTerminalArgument(bool partial)
    { Check("conditional-both-terminal", partial, enabled: true); }

    private static void Check(string shape, bool partial, bool enabled)
    {
        var expression = shape switch
        {
            "coalesce-before-both-terminal" =>
                "(value ?? throw new System.ArgumentNullException(nameof(value))).Length > 0 ? " +
                "throw new System.NullReferenceException() : throw new System.InvalidOperationException()",
            "coalesce-terminal-fallback" =>
                "value ?? (flag ? throw new System.NullReferenceException() : throw new System.InvalidOperationException())",
            _ => "flag ? throw new System.NullReferenceException() : throw new System.InvalidOperationException()"
        };
        var parameterType = shape == "coalesce-terminal-fallback" ? "string" : "bool";
        var calls = "Log(" + expression + ");";
        if (shape == "consecutive-both-terminal")
        {
            calls += "Log(!flag ? throw new System.ArgumentException() : throw new System.Exception());";
        }
        var declaration = partial
            ? "static partial void Log(" + parameterType + " argument);" +
                (enabled ? "static partial void Log(" + parameterType + " argument) { }" : "")
            : "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(" + parameterType + " argument) { }";
        var source = "#nullable enable\npublic static partial class Subject { " +
            "public static int Target(string? value, bool flag) { int result = 11; " + calls +
            " result += 6; return result; } " + declaration + " }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: enabled ? ["ABSENT"] : []), shape + ".cs");
        var compilation = CSharpCompilation.Create("TerminalElision_" + shape + "_" + partial, [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(context.HasScalarSignature, Is.True);
        var runtime = new AssemblyLoadContext("TerminalElision_" + shape + "_" + partial, isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in new string?[] { null, "", "word" })
            {
                foreach (var flag in new[] { false, true })
                {
                    if (enabled)
                    {
                        var fault = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [input, flag]));
                        Assert.That(fault!.InnerException, Is.TypeOf(flag ? typeof(NullReferenceException) : typeof(InvalidOperationException)));
                    }
                    else
                    { Assert.That(method.Invoke(null, [input, flag]), Is.EqualTo(17)); }
                }
            }

            var emission = new CSharpInvocationEmissionPolicy(compilation);
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(invocation => model.GetOperation(invocation)).OfType<IInvocationOperation>().ToArray();
            Assert.That(invocations, Has.Length.EqualTo(shape == "consecutive-both-terminal" ? 2 : 1));
            foreach (var invocation in invocations)
            {
                Assert.That(invocation.TargetMethod.IsConditional, Is.EqualTo(!partial));
                if (partial)
                { Assert.That(invocation.TargetMethod.PartialImplementationPart != null, Is.EqualTo(enabled)); }
                Assert.That(emission.IsElided(invocation, requireOwningInvocation: true), Is.EqualTo(!enabled));
            }
            var thrown = graph.Blocks.Where(block =>
                block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw).ToArray();
            Assert.That(thrown.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var block in thrown)
            {
                Assert.That(block.BranchValue, Is.Not.Null);
                Assert.That(emission.IsElided(block.BranchValue!), Is.EqualTo(!enabled));
            }
            var mutation = (ICompoundAssignmentOperation)model.GetOperation(
                syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single())!;
            Assert.That(emission.IsElided(mutation), Is.False);

            // Freeze the actual runtime contract before the first CI attempt.
            // Omitted arguments return; enabled arguments throw before Log.
            var lowerer = new RoslynProgramLowerer(factory);
            var lowered = enabled
                ? lowerer.LowerCandidate(graph, context, static _ => true, null, CancellationToken.None)
                : lowerer.LowerCandidate(graph, context);
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            foreach (var input in new string?[] { null, "", "word" })
            {
                foreach (var flag in new[] { false, true })
                {
                    var valueParameter = context.Parameters[0].Entry;
                    var flagParameter = context.Parameters[1].Entry;
                    var type = factory.GetVariableInfo(valueParameter).Type;
                    var value = input == null ? factory.CreateNullValue(type) : factory.CreateStringValue(input);
                    var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                        new Dictionary<IrVarId, IrValue>
                        {
                            [valueParameter] = value,
                            [flagParameter] = factory.CreateBooleanValue(flag)
                        });
                    Assert.That(replay.Status, Is.EqualTo(enabled
                        ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
                    if (enabled)
                    {
                        Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Explicit));
                        var span = factory.GetOperationInfo(replay.Instruction!.Operation).SourceSpan!;
                        Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo(flag
                            ? "new System.NullReferenceException()" : "new System.InvalidOperationException()"));
                    }
                    else
                    { Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17))); }
                    Assert.That(replay.ConsumedApproximation, Is.False);
                    Assert.That(replay.ApproximationVariables, Is.Empty);
                }
            }
        }
        finally
        { runtime.Unload(); }
    }
}
