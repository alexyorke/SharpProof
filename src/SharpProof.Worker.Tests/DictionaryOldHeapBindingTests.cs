using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class DictionaryOldHeapBindingTests
{
    // This supplies the provenance of a specialized source frame explicitly.
    // It tests lowering and snapshot evaluation, not worker verification of a
    // source callee's postconditions or support for arbitrary incoming receivers.
    [Test]
    public void SpecializedDictionaryOldReadsEntryHeapWhileOrdinaryReadsSeeUpdates()
    {
        var (compilation, graph, context) = Prepare();
        var parameter = context.Parameters.Single();
        context.SpecializeDictionaryOriginParameter(parameter.Parameter);
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        var clause = binding.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(BoundContractKind.Ensures));
        var lowering = new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());

        var factory = context.Factory;
        var fields = DictionaryFields(factory, clause.Value, clause.SafeCondition);
        var initial = new IrObjectState()
            .WithField(fields["field:dictionary:count"], factory.CreateIntegerValue(1))
            .WithField(fields["field:dictionary:default-comparer"], factory.CreateBooleanValue(true))
            .WithKeyedField(fields["field:dictionary:present"], factory.CreateIntegerValue(1), factory.CreateBooleanValue(true))
            .WithKeyedField(fields["field:dictionary:present"], factory.CreateIntegerValue(2), factory.CreateBooleanValue(false))
            .WithKeyedField(fields["field:dictionary:value"], factory.CreateIntegerValue(1), factory.CreateIntegerValue(17));
        var ownerType = factory.GetVariableInfo(parameter.Entry).Type;
        var execution = new IrProgramInterpreter(factory).Execute(lowering.Program,
            new Dictionary<IrVarId, IrValue> { [parameter.Entry] = factory.CreateReferenceValue(ownerType, initial) },
            10000, new IrProgramReplayOptions(request => request is null ? throw new ArgumentNullException(nameof(request)) : factory.CreateBooleanValue(false))
            { SnapshotVariables = [parameter.PreState] }, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        // Choosing the normal initializer path is still approximation evidence.
        // This test grants no concrete replay authority for that choice.
        Assert.That(execution.ConsumedApproximation, Is.True);
        Assert.That(execution.Heap, Is.Not.Null);
        var interpreter = new IrInterpreter(factory);
        var safe = interpreter.Evaluate(clause.SafeCondition, execution.Values, onVariableRead: null,
            heap: execution.Heap, cancellationToken: TestContext.CurrentContext.CancellationToken, snapshots: [parameter.PreState]);
        var value = interpreter.Evaluate(clause.Value, execution.Values, onVariableRead: null,
            heap: execution.Heap, cancellationToken: TestContext.CurrentContext.CancellationToken, snapshots: [parameter.PreState]);
        Assert.That(safe.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(safe.Value!.Boolean, Is.True);
        Assert.That(value.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(value.Value!.Boolean, Is.True);
        var withoutSnapshot = interpreter.Evaluate(clause.Value, execution.Values, onVariableRead: null,
            heap: execution.Heap, cancellationToken: TestContext.CurrentContext.CancellationToken, snapshots: []);
        Assert.That(withoutSnapshot.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(withoutSnapshot.Value!.Boolean, Is.False, "Old must not silently read the current heap.");

        var clr = new Dictionary<int, int> { [1] = 17 };
        var clrBefore = clr[1];
        clr[1] = 31;
        clr.Add(2, 99);
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(clrBefore));
        Assert.That(clr.Count, Is.EqualTo(2));
        Assert.That(clr[1], Is.EqualTo(31));
        Assert.That(clr.ContainsKey(2), Is.True);
    }

    [Test]
    public void ArbitraryIncomingDictionaryOldStillHasNoConstructorOrigin()
    {
        var (compilation, _, context) = Prepare();
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.False);
        Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(binding.Clauses, Is.Empty);
    }

    private static Dictionary<string, IrMemberId> DictionaryFields(IrFactory factory, params IrTerm[] roots)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(roots);
        var fields = new Dictionary<string, IrMemberId>(StringComparer.Ordinal);
        var pending = new Stack<IrTerm>(roots);
        while (pending.TryPop(out var term))
        {
            if (term is IrOpaqueTerm opaque)
            {
                var name = factory.GetString(factory.GetMemberInfo(opaque.Member).Name);
                if (name.StartsWith("field:dictionary:", StringComparison.Ordinal))
                { fields.TryAdd(name, opaque.Member); }
            }
            IrTraversal.PushChildren(term, pending);
        }
        return fields;
    }

    private static (Compilation Compilation, ControlFlowGraph Graph, TotalLoweringContext Context) Prepare()
    {
        var compilation = TestCompilation.Create("DictionaryOldHeapBinding", """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(System.Collections.Generic.Dictionary<int, int> data) {
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(data[1]) && data[1] == 31 &&
                        data.Count == Contract.Old(data.Count) + 1 && data.ContainsKey(2) && !Contract.Old(data.ContainsKey(2)));
                    var before = data[1];
                    data[1] = 31;
                    data.Add(2, 99);
                    return before;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), (IMethodSymbol)model.GetDeclaredSymbol(syntax)!);
        return (compilation, ControlFlowGraph.Create(syntax, model)!, context);
    }
}
