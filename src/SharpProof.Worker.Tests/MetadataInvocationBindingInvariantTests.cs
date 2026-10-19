using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataInvocationBindingInvariantTests
{
    private static readonly ImmutableArray<int> ExpectedOrdinals = [0, 1];
    private static readonly ImmutableArray<string> ExpectedRecoveryDiagnostics = ["CS1739"];

    [TestCase("Library.Target(value, 7)", ArgumentKind.Explicit)]
    [TestCase("Library.Target(right: 7, left: value)", ArgumentKind.Explicit)]
    [TestCase("Library.Target(value)", ArgumentKind.DefaultValue)]
    public void EmittedMetadataCallsPreserveMatchedBodiesAndParameterOrdinals(string call, ArgumentKind secondKind)
    {
        System.ArgumentNullException.ThrowIfNull(call);
        using var subject = CreateSubject(call);
        var invocation = Invocation(subject.Compilation);
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.TargetMethod.IsStatic, Is.True);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(2));
        Assert.That(invocation.Arguments.Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Parameter!.Ordinal)), Is.EquivalentTo(ExpectedOrdinals));
        Assert.That(invocation.Arguments.Single(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Parameter!.Ordinal == 1)).ArgumentKind, Is.EqualTo(secondKind));
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        var body = provider.Resolve(invocation.TargetMethod, TestContext.CurrentContext.CancellationToken);
        Assert.That(body, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(body!.Method, invocation.TargetMethod), Is.True);
        Assert.That(body.Method.Parameters, Has.Length.EqualTo(2));
        foreach (var argument in invocation.Arguments)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter!.ContainingSymbol, body.Method), Is.True);
        }
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact(), TestContext.CurrentContext.CancellationToken), out var preparations, TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal))).Total;
        Assert.That(total, Is.Not.Null);
        var owned = total!;
        var factory = owned.Program.Factory;
        var entry = owned.Parameters.Single().Entry;
        foreach (var input in ImmutableArray.Create(-17, 0, 23))
        {
            var expected = input - 7;
            Assert.That(subject.InvokeRoot(input), Is.EqualTo(expected));
            var replay = new IrProgramInterpreter(factory).Execute(owned.Program,
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, input)));
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        }
    }

    [Test]
    public void UnknownNamedArgumentRemainsCompilerRecoveryBeforeMetadataInlining()
    {
        using var subject = CreateSubject("Library.Target(value)");
        var tree = CSharpSyntaxTree.ParseText(
            "public static class Recovery { public static int Target(int value) => Library.Target(value, missing: 7); }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "invalid-named.cs");
        var compilation = CSharpCompilation.Create("MetadataBindingRecovery", [tree], subject.Compilation.References,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Is.EqualTo(ExpectedRecoveryDiagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.That(model.GetOperation(syntax), Is.InstanceOf<IInvalidOperation>());
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
        var graph = ControlFlowGraph.Create(method, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var provider = new CompilerTotalIlBodyProvider(compilation, references);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context, static _ => true,
            provider.Resolve, TestContext.CurrentContext.CancellationToken);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.Abstentions, Is.Not.Empty);
    }

    private static MetadataTestSubject CreateSubject(string call)
    {
        System.ArgumentNullException.ThrowIfNull(call);
        return new MetadataTestSubject(
            "public static class Library { public static int Target(int left, int right = 7) => left - right; }",
            "using SharpProof.Attributes; public static class Subject { public static int Target(int value) { Contract.Ensures(true); return " + call + "; } }");
    }

    private static IInvocationOperation Invocation(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "Library.Target"));
        return (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
    }
}
