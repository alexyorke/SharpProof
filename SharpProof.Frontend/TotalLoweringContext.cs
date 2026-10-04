namespace SharpProof.Frontend;

// A callable input: its value on entry, its current storage and its Old
// snapshot.
public class TotalInputBinding(IrVarId entry, IrVarId current, IrVarId preState)
{
    public IrVarId Entry { get; } = entry;
    public IrVarId Current { get; } = current;
    public IrVarId PreState { get; } = preState;
}

public sealed class TotalParameterBinding(IParameterSymbol parameter, IrVarId entry, IrVarId current, IrVarId preState)
    : TotalInputBinding(entry, current, preState)
{
    public IParameterSymbol Parameter { get; } = parameter;
}

internal enum TotalParameterState { Entry, Current, PreState }

internal readonly struct TotalSourcePrecondition(IrTerm value, IrTerm safe, OperationId clauseSite)
{
    internal IrTerm Value { get; } = value;
    internal IrTerm Safe { get; } = safe;
    internal OperationId ClauseSite { get; } = clauseSite;
}

internal sealed class TotalShadowCallHop(string callerIdentity, string calleeIdentity, IrSourceSpan site)
{
    internal string CallerIdentity { get; } = callerIdentity;
    internal string CalleeIdentity { get; } = calleeIdentity;
    internal IrSourceSpan Site { get; } = site;
}

internal readonly struct TotalCallPrecondition(string calleeIdentity, int clauseOrdinal, OperationId clauseSite, IrTerm value, IrTerm safe, TotalMetadataPrecondition? metadataClause = null, ImmutableArray<TotalShadowCallHop> ancestry = default)
{
    internal string CalleeIdentity { get; } = calleeIdentity;
    internal int ClauseOrdinal { get; } = clauseOrdinal;
    internal OperationId ClauseSite { get; } = clauseSite;
    internal IrTerm Value { get; } = value;
    internal IrTerm Safe { get; } = safe;
    internal ImmutableArray<TotalShadowCallHop> Ancestry { get; } = ancestry.IsDefault ? [] : ancestry;
    internal TotalMetadataPrecondition? MetadataClause { get; } = metadataClause;
}

public sealed class GuardedExpression(IrTerm value, IrTerm safeCondition, FrontendSubsetClassification classification)
{
    public IrTerm Value { get; } = value;
    public IrTerm SafeCondition { get; } = safeCondition;
    public FrontendSubsetClassification Classification { get; } = classification;
}

// One callable context is shared by its body and clauses. Entry values are
// immutable inputs; current parameter storage and Old snapshots remain distinct.
public sealed class TotalLoweringContext
{
    private readonly RoslynTypeMapper _types;
    private readonly Func<SyntaxTree, string> _document;
    private readonly bool _allowGenericContainer;
    private readonly Dictionary<ISymbol, IrVarId> _locals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly Dictionary<CaptureId, ImmutableArray<IrTerm>> _concatenationOperands = [];
    private readonly HashSet<(SyntaxTree Tree, int Start, int Length)> _specificationCalls = [];
    private readonly Dictionary<(SyntaxTree Tree, int Start, int Length), (IrTerm Condition, OperationId Site)> _assumptions = [];
    private int _temporary;

    public TotalLoweringContext(IrFactory factory, IMethodSymbol target)
        : this(factory, target, tree => string.IsNullOrEmpty(tree.FilePath) ? "source" : tree.FilePath)
    {
    }

