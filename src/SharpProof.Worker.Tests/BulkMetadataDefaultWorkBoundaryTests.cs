using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkMetadataDefaultWorkBoundaryTests
{
    private static readonly ImmutableArray<int> DivisionInputs = [3, 2];
    private static readonly ImmutableArray<int> MetadataInputs = [3];
    private static readonly ImmutableArray<bool> ExpectedPreconditions = [true];

    [Test]
    public void NaturalMetadataDivisionStopsAtTheDefaultFaultWorkBoundary()
    {
        const string caller = "public static class Subject { public static int Target(int x,int y) => Library.Target(x,y); }";
        using var candidate = new MetadataTestSubject(DivisionLibrary(125), caller);
        Assert.That(candidate.Method.Invoke(null, [3, 2]), Is.EqualTo(-2));
        Assert.That(candidate.RootMethod.Invoke(null, [3, 2]), Is.EqualTo(-2));
        var fault = Assert.Throws<TargetInvocationException>(new Action(() => candidate.RootMethod.Invoke(null, [3, 0])));
        Assert.That(fault!.InnerException, Is.InstanceOf<DivideByZeroException>());
        AssertCallerShape(candidate.Compilation, discarded: false);
        var references = Capture(candidate.Compilation);
        var body = Resolve(candidate, references);
        AssertInstructions(body, DivisionInstructions(125), 381);
        Assert.That(body.MaximumStack, Is.EqualTo(2));
        AssertRejected(candidate.Compilation, references);

        // An odd number of unchecked negations has the same integer semantics.
        // This cheap control must complete, not merely fail at a later debit.
        using var control = new MetadataTestSubject(DivisionLibrary(1), caller);
        Assert.That(control.Method.Invoke(null, [3, 2]), Is.EqualTo(-2));
        Assert.That(control.RootMethod.Invoke(null, [3, 2]), Is.EqualTo(-2));
        AssertCompleteControl(control, DivisionInputs, -2);
    }

    [Test]
    public void NaturalMetadataRequiresStopsAtTheDefaultClauseWorkBoundary()
    {
        const string caller = "public static class Subject { public static int Target(int x) { Library.Target(x); return x; } }";
        using var candidate = new MetadataTestSubject(MetadataLibrary(168), caller);
        Assert.That(candidate.Method.Invoke(null, [3]), Is.EqualTo(171));
        Assert.That(candidate.RootMethod.Invoke(null, [3]), Is.EqualTo(3));
        AssertCallerShape(candidate.Compilation, discarded: true);
        var references = Capture(candidate.Compilation);
        var body = Resolve(candidate, references);
        AssertInstructions(body, MetadataInstructions(168), 680);
        Assert.That(body.MaximumStack, Is.EqualTo(2));
        AssertRejected(candidate.Compilation, references);

        using var control = new MetadataTestSubject(MetadataLibrary(1), caller);
        Assert.That(control.Method.Invoke(null, [3]), Is.EqualTo(4));
        Assert.That(control.RootMethod.Invoke(null, [3]), Is.EqualTo(3));
        AssertCompleteControl(control, MetadataInputs, 3);
    }

    private static string DivisionLibrary(int negations)
    {
        return "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int x,int y) { unchecked {" +
            string.Concat(Enumerable.Repeat("x=-x;", negations)) + "return ~(-x)/y; } } }";
    }

    private static string MetadataLibrary(int additions)
    {
        return "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int x) { unchecked {" +
            string.Concat(Enumerable.Repeat("x=x+1;", additions)) + "x=-x;x=-x;return x; } } }";
    }

    private static ImmutableArray<(string Code, long Operand)> DivisionInstructions(int negations)
    {
        var expected = ImmutableArray.CreateBuilder<(string Code, long Operand)>();
        for (var index = 0; index < negations; index++)
        {
            expected.Add(("Ldarg", 0));
            expected.Add(("Neg", 0));
            expected.Add(("Starg", 0));
        }
        expected.Add(("Ldarg", 0));
        expected.Add(("Neg", 0));
        expected.Add(("Not", 0));
        expected.Add(("Ldarg", 1));
        expected.Add(("Div", 0));
        expected.Add(("Ret", 0));
        return expected.ToImmutable();
    }

    private static ImmutableArray<(string Code, long Operand)> MetadataInstructions(int additions)
    {
        var expected = ImmutableArray.CreateBuilder<(string Code, long Operand)>();
        for (var index = 0; index < additions; index++)
        {
            expected.Add(("Ldarg", 0));
            expected.Add(("Ldc_i4", 1));
            expected.Add(("Add", 0));
            expected.Add(("Starg", 0));
        }
        for (var index = 0; index < 2; index++)
        {
            expected.Add(("Ldarg", 0));
            expected.Add(("Neg", 0));
            expected.Add(("Starg", 0));
        }
        expected.Add(("Ldarg", 0));
        expected.Add(("Ret", 0));
        return expected.ToImmutable();
    }

    private static void AssertCallerShape(CSharpCompilation compilation, bool discarded)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var graph = ControlFlowGraph.Create(declaration, model)!;
        Assert.That(RoslynTotalProgramLowerer.MaximumRegionSteps, Is.EqualTo(4096));
        Assert.That(graph.Root.Kind, Is.EqualTo(ControlFlowRegionKind.Root));
        Assert.That(graph.Root.NestedRegions, Is.Empty);
        Assert.That(graph.Blocks, Has.Length.EqualTo(3));
        Assert.That(graph.Blocks[0].Kind, Is.EqualTo(BasicBlockKind.Entry));
        Assert.That(graph.Blocks[2].Kind, Is.EqualTo(BasicBlockKind.Exit));
        Assert.That(graph.Blocks.All(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionalSuccessor == null)), Is.True);
        Assert.That(graph.Blocks[0].FallThroughSuccessor!.Destination, Is.SameAs(graph.Blocks[1]));
        Assert.That(graph.Blocks[1].FallThroughSuccessor!.Destination, Is.SameAs(graph.Blocks[2]));
        Assert.That(graph.Blocks[1].FallThroughSuccessor!.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Return));
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        IInvocationOperation invocation;
        if (discarded)
        {
            Assert.That(graph.Blocks[1].Operations, Has.Length.EqualTo(1));
            Assert.That(graph.Blocks[1].Operations[0], Is.InstanceOf<IExpressionStatementOperation>());
            invocation = (IInvocationOperation)((IExpressionStatementOperation)graph.Blocks[1].Operations[0]).Operation;
            Assert.That(graph.Blocks[1].BranchValue, Is.InstanceOf<IParameterReferenceOperation>());
        }
        else
        {
            Assert.That(graph.Blocks[1].Operations, Is.Empty);
            Assert.That(graph.Blocks[1].BranchValue, Is.InstanceOf<IInvocationOperation>());
            invocation = (IInvocationOperation)graph.Blocks[1].BranchValue!;
        }
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.TargetMethod.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(method.Parameters.Length));
        for (var index = 0; index < invocation.Arguments.Length; index++)
        {
            var argument = invocation.Arguments[index];
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
            Assert.That(argument.Parameter!.Ordinal, Is.EqualTo(index));
            Assert.That(argument.Value, Is.InstanceOf<IParameterReferenceOperation>());
            var parameter = ((IParameterReferenceOperation)argument.Value).Parameter;
            Assert.That(SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[index]), Is.True);
        }
    }

    private static ImmutableArray<CompilerReferenceSnapshot> Capture(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
    }

    private static TotalIlBody Resolve(MetadataTestSubject subject, IReadOnlyList<CompilerReferenceSnapshot> references)
    {
        System.ArgumentNullException.ThrowIfNull(references, nameof(references));
        System.ArgumentNullException.ThrowIfNull(subject);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var body = new CompilerTotalIlBodyProvider(subject.Compilation, references).Resolve(method, TestContext.CurrentContext.CancellationToken)!;
        Assert.That(body, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(body.Method, method), Is.True);
        Assert.That(body.ImageSha256, Is.EqualTo(WorkerProtocolJson.ComputeSha256(subject.Image)));
        Assert.That(body.ModuleMvid, Is.Not.Empty);
        Assert.That(body.Locals, Is.Empty);
        Assert.That(body.ParameterAttributes, Has.Length.EqualTo(1));
        Assert.That(body.ParameterAttributes[0].Kind, Is.EqualTo("Positive"));
        Assert.That(body.ParameterAttributes[0].ParameterOrdinal, Is.Zero);
        return body;
    }

    private static void AssertInstructions(TotalIlBody body, ImmutableArray<(string Code, long Operand)> expected, int count)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        if (expected.IsDefault)
        { throw new System.ArgumentException("Array must be initialized.", nameof(expected)); }
        Assert.That(expected, Has.Length.EqualTo(count));
        Assert.That(body.Instructions.Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : ((instruction.Code, instruction.Operand))), Is.EqualTo(expected));
        Assert.That(body.Instructions.All(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Target == -1 && instruction.Method == null)), Is.True);
    }

    private static CompilerShadowPreparationBatch Prepare(CSharpCompilation compilation, IReadOnlyList<CompilerReferenceSnapshot> references)
    {
        System.ArgumentNullException.ThrowIfNull(references, nameof(references));
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
    }

    private static void AssertRejected(CSharpCompilation compilation, IReadOnlyList<CompilerReferenceSnapshot> references)
    {
        System.ArgumentNullException.ThrowIfNull(references, nameof(references));
        System.ArgumentNullException.ThrowIfNull(compilation);
        var batch = Prepare(compilation, references);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps, Has.Length.EqualTo(1));
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
    }

    private static void AssertCompleteControl(MetadataTestSubject subject, ImmutableArray<int> inputs, int expected)
    {
        if (inputs.IsDefault)
        {
            throw new ArgumentException("Array must be initialized.", nameof(inputs));
        }
        System.ArgumentNullException.ThrowIfNull(subject);
        AssertCallerShape(subject.Compilation, discarded: inputs.Length == 1);
        var references = Capture(subject.Compilation);
        _ = Resolve(subject, references);
        var batch = Prepare(subject.Compilation, references);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Callers, Has.Length.EqualTo(1));
        var prepared = batch.Callers.Single().Body;
        Assert.That(prepared.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(prepared.CallPreconditions.Single().MetadataClause, Is.Not.Null);
        var artifact = CompilerTotalCallableArtifactCodec.Encode(prepared)!;
        var decoded = CompilerDecodedShadowBody.Decode(prepared.CallableId, artifact, TestContext.CurrentContext.CancellationToken, references).Body;
        var factory = decoded.Program.Factory;
        var arguments = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty;
        for (var index = 0; index < inputs.Length; index++)
        {
            var entry = decoded.Parameters[index].Entry;
            arguments = arguments.Add(entry, factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, inputs[index]));
        }
        var observed = new List<bool>();
        var marker = decoded.CallPreconditions.Single().Instruction;
        var options = new IrProgramReplayOptions(static _ => null)
        {
            AssignmentObserver = (instruction, value, _) =>
            {
                System.ArgumentNullException.ThrowIfNull(instruction);
                System.ArgumentNullException.ThrowIfNull(value);
                if (instruction.Id == marker)
                { observed.Add(value.Boolean); }
            }
        };
        var execution = new IrProgramInterpreter(factory).Execute(decoded.Program, arguments, 10000, options);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(observed, Is.EqualTo(ExpectedPreconditions));
    }
}
