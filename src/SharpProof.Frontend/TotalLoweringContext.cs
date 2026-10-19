namespace SharpProof.Frontend;

// A callable input: its value on entry, its current storage and its Old
// snapshot.
public sealed class TotalInputBinding(IrVarId entry, IrVarId current, IrVarId preState)
{
    public IrVarId Entry { get; } = (entry.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(entry)) : entry);
    public IrVarId Current { get; } = (current.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(current)) : current);
    public IrVarId PreState { get; } = (preState.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(preState)) : preState);
}

public sealed class TotalParameterBinding(IParameterSymbol parameter, IrVarId entry, IrVarId current, IrVarId preState)
{
    public TotalInputBinding Input { get; } = new(
        entry.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(entry)) : entry,
        current.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(current)) : current,
        preState.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(preState)) : preState);
    public IrVarId Entry => Input.Entry;
    public IrVarId Current => Input.Current;
    public IrVarId PreState => Input.PreState;
    public IParameterSymbol Parameter { get; } = (parameter ?? throw new System.ArgumentNullException(nameof(parameter)));
}

internal enum TotalParameterState { Entry, Current, PreState }

internal readonly struct TotalSourcePrecondition(IrTerm value, IrTerm safe, OperationId clauseSite)
{
    internal IrTerm Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    internal IrTerm Safe { get; } = (safe ?? throw new System.ArgumentNullException(nameof(safe)));
    internal OperationId ClauseSite { get; } = (clauseSite.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauseSite)) : clauseSite);
}

internal sealed class TotalShadowCallHop(string callerIdentity, string calleeIdentity, IrSourceSpan site)
{
    internal string CallerIdentity { get; } = (callerIdentity ?? throw new System.ArgumentNullException(nameof(callerIdentity)));
    internal string CalleeIdentity { get; } = (calleeIdentity ?? throw new System.ArgumentNullException(nameof(calleeIdentity)));
    internal IrSourceSpan Site { get; } = (site ?? throw new System.ArgumentNullException(nameof(site)));
}

internal readonly struct TotalCallPrecondition(string calleeIdentity, int clauseOrdinal, OperationId clauseSite, IrTerm value, IrTerm safe, TotalMetadataPrecondition? metadataClause = null, ImmutableArray<TotalShadowCallHop> ancestry = default)
{
    internal string CalleeIdentity { get; } = (calleeIdentity ?? throw new System.ArgumentNullException(nameof(calleeIdentity)));
    internal int ClauseOrdinal { get; } = clauseOrdinal;
    internal OperationId ClauseSite { get; } = (clauseSite.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauseSite)) : clauseSite);
    internal IrTerm Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    internal IrTerm Safe { get; } = (safe ?? throw new System.ArgumentNullException(nameof(safe)));
    internal ImmutableArray<TotalShadowCallHop> Ancestry { get; } = ancestry.IsDefault ? [] : ancestry;
    internal TotalMetadataPrecondition? MetadataClause { get; } = metadataClause;
}

public sealed class GuardedExpression(IrTerm value, IrTerm safeCondition, FrontendSubsetClassification classification /* SPINT001: all values valid - every classification, including default Exact, is valid */)
{
    public IrTerm Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    public IrTerm SafeCondition { get; } = (safeCondition ?? throw new System.ArgumentNullException(nameof(safeCondition)));
    public FrontendSubsetClassification Classification { get; } = classification;
}

// One callable context is shared by its body and clauses. Entry values are
// immutable inputs; current parameter storage and Old snapshots remain distinct.
public sealed partial class TotalLoweringContext
{
    private readonly RoslynTypeMapper _types;
    private readonly Func<SyntaxTree, string> _document;
    private readonly bool _allowGenericContainer;
    private readonly Dictionary<ISymbol, IrVarId> _locals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly Dictionary<CaptureId, ImmutableArray<IrTerm>> _concatenationOperands = [];
    private readonly HashSet<(SyntaxTree Tree, int Start, int Length)> _specificationCalls = [];
    private readonly Dictionary<BasicBlock, ExactSpecificationContinuation> _exactSpecificationContinuations = [];
    private ControlFlowGraph? _exactSpecificationGraph;
    private Compilation? _exactSpecificationCompilation;
    private readonly Dictionary<(SyntaxTree Tree, int Start, int Length), (IrTerm Condition, OperationId Site)> _assumptions = [];
    private readonly List<(IrTerm Condition, OperationId Site)> _entryAssumptions = [];
    private int _temporary;