    internal TotalLoweringContext(IrFactory factory, IMethodSymbol target, Func<SyntaxTree, string> document)
        : this(factory, target, document, 0)
    {
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
    internal object Origin { get; } = new();
    internal bool AllowObjectWidening { get; set; }
    internal TotalLoweringContext CreateFrame(IMethodSymbol target)
    {
        return new(Factory, target, _document, 0, allowGenericContainer: true) { AllowObjectWidening = AllowObjectWidening };
    }
    internal TotalLoweringContext CreateContractFrame(IMethodSymbol target, bool omitReceiver)
    {
        return new(Factory, target, _document, omitReceiver ? 1 : 0, _allowGenericContainer);
    }
    public ImmutableArray<TotalParameterBinding> Parameters { get; }

    // `this` of a class instance member, once modeled, is a trailing input
    // whose fields are read and stored like those of any other object.
    internal TotalInputBinding? Receiver { get; private set; }

    internal void ModelReceiver()
    {
        if (Receiver != null || Target.IsStatic || !Target.ContainingType.IsReferenceType)
        { return; }
        var ordinal = Parameters.Length.ToString(CultureInfo.InvariantCulture);
        Receiver = new(Factory.CreateVariable("entry:" + ordinal, Factory.ObjectType),
            Factory.CreateVariable("current:" + ordinal, Factory.ObjectType),
            Factory.CreateVariable("old:" + ordinal, Factory.ObjectType));
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

    internal ImmutableArray<TotalInputBinding> Inputs =>
        Receiver == null ? [.. Parameters] : [.. Parameters, Receiver];
    internal ImmutableArray<TotalMetadataPrecondition> MetadataCallPreconditions { get; set; } = [];
    internal ImmutableArray<TotalSourcePrecondition> SourceCallPreconditions { get; set; } = [];
    public IrVarId? Result { get; }
    // The program's initialization reads Entry, so concrete replay must bind
    // these identities themselves before executing the first instruction.
    public ImmutableArray<IrVarId> EntryVariables => [.. Inputs.Select(binding => binding.Entry)];

    // A virtual or overriding body is lowered as written; callers dispatching
    // to it are a separate concern, so only a body-free method is excluded.
    internal bool HasScalarSignature => (Target.IsStatic || Target.MethodKind is MethodKind.Ordinary or MethodKind.PropertyGet or
        MethodKind.PropertySet or MethodKind.Constructor or MethodKind.LocalFunction &&
        !Target.IsAbstract) &&
        !Target.IsAsync && (Target.Arity == 0 && (!Target.ContainingType.IsGenericType || _allowGenericContainer) ||
            SymbolEqualityComparer.Default.Equals(Target, Target.OriginalDefinition)) &&
        Target.PartialImplementationPart == null &&
        !Target.ReturnsByRef && !Target.ReturnsByRefReadonly &&
        Parameters.All(binding => binding.Parameter.RefKind == RefKind.None &&
            CSharpOperationSemantics.IsValueDomain(binding.Parameter.Type)) &&
        (Target.ReturnsVoid || CSharpOperationSemantics.IsValueDomain(Target.ReturnType));

    internal bool OwnsBody(IOperation body)
    {
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
    { return Parameters.Any(binding => SymbolEqualityComparer.Default.Equals(binding.Parameter, parameter)); }

    internal IrVarId Variable(ISymbol symbol, TotalParameterState state = TotalParameterState.Current)
    {
        if (symbol is IParameterSymbol parameter)
        {
            var binding = Parameters.Single(item => SymbolEqualityComparer.Default.Equals(item.Parameter, parameter));
            return state switch
            {
                TotalParameterState.Entry => binding.Entry,
                TotalParameterState.PreState => binding.PreState,
                _ => binding.Current
            };
        }
        if (!_locals.TryGetValue(symbol, out var variable))
        {
            var local = (ILocalSymbol)symbol;
            variable = string.IsNullOrEmpty(local.Name) && local.IsImplicitlyDeclared
                ? Temporary(Type(local.Type)) : Factory.CreateVariable(local.Name, Type(local.Type));
            _locals.Add(symbol, variable);
        }
        return variable;
    }

    internal ImmutableArray<IrTerm> ConcatenationOperands(CaptureId capture)
    { return _concatenationOperands.TryGetValue(capture, out var operands) ? operands : default; }

    internal void RecordConcatenationOperands(CaptureId capture, ImmutableArray<IrTerm> operands)
    {
        if (operands.IsDefault)
        { _concatenationOperands.Remove(capture); }
        else
        { _concatenationOperands[capture] = operands; }
    }

    // Roslyn captures `this` to keep evaluation order around a branching
    // right-hand side; such a capture stands for the never-null receiver.
    private readonly HashSet<CaptureId> _thisCaptures = [];
    internal void RecordThisCapture(CaptureId capture)
    { _thisCaptures.Add(capture); }
    internal bool IsThisCapture(CaptureId capture)
    { return _thisCaptures.Contains(capture); }

    // Roslyn also captures a field of `this` used as an assignment target. A
    // read through it is a fresh approximate field read, which
    // over-approximates the value at capture time.
    private readonly Dictionary<CaptureId, IFieldReferenceOperation> _fieldCaptures = [];
    internal void RecordFieldCapture(CaptureId capture, IFieldReferenceOperation field)
    { _fieldCaptures[capture] = field; }
    internal IFieldReferenceOperation? CapturedField(IOperation operation)
    { return operation is IFlowCaptureReferenceOperation reference && _fieldCaptures.TryGetValue(reference.Id, out var field) ? field : null; }

    internal IrVarId Capture(CaptureId capture, ITypeSymbol? type)
    {
        if (!_captures.TryGetValue(capture, out var variable))
        {
            variable = Temporary(Type(type));
            _captures.Add(capture, variable);
        }
        return variable;
    }

    internal IrVarId Temporary(IrTypeId type, string? ownedName = null)
    {
        var ordinal = _temporary++;
        return Factory.CreateVariable(ownedName ?? "temp:" + ordinal, type);
    }

    internal OperationId Site(IOperation operation)
    {
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
        return Factory.CreateOperation(IrWriteSites.FreshPrefix + operation.Syntax.SpanStart, Span(operation.Syntax));
    }

    // A static field read is a read of ambient state.
    internal OperationId StaticReadSite(IOperation operation)
    {
        return Factory.CreateOperation("StaticFieldReference@" + operation.Syntax.SpanStart, Span(operation.Syntax));
    }

    internal OperationId SyntaxSite(OperationKind kind, SyntaxNode syntax)
    {
        return Factory.CreateOperation(kind + "@" + syntax.SpanStart, Span(syntax));
    }

    internal OperationId AttributeSite(SyntaxNode syntax)
    {
        return Factory.CreateOperation("closed-attribute", Span(syntax));
    }

    // The description names the thrown static type and its base classes, most
    // derived first; "exact:" marks a freshly created exception.
    internal OperationId ThrowSite(IOperation thrown, INamedTypeSymbol type, bool exact)
    {
        var hierarchy = new List<string>();
        for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        { hierarchy.Add(CompilerIdentityBridge.CreateTypeDisplay(current)); }
        return Factory.CreateOperation("explicit-throw:" + (exact ? "exact:" : "") + string.Join(";", hierarchy), Span(thrown.Syntax));
    }

    internal OperationId OpaqueCallSite(IOperation call, IrOpaqueCallEffects effects, string member)
    { return Factory.CreateOperation(IrOpaqueCallSite.Describe(effects, member), Span(call.Syntax)); }

    private IrSourceSpan Span(SyntaxNode syntax)
    {
        var position = syntax.SyntaxTree.GetLineSpan(syntax.Span).StartLinePosition;
        return new(_document(syntax.SyntaxTree), syntax.SpanStart, syntax.Span.Length,
            position.Line + 1, position.Character + 1);
    }

    internal void ExcludeSpecificationCall(IInvocationOperation invocation)
    {
        var syntax = invocation.Syntax;
        _specificationCalls.Add((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length));
    }

    internal void RestoreSpecificationCall(IInvocationOperation invocation)
    {
        var syntax = invocation.Syntax;
        _specificationCalls.Remove((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length));
    }

    internal void RegisterSpecificationAssumption(IInvocationOperation invocation, IrTerm condition, OperationId site)
    {
        var syntax = invocation.Syntax;
        _assumptions.Add((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length), (condition, site));
    }

    internal void DiscardSpecificationAssumptions()
    {
        _assumptions.Clear();
    }

    internal bool TryGetSpecificationAssumption(IOperation operation, out IrTerm condition, out OperationId site)
    {
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

    internal bool IsSpecificationOperation(IOperation operation)
    {
        var syntax = operation.Syntax;
        return _specificationCalls.Any(call => call.Tree == syntax.SyntaxTree &&
            call.Start <= syntax.SpanStart && syntax.Span.End <= call.Start + call.Length);
    }

    internal ImmutableArray<FrontendVariableBinding> Variables =>
        [.. Parameters.Select(binding => new FrontendVariableBinding(binding.Parameter, binding.Current))
            .Concat(_locals.Select(pair => new FrontendVariableBinding(pair.Key, pair.Value)))];
    internal ImmutableArray<IrVarId> Captures => [.. _captures.Values.OrderBy(value => value.Value)];
}
