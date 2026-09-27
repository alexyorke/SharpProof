using System.Globalization;
using System.Threading;

namespace SharpProof.Ir;

internal enum IrForwardFailure
{
    None,
    ResourceLimit,
    ExpressionDepth,
    UnsupportedBody,
    UnsupportedInstruction,
    CyclicControlFlow
}

// Forward symbolic execution of an IrProgram, shared by the worker's body
// executor and the relational summary builder. Each block runs once in a
// topological order of the forward edges. Paths merge with if-then-else.
// Loops are cut at their headers: a back edge is `assume false`, and every
// variable the loop may assign gets a fresh value at the header, so one pass
// over the body stands for every iteration.
internal abstract class IrForwardExecutor
{
    private readonly Dictionary<IrBlockId, List<FlowState>> _incoming = [];
    private readonly Dictionary<IrId, int> _termDepths = [];
    private readonly int _maximumExpressionDepth;
    private int _remainingOperations;
    private IrLoopCut? _cut;
    private IrBlockId _current;
    private OperationId? _expectedMemoryHavoc;
    private Dictionary<IrVarId, (long Minimum, long Maximum)?>? _ranges;

    protected IrForwardExecutor(
        IrProgram program,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        int maximumExpressionDepth,
        int maximumOperations,
        CancellationToken cancellationToken)
    {
        Program = program;
        InitialEnvironment = initialEnvironment;
        _maximumExpressionDepth = maximumExpressionDepth;
        _remainingOperations = maximumOperations;
        CancellationToken = cancellationToken;
    }

    protected IrProgram Program { get; }

    protected IrFactory Factory => Program.Factory;

    protected ImmutableDictionary<IrVarId, IrTerm> InitialEnvironment { get; }

    protected CancellationToken CancellationToken { get; }

    protected IrForwardFailure Failure { get; private set; }

    // Runs every reachable block; false with Failure set when a path cannot
    // be modeled.
    protected bool Run()
    {
        CancellationToken.ThrowIfCancellationRequested();
        _cut = IrBlockOrder.TryCutLoops(Program, Spend, out var orderFailure);
        if (_cut == null)
        {
            return Fail(orderFailure switch
            {
                IrAcyclicOrderFailure.ResourceLimit => IrForwardFailure.ResourceLimit,
                IrAcyclicOrderFailure.CyclicControlFlow => IrForwardFailure.CyclicControlFlow,
                IrAcyclicOrderFailure.UnsupportedInstruction => IrForwardFailure.UnsupportedInstruction,
                _ => IrForwardFailure.UnsupportedBody
            });
        }

        foreach (var blockId in _cut.Order)
        {
            CancellationToken.ThrowIfCancellationRequested();
            _current = blockId;
            var state = Merge(blockId);
            if (state == null)
            {
                if (Failure != IrForwardFailure.None)
                {
                    return false;
                }

                continue;
            }

            if (_cut.Loops.TryGetValue(blockId, out var loop))
            {
                state = CutLoop(blockId, loop, state.Value);
                if (state == null)
                {
                    return false;
                }
            }

            if (!ExecuteBlock(Program.GetBlock(blockId), state.Value))
            {
                // Keeps the first recorded reason, if any.
                return Fail(IrForwardFailure.UnsupportedBody);
            }
        }

        CancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    // A call's result and effect on the path; false when it cannot be modeled.
    protected abstract bool ExecuteCall(
        IrCallInstruction call,
        ref IrTerm predicate,
        ref ImmutableDictionary<IrVarId, IrTerm> environment);

    // The path reaches a normal return.
    protected abstract bool ExecuteReturn(
        IrReturnInstruction returned,
        IrTerm predicate,
        ImmutableDictionary<IrVarId, IrTerm> environment);

    // An operation that may throw: `completes` holds when it does not.
    protected virtual void OnThrowSite(IrTerm predicate, IrTerm completes)
    {
    }

    // A value the executor invented (a loop-header or merge unknown).
    protected virtual void OnFreshVariable(IrVarId variable)
    {
    }

    // The source integer range of an initial value, if known.
    protected virtual (long Minimum, long Maximum)? InitialRange(IrTerm initial)
    {
        return null;
    }

    // Accept the memory havoc a modeled effectful call lowers to.
    protected void ExpectMemoryHavoc(OperationId operation)
    {
        _expectedMemoryHavoc = operation;
    }

    protected bool Fail(IrForwardFailure failure)
    {
        if (Failure == IrForwardFailure.None)
        {
            Failure = failure;
        }

        return false;
    }

    protected bool Spend(int amount = 1)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (amount >= 0 && amount <= _remainingOperations)
        {
            _remainingOperations -= amount;
            return true;
        }

        return Fail(IrForwardFailure.ResourceLimit);
    }