    public TotalLoweringContext(IrFactory factory, IMethodSymbol target)
        : this(factory, target, tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (string.IsNullOrEmpty(tree.FilePath) ? "source" : tree.FilePath))
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
    }

    internal TotalLoweringContext(IrFactory factory, IMethodSymbol target, Func<SyntaxTree, string> document)
        : this(factory, target, document, 0)
    {
        document = RepositoryParameterGuard.NotNull(document, nameof(document));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
    }

    private TotalLoweringContext(IrFactory factory, IMethodSymbol target, Func<SyntaxTree, string> document, int leadingParameters,
        bool allowGenericContainer = false)
    {
        Factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        Target = ArgumentNullGuard.NotNull(target, nameof(target));
        _document = ArgumentNullGuard.NotNull(document, nameof(document));
        _allowGenericContainer = allowGenericContainer;
        if (factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("Candidate lowering requires Total IR semantics.", nameof(factory));
        }
        _types = new RoslynTypeMapper(factory);
        Parameters = [.. target.Parameters.Skip(leadingParameters).Select(parameter =>
        {
            parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
            var type = _types.GetTypeId(parameter.Type);
            return new TotalParameterBinding(parameter,
                factory.CreateVariable("entry:" + parameter.Ordinal, type),
                factory.CreateVariable("current:" + parameter.Ordinal, type),
                factory.CreateVariable("old:" + parameter.Ordinal, type));
        })];
        Result = target.ReturnsVoid ? null : factory.CreateVariable("result", _types.GetTypeId(target.ReturnType));
    }

    public IrFactory Factory { get; }
    public IMethodSymbol Target { get; }
    internal bool CaptureShadowCallAncestry { get; set; }
    internal bool CaptureResourceLoopEntries { get; set; }
    internal object Origin { get; } = new();
    internal bool AllowObjectWidening { get; set; }
    internal TotalLoweringContext CreateFrame(IMethodSymbol target)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return new(Factory, target, _document, 0, allowGenericContainer: true) { AllowObjectWidening = AllowObjectWidening };
    }
    internal TotalLoweringContext CreateContractFrame(IMethodSymbol target, bool omitReceiver)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return new(Factory, target, _document, omitReceiver ? 1 : 0, _allowGenericContainer);
    }
    public ImmutableArray<TotalParameterBinding> Parameters { get; private set; }

    // `this` of a class instance member, once modeled, is a trailing input
    // whose fields are read and stored like those of any other object. An
    // inlined callee's receiver is assigned by its caller.
    internal TotalInputBinding? Receiver { get; private set; }

    internal void ModelReceiver()
    {
        if (Receiver != null || Target.IsStatic || !Target.ContainingType.IsReferenceType)
        { return; }
        var ordinal = Parameters.Length.ToString(CultureInfo.InvariantCulture);
        var type = Type(Target.ContainingType);
        Receiver = new(Factory.CreateVariable("entry:" + ordinal, type), Factory.CreateVariable("current:" + ordinal, type),
            Factory.CreateVariable("old:" + ordinal, type));
    }

    internal IrTerm? ReceiverValue(TotalParameterState state = TotalParameterState.Current)
    {
        return Receiver == null ? null : Factory.Variable(state switch
        {
            TotalParameterState.Entry => Receiver.Entry,
            TotalParameterState.PreState => Receiver.PreState,
            _ => Receiver.Current
        });
    }

    // A constructor's `this` stays a receiver only: once it escapes, its
    // earlier field writes are no longer those of an unobserved object.
    internal IrTerm? ThisValue(TotalParameterState state = TotalParameterState.Current)
    { return Target.MethodKind == MethodKind.Constructor ? null : ReceiverValue(state); }

    internal ImmutableArray<TotalInputBinding> Inputs =>
        Receiver == null ? [.. Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : parameter.Input)]
            : [.. Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : parameter.Input), Receiver];
    internal ImmutableArray<TotalMetadataPrecondition> MetadataCallPreconditions { get; set; } = [];
    internal ImmutableArray<TotalSourcePrecondition> SourceCallPreconditions { get; set; } = [];
    public IrVarId? Result { get; private set; }
    // The program's initialization reads Entry, so concrete replay must bind
    // these identities themselves before executing the first instruction.
    public ImmutableArray<IrVarId> EntryVariables => [.. Inputs.Select(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry))];

    // A virtual or overriding body is lowered as written; callers dispatching
    // to it are a separate concern, so only a body-free method is excluded.
    internal bool HasScalarSignature => (Target.IsStatic || Target.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or
        MethodKind.PropertySet or MethodKind.Constructor or MethodKind.LocalFunction or MethodKind.ExplicitInterfaceImplementation &&
        !Target.IsAbstract) &&
        !Target.IsAsync && (Target.Arity == 0 && (!Target.ContainingType.IsGenericType || _allowGenericContainer) ||
            SymbolEqualityComparer.Default.Equals(Target, Target.OriginalDefinition)) &&
        Target.PartialImplementationPart == null &&
        !Target.ReturnsByRef && !Target.ReturnsByRefReadonly &&
        Parameters.All(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : ((binding.Parameter.RefKind == RefKind.None || IsReadOnlyReference(binding.Parameter)) &&
            CSharpOperationSemantics.IsValueDomain(binding.Parameter.Type))) &&
        (Target.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(Target.ReturnType));

    // A `ref` or `in` parameter of a scalar or reference type that the body
    // never writes (no assignment, increment, compound assignment or ref or
    // out argument naming it) starts with its caller's value. The final
    // program pass invalidates its current cell after possible alias writes.
    // A struct stays excluded: a call on it may
    // mutate the caller's storage.
    private bool IsReadOnlyReference(IParameterSymbol parameter)
    {
        if (parameter.RefKind is not (RefKind.Ref or RefKind.In) || Target.DeclaringSyntaxReferences.Length != 1 ||
            !CSharpOperationSemantics.IsScalar(parameter.Type) && !CSharpOperationSemantics.IsReferenceDomain(parameter.Type))
        { return false; }
        var name = parameter.Name;
        bool Names(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax? expression)
        {
            while (expression is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
            { expression = parenthesized.Expression; }
            return expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;
        }
        return !Target.DeclaringSyntaxReferences[0].GetSyntax().DescendantNodes().Any(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax assignment => Names(assignment.Left),
            Microsoft.CodeAnalysis.CSharp.Syntax.PrefixUnaryExpressionSyntax prefix => Names(prefix.Operand),
            Microsoft.CodeAnalysis.CSharp.Syntax.PostfixUnaryExpressionSyntax postfix => Names(postfix.Operand),
            Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) &&
                Names(argument.Expression),
            Microsoft.CodeAnalysis.CSharp.Syntax.RefExpressionSyntax reference => Names(reference.Expression),
            _ => false
        }));
    }

    internal bool OwnsBody(IOperation body)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        var model = body.SemanticModel;
        var method = model?.GetDeclaredSymbol(body.Syntax) as IMethodSymbol ??
            model?.GetEnclosingSymbol(body.Syntax.SpanStart) as IMethodSymbol;
        return SymbolEqualityComparer.Default.Equals(Target, method);
    }

    internal IrTypeId Type(ITypeSymbol? type)
    {
        return _types.GetTypeId(type);
    }

    internal bool OwnsParameter(IParameterSymbol parameter)
    { parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter)); return Parameters.Any(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (SymbolEqualityComparer.Default.Equals(binding.Parameter, parameter))); }

    internal IrVarId Variable(ISymbol symbol, TotalParameterState state = TotalParameterState.Current)
    {
        if (symbol is IParameterSymbol parameter)
        {
            var binding = Parameters.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (SymbolEqualityComparer.Default.Equals(item.Parameter, parameter)));
            return state switch
            {
                TotalParameterState.Entry => binding.Entry,
                TotalParameterState.PreState => binding.PreState,
                _ => binding.Current
            };
        }
        if (!_locals.TryGetValue(symbol, out var variable))
        {
            var local = symbol as ILocalSymbol ?? throw new ArgumentException("Variable symbol must be a parameter or local.", nameof(symbol));
            var type = Type(_arrayReceivers.TryGetValue(local, out var array) ? array : local.Type);
            variable = string.IsNullOrEmpty(local.Name) && local.IsImplicitlyDeclared
                ? Temporary(type) : Factory.CreateVariable(local.Name, type);
            _locals.Add(symbol, variable);
        }
        return variable;
    }

    internal ImmutableArray<IrTerm> ConcatenationOperands(CaptureId capture)
    { return _concatenationOperands.TryGetValue(capture, out var operands) ? operands : default; }

    internal void RecordConcatenationOperands(CaptureId capture /* SPINT001: all values valid - every capture token, including zero, is a valid lookup or storage key */, ImmutableArray<IrTerm> operands)
    {
        if (operands.IsDefault)
        { _concatenationOperands.Remove(capture); }
        else
        { _concatenationOperands[capture] = operands; }
    }

    // Roslyn captures `this` to keep evaluation order around a branching
    // right-hand side; such a capture stands for the never-null receiver.
    private readonly HashSet<CaptureId> _thisCaptures = [];
    internal void RecordThisCapture(CaptureId capture /* SPINT001: all values valid - every capture token, including zero, is a valid lookup or storage key */)
    { _thisCaptures.Add(capture); }
    internal bool IsThisCapture(CaptureId capture)
    { return _thisCaptures.Contains(capture); }

    // Roslyn also captures a field of `this` used as an assignment target. A
    // read through it is a fresh approximate field read, which
    // over-approximates the value at capture time.
    private readonly Dictionary<CaptureId, IFieldReferenceOperation> _fieldCaptures = [];
    internal void RecordFieldCapture(CaptureId capture /* SPINT001: all values valid - every capture token, including zero, is a valid lookup or storage key */, IFieldReferenceOperation field)
    { field = RepositoryParameterGuard.NotNull(field, nameof(field)); _fieldCaptures[capture] = field; }
    internal IFieldReferenceOperation? CapturedField(IOperation operation)
    { operation = RepositoryParameterGuard.NotNull(operation, nameof(operation)); return operation is IFlowCaptureReferenceOperation reference && _fieldCaptures.TryGetValue(reference.Id, out var field) ? field : null; }

    // A capture of a local or parameter, as an assignment target, is that
    // variable's storage.
    private readonly Dictionary<CaptureId, ISymbol> _storageCaptures = [];
    internal void RecordStorageCapture(CaptureId capture /* SPINT001: all values valid - every capture token, including zero, is a valid lookup or storage key */, ISymbol variable)
    { variable = RepositoryParameterGuard.NotNull(variable, nameof(variable)); _storageCaptures[capture] = variable; }
    internal IrVarId? CapturedStorage(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IFlowCaptureReferenceOperation reference && _storageCaptures.TryGetValue(reference.Id, out var variable)
            ? Variable(variable) : null;
    }

    internal IrVarId Capture(CaptureId capture /* SPINT001: all values valid - every capture token, including zero, is a valid lookup or storage key */, ITypeSymbol? type)
    {
        if (!_captures.TryGetValue(capture, out var variable))
        {
            variable = Temporary(Type(_arrayCaptures.TryGetValue(capture, out var array) ? array : type));
            _captures.Add(capture, variable);
        }
        return variable;
    }

    internal IrVarId Temporary(IrTypeId type, string? ownedName = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        var ordinal = _temporary++;
        return Factory.CreateVariable(ownedName ?? "temp:" + ordinal, type);
    }

    internal OperationId Site(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var syntax = operation.Syntax;
        return Factory.CreateOperation(operation.Kind + "@" + syntax.SpanStart, Span(syntax));
    }

    // A constructor whose body uses `this` only to read and write its fields
    // initializes an object no caller can observe yet.
    internal bool FreshReceiver { get; set; }

    // The compilation of the body being lowered; control flow graph
    // operations carry no semantic model.
    internal Compilation? Compilation { get; set; }

    internal OperationId FreshWriteSite(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return Factory.CreateOperation(IrWriteSites.FreshPrefix + operation.Syntax.SpanStart, Span(operation.Syntax));
    }

    // A static field read is a read of ambient state.
    internal OperationId StaticReadSite(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return Factory.CreateOperation("StaticFieldReference@" + operation.Syntax.SpanStart, Span(operation.Syntax));
    }

    internal OperationId SyntaxSite(OperationKind kind, SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        return Factory.CreateOperation(kind + "@" + syntax.SpanStart, Span(syntax));
    }

    internal OperationId AttributeSite(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        return Factory.CreateOperation("closed-attribute", Span(syntax));
    }

    // The description names the thrown static type and its base classes, most
    // derived first; "exact:" marks a freshly created exception.
    internal OperationId ThrowSite(IOperation thrown, INamedTypeSymbol type, bool exact)
    {
        thrown = RepositoryParameterGuard.NotNull(thrown, nameof(thrown));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var hierarchy = new List<string>();
        for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        { hierarchy.Add(CompilerIdentityBridge.CreateTypeDisplay(current)); }
        return Factory.CreateOperation("explicit-throw:" + (exact ? "exact:" : "") + string.Join(";", hierarchy), Span(thrown.Syntax));
    }

    internal OperationId OpaqueCallSite(IOperation call, IrOpaqueCallEffects effects, string member)
    { call = RepositoryParameterGuard.NotNull(call, nameof(call)); member = RepositoryParameterGuard.NotNull(member, nameof(member)); return Factory.CreateOperation(IrOpaqueCallSite.Describe(effects, member), Span(call.Syntax)); }

    private IrSourceSpan Span(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        var position = syntax.SyntaxTree.GetLineSpan(syntax.Span).StartLinePosition;
        return new(_document(syntax.SyntaxTree), syntax.SpanStart, syntax.Span.Length,
            position.Line + 1, position.Character + 1);
    }

    internal void ExcludeSpecificationCall(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        var syntax = invocation.Syntax;
        if (_specificationCalls.Add((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length)))
        { _exactSpecificationContinuations.Clear(); }
    }

    internal void RestoreSpecificationCall(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        var syntax = invocation.Syntax;
        if (_specificationCalls.Remove((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length)))
        { _exactSpecificationContinuations.Clear(); }
    }

    // Entries are successes in the exact, same-region mode only. Classification
    // mutations invalidate them; graph and fixed emission compilation scope reuse.
    internal readonly struct ExactSpecificationContinuation
    {
        internal ExactSpecificationContinuation(BasicBlock continuation, int traversalWork)
        {
            continuation = RepositoryParameterGuard.NotNull(continuation, nameof(continuation));
            Continuation = continuation;
            TraversalWork = traversalWork;
        }

        internal BasicBlock Continuation { get; }
        internal int TraversalWork { get; }
    }

    internal bool TryGetExactSpecificationContinuation(ControlFlowGraph graph, Compilation? compilation,
        BasicBlock source, out ExactSpecificationContinuation continuation)
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        if (!ReferenceEquals(_exactSpecificationGraph, graph) ||
            !ReferenceEquals(_exactSpecificationCompilation, compilation))
        {
            _exactSpecificationContinuations.Clear();
            _exactSpecificationGraph = graph;
            _exactSpecificationCompilation = compilation;
        }
        return _exactSpecificationContinuations.TryGetValue(source, out continuation);
    }

    internal void CacheExactSpecificationContinuation(ControlFlowGraph graph, Compilation? compilation,
        BasicBlock source, ExactSpecificationContinuation continuation)
    {
        RepositoryParameterGuard.RequireInitialized(continuation.Continuation is null, nameof(continuation), "A continuation is required.");
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        if (ReferenceEquals(_exactSpecificationGraph, graph) &&
            ReferenceEquals(_exactSpecificationCompilation, compilation))
        { _exactSpecificationContinuations[source] = continuation; }
    }

    internal IReadOnlyList<(IrTerm Condition, OperationId Site)> EntrySpecificationAssumptions => _entryAssumptions;

    internal void RegisterSpecificationAssumption(IInvocationOperation invocation, IrTerm condition, OperationId site, bool atEntry = false)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        condition = RepositoryParameterGuard.NotNull(condition, nameof(condition));
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        if (atEntry)
        {
            // A companion prologue has no executable source site in the target body.
            _entryAssumptions.Add((condition, site));
            return;
        }
        var syntax = invocation.Syntax;
        _assumptions.Add((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length), (condition, site));
    }

    internal void DiscardSpecificationAssumptions()
    {
        _assumptions.Clear();
        _entryAssumptions.Clear();
    }

    internal bool TryGetSpecificationAssumption(IOperation operation, out IrTerm condition, out OperationId site)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var syntax = (operation is IExpressionStatementOperation statement ? statement.Operation : operation).Syntax;
        if (_assumptions.TryGetValue((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length), out var assumption))
        {
            condition = assumption.Condition;
            site = assumption.Site;
            return true;
        }
        condition = null!;
        site = default;
        return false;
    }

    internal bool IsExcludedSpecificationCall(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var syntax = operation.Syntax;
        return _specificationCalls.Contains((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length));
    }
    internal bool IsSpecificationOperation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var syntax = operation.Syntax;
        return _specificationCalls.Any(call => call.Tree == syntax.SyntaxTree &&
            call.Start <= syntax.SpanStart && syntax.Span.End <= call.Start + call.Length);
    }

    internal ImmutableArray<FrontendVariableBinding> Variables =>
        [.. Parameters.Select(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (new FrontendVariableBinding(binding.Parameter, binding.Current)))
            .Concat(_locals.Select(pair => new FrontendVariableBinding(pair.Key, pair.Value)))];
    internal ImmutableArray<IrVarId> Captures => [.. _captures.Values.OrderBy(value => value.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(value)) : (value.Value))];
}
