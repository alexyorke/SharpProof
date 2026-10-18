using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ArrayReceiverModelOwnershipTests
{
    [TestCase("++((System.Collections.Generic.IList<int>)values)[0]", 6)]
    [TestCase("((System.Collections.Generic.IList<int>)values)[0] += 3", 8)]
    public void ApproximateArrayInterfaceUpdatesRetainTheirMutation(string expression, int expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) { return " + expression + "; } }");
        var compilation = CSharpCompilation.Create("ApproximateInterfaceUpdate", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
            Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!, context, static _ => true, null,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true, approximateElementReads: true);
        Assert.That(lowered.IsExact, Is.True);
        var parameter = context.Parameters[0].Entry;
        var array = factory.CreateSequenceValue(factory.GetVariableInfo(parameter).Type, [factory.CreateIntegerValue(17)]);
        var writes = new List<IrWriteInstruction>();
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
            new Dictionary<ScopedIrId<IrVariableTag>, IrValue> { [parameter] = array }, 1000,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(5)) { WriteObserver = writes.Add });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
        Assert.That(replay.ConsumedApproximation, Is.True);
        Assert.That(writes.Count(write => write is null ? throw new ArgumentNullException(nameof(write)) : write.Region == IrWriteRegion.Element && write.IsStore), Is.EqualTo(1));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ArraySpecializationRequiresAnUnambiguousSourceModel(bool shared)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) => values.Count; }");
        var compilation = CSharpCompilation.Create("ArrayReceiverOwner", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (shared)
        {
            compilation = compilation.AddReferences(compilation.WithAssemblyName("ArrayReceiverDependency").ToMetadataReference());
        }

        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = CompilationModelProvider.GetRootSemanticModel(compilation, tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var target = model.GetDeclaredSymbol(syntax)!;
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        var admitted = session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph);
        Assert.That(admitted, Is.EqualTo(!shared));
        if (shared)
        {
            Assert.That(frame, Is.Null);
            Assert.That(graph, Is.Null);
        }
        else
        {
            Assert.That(frame, Is.Not.Null);
            Assert.That(graph, Is.Not.Null);
            Assert.That(new RoslynProgramLowerer(factory).LowerCandidate(graph!, frame!).IsExact, Is.True);
        }
    }

    [TestCase("System.Func<int> read = () => values.Count; return read();")]
    [TestCase("int Read() => values.Count; return Read();")]
    public void CapturedParameterReadersKeepOpaqueInterfaceBindings(string body)
    {
        global::System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) { " + body + " } }");
        var compilation = CSharpCompilation.Create("CapturedReceiver", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out _), Is.True);
        Assert.That(factory.GetTypeInfo(factory.GetVariableInfo(frame!.Parameters[0].Entry).Type).Kind, Is.EqualTo(IrTypeKind.Reference));
    }
    [TestCase("return comparer.Compare(1, 2);", true)]
    [TestCase("ref var alias = ref comparer; return comparer.Compare(1, 2);", false)]
    public void DefaultComparerSpecializationRejectsRefAliasedParameterStorage(string body, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => Helper(System.Collections.Generic.Comparer<int>.Default); public static int Helper(System.Collections.Generic.Comparer<int> comparer) { " + body + " } }");
        var compilation = CSharpCompilation.Create("ComparerReceiverOwner", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var invocation = (IInvocationOperation)model.GetOperation(methods[0].ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(methods[0])!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out _), Is.True);
        var comparison = (IInvocationOperation)model.GetOperation(methods[1].Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
        Assert.That(frame!.IsDefaultIntegerComparison(comparison), Is.EqualTo(expected));
    }
}