    protected bool Supported(IrTerm term)
    {
        if (!_termDepths.TryGetValue(term.Id, out var depth) &&
            !ChargeAndMeasureDepth(term, out depth))
        {
            return false;
        }

        return depth <= _maximumExpressionDepth ||
            Fail(IrForwardFailure.ExpressionDepth);
    }

    // Substitutes the environment; every variable must be bound or free.
    protected IrTerm? Substitute(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrTerm> environment,
        ISet<IrVarId>? freeVariables = null)
    {
        CancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!IrSubstitution.TrySubstitute(Factory, term, environment, freeVariables, out var result))
            {
                Fail(IrForwardFailure.UnsupportedBody);
                return null;
            }

            return Supported(result) ? result : null;
        }
        catch (ArgumentException)
        {
            Fail(IrForwardFailure.UnsupportedBody);
            return null;
        }
    }

    // Adds "evaluated does not throw" to the path and records the throw site.
    protected IrTerm? ConstrainNormalExecution(IrTerm predicate, IrTerm evaluated)
    {
        if (!IrSemanticTerms.RequiresDefinednessWitness(evaluated))
        {
            return predicate;
        }

        if (!Spend(2))
        {
            return null;
        }

        OnThrowSite(predicate, Factory.Binary(IrBinaryOperator.Equal, evaluated, evaluated));
        var constrained = IrSemanticTerms.ConstrainSuccessfulEvaluation(Factory, predicate, evaluated);
        return Supported(constrained) ? constrained : null;
    }

    private bool ExecuteBlock(IrBasicBlock block, FlowState state)
    {
        var environment = state.Environment;
        var predicate = state.Predicate;
        for (var index = 0; index < block.Instructions.Length; index++)
        {
            if (!Spend())
            {
                return false;
            }

            var instruction = block.Instructions[index];
            var last = index == block.Instructions.Length - 1;
            if (_expectedMemoryHavoc is { } expected)
            {
                _expectedMemoryHavoc = null;
                if (instruction is IrHavocInstruction { HavocKind: IrHavocKind.Memory, Variables.IsEmpty: true } havoc &&
                    havoc.Operation == expected)
                {
                    continue;
                }

                return Fail(IrForwardFailure.UnsupportedInstruction);
            }

            switch (instruction)
            {
                case IrAssignInstruction assign:
                    var assigned = Substitute(assign.Value, environment);
                    if (assigned == null ||
                        ConstrainNormalExecution(predicate, assigned) is not { } afterAssign)
                    {
                        return false;
                    }

                    predicate = afterAssign;
                    environment = environment.SetItem(assign.Target, assigned);
                    break;
                case IrAssumeInstruction { Condition: IrBooleanTerm { Value: false } }:
                    // The path does not complete normally (an uncaught throw)
                    // and contributes no return.
                    OnThrowSite(predicate, Factory.Boolean(false));
                    return true;
                case IrAssumeInstruction assume:
                    var assumed = Substitute(assume.Condition, environment);
                    if (assumed == null ||
                        assumed.Type != Factory.BooleanType ||
                        ConstrainNormalExecution(predicate, assumed) is not { } guarded ||
                        !Spend())
                    {
                        return false;
                    }

                    predicate = Factory.Binary(IrBinaryOperator.AndAlso, guarded, assumed);
                    if (!Supported(predicate))
                    {
                        return false;
                    }

                    break;
                case IrCallInstruction call:
                    if (!ExecuteCall(call, ref predicate, ref environment))
                    {
                        return false;
                    }

                    break;
                case IrBranchInstruction branch:
                    return last && TransferBranch(block.Id, branch, predicate, environment);
                case IrGotoInstruction go:
                    AddIncoming(go.Target, block.Id.Value << 1, predicate, environment);
                    return last;
                case IrReturnInstruction returned:
                    return last && ExecuteReturn(returned, predicate, environment);
                default:
                    return Fail(IrForwardFailure.UnsupportedInstruction);
            }
        }

        return false;
    }

    private bool TransferBranch(
        IrBlockId predecessor,
        IrBranchInstruction branch,
        IrTerm predicate,
        ImmutableDictionary<IrVarId, IrTerm> environment)
    {
        var condition = Substitute(branch.Condition, environment);
        if (condition == null || condition.Type != Factory.BooleanType)
        {
            return false;
        }

        if (ConstrainNormalExecution(predicate, condition) is not { } constrained)
        {
            return false;
        }

        predicate = constrained;
        var order = predecessor.Value << 1;
        if (condition is IrBooleanTerm literal)
        {
            AddIncoming(
                literal.Value ? branch.WhenTrue : branch.WhenFalse,
                order + (literal.Value ? 0 : 1),
                predicate,
                environment);
            return true;
        }

        if (!Spend(2))
        {
            return false;
        }

        var whenTrue = Factory.Binary(IrBinaryOperator.AndAlso, predicate, condition);
        var whenFalse = Factory.Binary(
            IrBinaryOperator.AndAlso,
            predicate,
            Factory.Unary(IrUnaryOperator.Not, condition));
        if (!Supported(whenTrue) || !Supported(whenFalse))
        {
            return false;
        }

        AddIncoming(branch.WhenTrue, order, whenTrue, environment);
        AddIncoming(branch.WhenFalse, order + 1, whenFalse, environment);
        return true;
    }

    private void AddIncoming(
        IrBlockId block,
        int order,
        IrTerm predicate,
        ImmutableDictionary<IrVarId, IrTerm> environment)
    {
        // A cut back edge is `assume false`: the loop header already stands
        // for every iteration.
        if (predicate is IrBooleanTerm { Value: false } ||
            _cut!.BackEdges.Contains((_current, block)))
        {
            return;
        }

        if (!_incoming.TryGetValue(block, out var values))
        {
            _incoming.Add(block, values = []);
        }

        values.Add(new FlowState(order, predicate, environment));
    }

    private FlowState? Merge(IrBlockId block)
    {
        if (block == Program.Entry)
        {
            foreach (var term in InitialEnvironment.Values)
            {
                if (!Supported(term))
                {
                    return null;
                }
            }

            return new FlowState(0, Factory.Boolean(true), InitialEnvironment);
        }

        if (!_incoming.TryGetValue(block, out var values) || values.Count == 0)
        {
            return null;
        }

        values.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        if (!Spend(values.Count))
        {
            return null;
        }

        var predicate = IrSemanticTerms.Disjoin(
            Factory,
            values.Select(static value => value.Predicate).ToArray());
        if (!Supported(predicate))
        {
            return null;
        }

        if (values.Count == 1)
        {
            return new FlowState(0, predicate, values[0].Environment);
        }

        var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        var variables = values
            .SelectMany(static value => value.Environment.Keys)
            .Distinct()
            .OrderBy(static value => value.Value);
        foreach (var variable in variables)
        {
            if (!Spend(values.Count))
            {
                return null;
            }

            // A variable some predecessors never assigned is never read on
            // those paths (C# definite assignment), so it takes a fresh value
            // there instead of being dropped.
            var incoming = new IrTerm[values.Count];
            IrTerm? undefined = null;
            for (var index = 0; index < values.Count; index++)
            {
                incoming[index] = values[index].Environment.TryGetValue(variable, out var current)
                    ? current
                    : undefined ??= Fresh("merge-undefined", block, variable);
            }

            var merged = incoming[incoming.Length - 1];
            for (var index = values.Count - 2; index >= 0; index--)
            {
                if (incoming[index].Id != merged.Id)
                {
                    merged = Factory.Conditional(values[index].Predicate, incoming[index], merged);
                }
            }

            if (!Supported(merged))
            {
                return null;
            }

            environment.Add(variable, merged);
        }

        return new FlowState(0, predicate, environment.ToImmutable());
    }

    // Every variable the loop may assign becomes a fresh unknown at its
    // header. One that is only ever assigned C# integer narrowings (or
    // constants) keeps that range as an invariant.
    private FlowState? CutLoop(IrBlockId header, ImmutableArray<IrBlockId> loop, FlowState state)
    {
        var assigned = new HashSet<IrVarId>();
        foreach (var block in loop)
        {
            foreach (var instruction in Program.GetBlock(block).Instructions)
            {
                if (!Spend())
                {
                    return null;
                }

                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        assigned.Add(assign.Target);
                        break;
                    case IrCallInstruction { Target: { } target }:
                        assigned.Add(target);
                        break;
                    case IrHavocInstruction havoc:
                        assigned.UnionWith(havoc.Variables);
                        break;
                }
            }
        }

        var environment = state.Environment.ToBuilder();
        var predicate = state.Predicate;
        foreach (var variable in assigned.OrderBy(static variable => variable.Value))
        {
            if (!Spend(3))
            {
                return null;
            }

            var fresh = Fresh("loop-havoc", header, variable);
            environment[variable] = fresh;
            if (IntegerRange(variable) is { } range)
            {
                predicate = Factory.Binary(
                    IrBinaryOperator.AndAlso,
                    predicate,
                    Factory.Binary(
                        IrBinaryOperator.AndAlso,
                        Factory.Binary(IrBinaryOperator.GreaterThanOrEqual, fresh, Factory.Integer(range.Minimum)),
                        Factory.Binary(IrBinaryOperator.LessThanOrEqual, fresh, Factory.Integer(range.Maximum))));
            }
        }

        return Supported(predicate)
            ? new FlowState(0, predicate, environment.ToImmutable())
            : null;
    }

    private IrVariableTerm Fresh(string purpose, IrBlockId block, IrVarId variable)
    {
        var fresh = Factory.CreateVariable(
            purpose + ":" +
            block.Value.ToString(CultureInfo.InvariantCulture) + ":" +
            variable.Value.ToString(CultureInfo.InvariantCulture),
            Factory.GetVariableInfo(variable).Type);
        OnFreshVariable(fresh);
        return Factory.Variable(fresh);
    }

    private (long Minimum, long Maximum)? IntegerRange(IrVarId variable)
    {
        if (_ranges == null)
        {
            _ranges = [];
            foreach (var initial in InitialEnvironment)
            {
                Widen(initial.Key, InitialRange(initial.Value));
            }

            foreach (var block in Program.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    switch (instruction)
                    {
                        case IrAssignInstruction assign:
                            Widen(assign.Target, assign.Value switch
                            {
                                IrIntegerTerm constant => (constant.Value, constant.Value),
                                IrUnaryTerm unary when IrIntegerNarrowing.TryGet(
                                    unary.Operator, out var narrowing) =>
                                    (narrowing.Minimum, narrowing.Maximum),
                                _ => null
                            });
                            break;
                        case IrCallInstruction { Target: { } target }:
                            Widen(target, null);
                            break;
                        case IrHavocInstruction havoc:
                            foreach (var havocked in havoc.Variables)
                            {
                                Widen(havocked, null);
                            }

                            break;
                    }
                }
            }
        }

        return _ranges.TryGetValue(variable, out var range) ? range : null;

        void Widen(IrVarId target, (long Minimum, long Maximum)? value)
        {
            _ranges[target] = !_ranges.TryGetValue(target, out var existing)
                ? value
                : existing is { } left && value is { } right
                    ? (Math.Min(left.Minimum, right.Minimum), Math.Max(left.Maximum, right.Maximum))
                    : null;
        }
    }

    private bool ChargeAndMeasureDepth(IrTerm root, out int depth)
    {
        depth = 0;
        var pending = new Stack<(IrTerm Term, ImmutableArray<IrTerm> Children, bool ChildrenReady)>();
        pending.Push((root, [], false));
        while (pending.Count != 0)
        {
            var (term, children, childrenReady) = pending.Pop();
            if (childrenReady)
            {
                var termDepth = 1;
                foreach (var child in children)
                {
                    termDepth = Math.Max(termDepth, 1 + _termDepths[child.Id]);
                }

                _termDepths[term.Id] = termDepth;
                continue;
            }

            if (_termDepths.ContainsKey(term.Id))
            {
                continue;
            }

            if (!Spend())
            {
                return false;
            }

            children = IrTraversal.GetChildren(term);
            pending.Push((term, children, true));
            for (var index = children.Length - 1; index >= 0; index--)
            {
                pending.Push((children[index], [], false));
            }
        }

        return _termDepths.TryGetValue(root.Id, out depth);
    }

    private readonly struct FlowState(
        int order,
        IrTerm predicate,
        ImmutableDictionary<IrVarId, IrTerm> environment)
    {
        internal int Order { get; } = order;

        internal IrTerm Predicate { get; } = predicate;

        internal ImmutableDictionary<IrVarId, IrTerm> Environment { get; } = environment;
    }
}
