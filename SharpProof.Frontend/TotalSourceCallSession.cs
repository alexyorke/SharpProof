using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

internal sealed class TotalScalarCallModel(int parameterCount, Func<ImmutableArray<IrTerm>, TotalScalarRule> apply,
    bool stringConcatenation = false)
{
    internal int ParameterCount { get; } = parameterCount;
    internal bool StringConcatenation { get; } = stringConcatenation;
    internal TotalScalarRule Apply(ImmutableArray<IrTerm> arguments) { return apply(arguments); }
}

// One compiler-owned expansion session covers the caller and every fresh frame.
// It supplies source bodies and approved scalar models, never relational
// summaries or contract premises.
internal sealed class TotalSourceCallSession(Compilation compilation,
    Func<TotalLoweringContext, bool> prepareCallee, ResolveTotalIlBody? resolveIl, CancellationToken cancellationToken,
    Func<IMethodSymbol, TotalScalarCallModel?>? resolveScalarModel = null,
    Func<TotalLoweringContext, TotalIlBody, bool>? prepareMetadata = null)
{
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _activeIl = new(StringComparer.Ordinal);
    private readonly Dictionary<IMethodSymbol, bool> _iterators = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, bool> _typeInitialization = new(SymbolEqualityComparer.Default);
    private int _remaining = RoslynTotalProgramLowerer.MaximumRegionSteps;
    internal bool ConstructionLimitExceeded { get; private set; }

    internal bool MetadataRequiresEnabled => prepareMetadata != null;
    // Claim lowering admits unmodeled metadata calls as opaque calls. Shadow
    // and metadata-Requires lowering keep rejecting them.
    internal bool OpaqueCalls { get; set; }
    internal Func<IMethodSymbol, IrOpaqueCallEffects?>? OpaqueEffects { get; set; }
    internal bool ApproximateElementReads { get; set; }
    // Element reads are assigned where they occur, so later stores cannot
    // change them.
    internal bool PinElementReads { get; set; }
    internal bool PrepareMetadata(TotalLoweringContext frame, TotalIlBody body)
    { return prepareMetadata?.Invoke(frame, body) ?? true; }

    internal TotalScalarCallModel? PrepareScalarCall(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;
        if (invocation.Instance != null || !method.IsStatic || method.Parameters.Length > 128 ||
            invocation.Arguments.Length != method.Parameters.Length ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
        { return null; }
        var ordinals = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { } parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method) ||
                !ordinals.Add(parameter.Ordinal) || argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue))
            { return null; }
        }
        var model = resolveScalarModel?.Invoke(method);
        return model != null && model.ParameterCount == method.Parameters.Length && Spend(method.Parameters.Length + 1)
            ? model : null;
    }

    internal bool Spend(int amount = 1)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount < 0 || amount > _remaining)
        { _remaining = 0; ConstructionLimitExceeded = true; return false; }
        _remaining -= amount;
        return true;
    }

    internal bool Enter(IMethodSymbol method)
    {
        return Spend() && _active.Count < 256 && _active.Add(method);
    }

    internal bool IsActive(IMethodSymbol method)
    { return _active.Contains(method) || _active.Contains(method.OriginalDefinition); }

    internal void Leave(IMethodSymbol method)
    {
        _active.Remove(method);
    }

    internal TotalIlBody? PrepareIl(IMethodSymbol method)
    {
        if (resolveIl == null || !Spend(method.Parameters.Length + 1) || _activeIl.Contains(IlKey(method)))
        { return null; }
        return resolveIl(method, cancellationToken);
    }

    internal bool EnterIl(IMethodSymbol method)
    {
        return Spend() && _active.Count + _activeIl.Count < 256 && _activeIl.Add(IlKey(method));
    }

    internal void LeaveIl(IMethodSymbol method)
    { _activeIl.Remove(IlKey(method)); }

    internal static bool IsEmptyParamsArray(IArgumentOperation argument)
    {
        return argument.ArgumentKind == ArgumentKind.ParamArray && argument.Parameter?.IsParams == true &&
            argument.Value is IArrayCreationOperation
            {
                IsImplicit: true, Initializer.ElementValues.Length: 0,
                DimensionSizes.Length: 1, Type: IArrayTypeSymbol { IsSZArray: true }
            } creation &&
            creation.DimensionSizes[0].ConstantValue is { HasValue: true, Value: 0 };
    }

    internal TotalScalarRule? PrepareEmptyParamsArray(IArgumentOperation argument)
    {
        if (!IsEmptyParamsArray(argument) || !Spend() || argument.Value.Type is not IArrayTypeSymbol array)
        { return null; }
        var emptyMethods = compilation.GetSpecialType(SpecialType.System_Array).GetMembers("Empty")
            .OfType<IMethodSymbol>().Where(method => method.IsStatic && method.DeclaredAccessibility == Accessibility.Public &&
                method.Arity == 1 && method.Parameters.IsEmpty &&
                method.ReturnType is IArrayTypeSymbol { IsSZArray: true } returned &&
                SymbolEqualityComparer.Default.Equals(returned.ElementType, method.TypeParameters[0])).ToArray();
        if (emptyMethods.Length != 1 || !Spend(emptyMethods.Length))
        { return null; }
        var empty = emptyMethods[0].Construct(array.ElementType);
        var model = resolveScalarModel?.Invoke(empty);
        return model?.ParameterCount == 0 && SymbolEqualityComparer.Default.Equals(empty.ReturnType, array)
            ? model.Apply([]) : null;
    }

    private static string IlKey(IMethodSymbol method)
    { return method.ContainingAssembly.Identity + "/" + method.ContainingModule.Name + "/" + method.MetadataToken; }

    // An instance callee is a nonvirtual member on a reference receiver. Its
    // implicit `this` is never null and is the caller's receiver value.
    // An accessor takes its property's arguments, and a setter also takes the
    // assigned value as its final `value` parameter. A generic callee runs its
    // declaration, whose parameters must share the call's value domains.
    // With `contractOnly`, a callee already being inlined (a recursive call)
    // gets a frame for its preconditions and no body; so does an iterator
    // without preconditions, whose body runs only as it is enumerated, and a
    // class constructor that chains, initializes fields or has a base class.
    internal bool TryPrepare(TotalLoweringContext caller, IMethodSymbol method, IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments, out TotalLoweringContext? frame, out ControlFlowGraph? graph,
        bool assigned = false, bool contractOnly = false)
    {
        frame = null;
        graph = null;
        if (!Spend(method.Parameters.Length + 1) || !contractOnly && IsActive(method) ||
            method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.Constructor) ||
            assigned != (method.MethodKind == MethodKind.PropertySet) ||
            (method.MethodKind == MethodKind.Constructor
                ? instance != null || !CSharpOperationSemantics.IsPlainConstructor(method, cancellationToken) &&
                    !(contractOnly && method.ContainingType.TypeKind == TypeKind.Class)
                : method.IsStatic != (instance == null)) ||
            instance != null && (instance.Type?.IsReferenceType != true || !method.ContainingType.IsReferenceType) ||
            method.IsVirtual || method.IsOverride || method.IsAbstract || method.IsAsync || method.IsExtern ||
            method.ReducedFrom != null || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.PartialDefinitionPart != null || method.PartialImplementationPart != null ||
            method.Parameters.Where((parameter, ordinal) =>
                !CSharpOperationSemantics.SharesValueDomain(parameter.Type, method.OriginalDefinition.Parameters[ordinal].Type)).Any() ||
            !CSharpOperationSemantics.SharesValueDomain(method.ReturnType, method.OriginalDefinition.ReturnType) ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                parameter.IsParams && parameter.Type is not IArrayTypeSymbol { IsSZArray: true } ||
                !CSharpOperationSemantics.IsValueDomain(parameter.Type)) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType) ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) ||
            method.DeclaringSyntaxReferences.Length != 1 || arguments.Length + (assigned ? 1 : 0) != method.Parameters.Length)
        { return false; }
        var ordinals = new HashSet<int>();
        foreach (var argument in arguments)
        {
            if (!Spend() || argument.Parameter is not { } parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method) &&
                    !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method.AssociatedSymbol) ||
                !ordinals.Add(parameter.Ordinal) ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue or ArgumentKind.ParamArray) ||
                argument.ArgumentKind == ArgumentKind.ParamArray && !parameter.IsParams ||
                argument.ArgumentKind == ArgumentKind.DefaultValue &&
                    (!parameter.HasExplicitDefaultValue || !argument.Value.ConstantValue.HasValue))
            { return false; }
        }
        var reference = method.DeclaringSyntaxReferences[0];
        var declaration = reference.GetSyntax(cancellationToken);
        if (!compilation.ContainsSyntaxTree(reference.SyntaxTree) ||
            declaration is not (MethodDeclarationSyntax or ConstructorDeclarationSyntax or AccessorDeclarationSyntax { Body: not null } or
                AccessorDeclarationSyntax { ExpressionBody: not null }))
        { return false; }
        var iterator = false;
        if (_iterators.TryGetValue(method.OriginalDefinition, out iterator))
        {
            if (!Spend() || iterator && !contractOnly)
            { return false; }
        }
        else
        {
            foreach (var node in declaration.DescendantNodesAndSelf())
            {
                if (!Spend())
                { return false; }
                iterator |= node is YieldStatementSyntax;
                if (iterator && !contractOnly)
                { _iterators[method.OriginalDefinition] = true; return false; }
            }
            _iterators[method.OriginalDefinition] = iterator;
        }
        if (contractOnly && !IsActive(method) && !iterator &&
            !(method.MethodKind == MethodKind.Constructor && !CSharpOperationSemantics.IsPlainConstructor(method, cancellationToken)))
        { return false; }
        if (!HasNoTypeInitialization(method.ContainingType) || !Spend(method.Parameters.Length * 3 + 1))
        { return false; }
        // Source operations bind to declaration symbols. Closed outer types
        // may share that body only when their intrinsic signature is unchanged.
        frame = caller.CreateFrame(method.OriginalDefinition);
        if (instance != null)
        { frame.ModelReceiver(); }
        if (!prepareCallee(frame))
        { frame = null; return false; }
        if (contractOnly)
        {
            // An iterator checks its preconditions only once enumerated.
            if (iterator && !frame.SourceCallPreconditions.IsEmpty)
            { frame = null; return false; }
            return true;
        }
        cancellationToken.ThrowIfCancellationRequested();
        try
        { graph = ControlFlowGraph.Create(declaration, CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree), cancellationToken); }
        catch (ArgumentException)
        { frame = null; return false; }
        return graph != null;
    }

    private bool HasNoTypeInitialization(INamedTypeSymbol type)
    {
        if (_typeInitialization.TryGetValue(type, out var eligible))
        { return Spend() && eligible; }
        if (!TryCheckTypeInitialization(type, out eligible))
        { return false; }
        _typeInitialization[type] = eligible;
        return eligible;
    }

    private bool TryCheckTypeInitialization(INamedTypeSymbol type, out bool eligible)
    {
        eligible = false;
        for (var current = type; current != null; current = current.ContainingType)
        {
            foreach (var member in current.GetMembers())
            {
                if (!Spend())
                { return false; }
                if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: false })
                { return true; }
                if (!member.IsStatic || member is IFieldSymbol { IsConst: true })
                { continue; }
                foreach (var reference in member.DeclaringSyntaxReferences)
                {
                    if (!Spend())
                    { return false; }
                    var syntax = reference.GetSyntax(cancellationToken);
                    var value = syntax switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value,
                        _ => null
                    };
                    if (value != null && (!CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree).GetConstantValue(value, cancellationToken).HasValue ||
                        !CSharpOperationSemantics.IsScalar(member is IFieldSymbol field ? field.Type : (member as IPropertySymbol)?.Type)))
                    { return true; }
                }
            }
        }
        eligible = true;
        return true;
    }
}
