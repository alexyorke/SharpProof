namespace SharpProof.Contracts;

public sealed class ContractBinder(
    Compilation compilation,
    IrFactory factory,
    ContractClauseInventoryBuilder? clauseInventory = null) {
    private readonly Compilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ContractApiSymbols? _api =
        ContractApiSymbols.TryCreate(
            compilation ?? throw new ArgumentNullException(nameof(compilation)));
    private readonly RoslynOperationLowerer _types =
        new(factory ?? throw new ArgumentNullException(nameof(factory)));
    private readonly ContractClauseInventoryBuilder _clauseInventory =
        clauseInventory ?? new(compilation);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult>
        _bindings = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult>
        _requiresBindings = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<
        INamedTypeSymbol,
        ImmutableArray<CompanionCandidate>> _companionBindings =
        new(SymbolEqualityComparer.Default);

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null) {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return implementationBody == null
            ? _bindings.GetOrAdd(target, BindUncached)
            : BindCore(target, implementationBody, requiresOnly: false);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null) {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return implementationBody == null
            ? _requiresBindings.GetOrAdd(target, BindRequiresUncached)
            : BindCore(target, implementationBody, requiresOnly: true);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target) =>
        _clauseInventory.Create(NormalizePartialMethod(
            target ?? throw new ArgumentNullException(nameof(target))));

    private ContractBindingResult BindUncached(IMethodSymbol target) =>
        BindCore(target, null, requiresOnly: false);

    private ContractBindingResult BindRequiresUncached(IMethodSymbol target) =>
        BindCore(target, null, requiresOnly: true);

    private ContractBindingResult BindCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly) {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (_api == null)
            return ContractBindingResult.Fail(
                ContractBindingFailure.ContractApiUnavailable);
        if (target.MethodKind is not (
                MethodKind.Ordinary or MethodKind.Constructor))
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedTarget);

        var source = NormalizePartialMethod(target);
        var usesCompanion = false;
        var inventory = _clauseInventory.Create(source, implementationBody);
        var sourceBody = inventory.ImplementationBody;
        if (inventory.HasPlacementErrors)
            return ContractBindingResult.Fail(
                ContractBindingFailure.InvalidClausePlacement);

        if (!inventory.Clauses.Any(clause =>
                clause.IsValid &&
                (!requiresOnly ||
                 clause.Kind == BoundContractKind.Requires))) {
            var companion = ResolveCompanion(target);
            if (companion.Failure != ContractBindingFailure.None)
                return ContractBindingResult.Fail(companion.Failure);
            if (companion.Method != null) {
                source = companion.Method;
                inventory = _clauseInventory.Create(source);
                sourceBody = inventory.ImplementationBody;
                if (sourceBody == null)
                    return ContractBindingResult.Fail(
                        ContractBindingFailure.CompanionBodyUnavailable);
                usesCompanion = true;
                if (inventory.HasPlacementErrors)
                    return ContractBindingResult.Fail(
                        ContractBindingFailure.InvalidClausePlacement);
            }
        }

        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source,
            CreateTypeSpecializer(source));
        var invocationResult = BindInvocations(
            expressionBinder,
            source,
            sourceBody,
            inventory,
            usesCompanion,
            requiresOnly);
        if (invocationResult.Failure != ContractBindingFailure.None)
            return ContractBindingResult.Fail(invocationResult.Failure);

        var canonical = CreateCanonicalVariables(
            target,
            includeResult: !requiresOnly);
        var substitutions = CreateCanonicalSubstitutions(
            target,
            source,
            usesCompanion,
            expressionBinder,
            canonical);
        if (substitutions == null)
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);

        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var clause in invocationResult.Clauses) {
            IrTerm condition;
            try {
                condition = IrSubstitution.Substitute(
                    _factory,
                    clause.Condition,
                    substitutions);
            }
            catch (ArgumentException) {
                return ContractBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
            clauses.Add(new BoundContractClause(
                clause.Kind,
                condition,
                clause.SourceOperation,
                clause.Evidence));
        }

        var attributeResult = BindClosedAttributes(
            target,
            canonical,
            requiresOnly);
        if (attributeResult.Failure != ContractBindingFailure.None)
            return ContractBindingResult.Fail(attributeResult.Failure);
        clauses.AddRange(attributeResult.Clauses);

        return ContractBindingResult.Success(
            new BoundMethodContracts(
                target,
                source,
                clauses.ToImmutable(),
                canonical.ToBoundVariables(),
                usesCompanion));
    }

    private InvocationBindingResult BindInvocations(
        ContractExpressionBinder expressionBinder,
        IMethodSymbol source,
        IOperation? body,
        ContractClauseInventory inventory,
        bool usesCompanion,
        bool requiresOnly) {
        if (body == null) return InvocationBindingResult.Empty;
        var invocations = body.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Where(invocation => IsOwnedBy(invocation, source))
            .OrderBy(static invocation => invocation.Syntax.SpanStart)
            .ToImmutableArray();

        foreach (var invocation in invocations) {
            if (!_api!.IsResult(invocation.TargetMethod) &&
                !_api.IsOld(invocation.TargetMethod))
                continue;
            var enclosingClause = FindEnclosingClause(invocation);
            if (requiresOnly &&
                (enclosingClause == null ||
                 _api.GetClauseKind(enclosingClause.TargetMethod) !=
                 BoundContractKind.Requires))
                continue;
            if (enclosingClause == null ||
                _api.GetClauseKind(enclosingClause.TargetMethod) !=
                BoundContractKind.Ensures)
                return new InvocationBindingResult(
                    [],
                    _api.IsResult(invocation.TargetMethod)
                        ? ContractBindingFailure.ResultOutsideEnsures
                        : ContractBindingFailure.OldOutsideEnsures);
        }

        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var occurrence in inventory.Clauses) {
            if (requiresOnly &&
                occurrence.Kind != BoundContractKind.Requires)
                continue;
            var invocation = occurrence.Invocation;
            if (invocation.Arguments.Length != 1)
                return new InvocationBindingResult(
                    [],
                    ContractBindingFailure.InvalidIntrinsicSignature);
            var expression = expressionBinder.Bind(
                invocation.Arguments[0].Value,
                occurrence.Kind);
            if (!expression.IsSuccess)
                return new InvocationBindingResult([], expression.Failure);
            if (expression.Term!.Type != _factory.BooleanType)
                return new InvocationBindingResult(
                    [],
                    ContractBindingFailure.NonBooleanCondition);
            clauses.Add(new BoundContractClause(
                occurrence.Kind,
                expression.Term,
                _factory.CreateOperation(
                    "contract@" +
                    invocation.Syntax.SpanStart.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)),
                usesCompanion
                    ? BoundContractEvidence.Companion
                    : BoundContractEvidence.CompilerBoundInvocation));
        }
        return new InvocationBindingResult(
            clauses.ToImmutable(),
            ContractBindingFailure.None);
    }

    private IInvocationOperation? FindEnclosingClause(IOperation operation) {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent) {
            if (parent is IInvocationOperation invocation &&
                _api!.GetClauseKind(invocation.TargetMethod).HasValue)
                return invocation;
        }
        return null;
    }

    private static bool IsOwnedBy(IOperation operation, IMethodSymbol method) {
        var enclosing = operation.SemanticModel?.GetEnclosingSymbol(
            operation.Syntax.SpanStart);
        return enclosing is IMethodSymbol enclosingMethod &&
               SymbolEqualityComparer.Default.Equals(
                   enclosingMethod.OriginalDefinition,
                   method.OriginalDefinition);
    }

    private static IMethodSymbol NormalizePartialMethod(
        IMethodSymbol method) =>
        method.PartialImplementationPart ?? method;

    private CompanionResolution ResolveCompanion(IMethodSymbol target) {
        var companions = _companionBindings.GetOrAdd(
            target.ContainingType,
            FindCompanions);
        if (companions.Length == 0)
            return CompanionResolution.None;
        if (companions.Length != 1)
            return CompanionResolution.Fail(
                ContractBindingFailure.AmbiguousCompanion);

        var companion = companions[0];
        if (!ContractForSymbolMatcher.CompanionTypeMatches(
                companion.Type,
                companion.Target))
            return CompanionResolution.Fail(
                ContractBindingFailure.CompanionSignatureMismatch);
        var signatureTarget = companion.Target.IsOpen
            ? target.OriginalDefinition
            : target.ConstructedFrom;
        var methods = companion.Type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared)
            .ToImmutableArray();
        var named = methods
            .Where(candidate => string.Equals(
                candidate.Name,
                target.Name,
                StringComparison.Ordinal))
            .ToImmutableArray();
        var matches = named
            .Where(candidate =>
                ContractForSymbolMatcher.MemberSignaturesMatch(
                    signatureTarget,
                    candidate))
            .ToImmutableArray();
        if (matches.Length == 1)
            return HasUniqueTarget(signatureTarget, matches[0])
                ? SpecializeCompanion(
                    companion,
                    matches[0],
                    target)
                : CompanionResolution.Fail(
                    ContractBindingFailure.AmbiguousCompanion);
        if (matches.Length > 1)
            return CompanionResolution.Fail(
                ContractBindingFailure.AmbiguousCompanion);
        return CompanionResolution.Fail(
            named.Length == 0
                ? ContractBindingFailure.MissingCompanion
                : ContractBindingFailure.CompanionSignatureMismatch);
    }

    private static CompanionResolution SpecializeCompanion(
        CompanionCandidate companion,
        IMethodSymbol definition,
        IMethodSymbol target) {
        try {
            var type = companion.Type;
            if (companion.Target.IsOpen)
                type = type.Construct(
                    [.. target.ContainingType.TypeArguments]);
            var method = type.GetMembers(definition.Name)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    SymbolEqualityComparer.Default.Equals(
                        method.OriginalDefinition,
                        definition.OriginalDefinition));
            if (method == null)
                return CompanionResolution.Fail(
                    ContractBindingFailure.CompanionSignatureMismatch);
            if (method.Arity != 0)
                method = method.Construct([.. target.TypeArguments]);
            return CompanionResolution.Success(
                NormalizePartialMethod(method));
        }
        catch (ArgumentException) {
            return CompanionResolution.Fail(
                ContractBindingFailure.CompanionSignatureMismatch);
        }
    }

    private Func<ITypeSymbol?, ITypeSymbol?> CreateTypeSpecializer(IMethodSymbol source) {
        return Specialize;
        ITypeSymbol? Specialize(ITypeSymbol? type) {
            if (type == null) return null;
            if (type is ITypeParameterSymbol parameter) {
                var parameterArguments = SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol, source.OriginalDefinition)
                    ? source.TypeArguments
                    : SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol,
                        source.ContainingType.OriginalDefinition)
                        ? source.ContainingType.TypeArguments
                        : default;
                if (parameterArguments.IsDefault) return type;
                var replacement = parameterArguments[parameter.Ordinal];
                return type.NullableAnnotation == NullableAnnotation.Annotated
                    ? replacement.WithNullableAnnotation(NullableAnnotation.Annotated)
                    : replacement;
            }
            if (type is IArrayTypeSymbol array)
                return Specialize(array.ElementType) is { } element
                    ? _compilation.CreateArrayTypeSymbol(
                        element, array.Rank, array.ElementNullableAnnotation).WithNullableAnnotation(array.NullableAnnotation)
                    : null;
            if (type is IPointerTypeSymbol pointer)
                return Specialize(pointer.PointedAtType) is { } pointedAt
                    ? _compilation.CreatePointerTypeSymbol(pointedAt) : null;
            if (type is not INamedTypeSymbol named || named.IsUnboundGenericType) return type;
            var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>(named.TypeArguments.Length);
            foreach (var argument in named.TypeArguments) {
                var specialized = Specialize(argument);
                if (specialized == null) return null;
                arguments.Add(specialized);
            }
            var changed = !arguments.SequenceEqual(named.TypeArguments, SymbolEqualityComparer.IncludeNullability);
            if (!changed) return named;
            if (named.IsTupleType) return null;
            try {
                return named.OriginalDefinition.Construct([.. arguments])
                    .WithNullableAnnotation(named.NullableAnnotation);
            }
            catch (ArgumentException) { return null; }
        }
    }

    private ImmutableArray<CompanionCandidate> FindCompanions(
        INamedTypeSymbol targetType) =>
        [.. GetAllTypes(_compilation.Assembly.GlobalNamespace)
            .Select(type => TryGetCompanion(type, targetType))
            .Where(static companion => companion.HasValue)
            .Select(static companion => companion!.Value)];

    private CompanionCandidate? TryGetCompanion(
        INamedTypeSymbol candidate,
        INamedTypeSymbol targetType) {
        var attributes = ContractForSymbolMatcher.GetAttributes(
            candidate,
            _api!.ContractFor);
        if (attributes.Length != 1 ||
            !ContractForSymbolMatcher.TryGetTarget(
                attributes[0],
                out var contractTarget) ||
            !ContractForSymbolMatcher.TargetsType(
                contractTarget,
                targetType))
            return null;
        return new CompanionCandidate(candidate, contractTarget);
    }

    private static bool HasUniqueTarget(
        IMethodSymbol target,
        IMethodSymbol companion) =>
        target.ContainingType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared)
            .Count(candidate =>
                ContractForSymbolMatcher.MemberSignaturesMatch(
                    candidate,
                    companion)) == 1;

    private CanonicalVariables CreateCanonicalVariables(
        IMethodSymbol target,
        bool includeResult) {
        var result = new CanonicalVariables(_factory);
        if (!target.IsStatic && target.MethodKind != MethodKind.Constructor)
            result.Receiver = result.Add(
                target.ContainingType,
                BoundContractVariableRole.Receiver,
                -1,
                _types.GetTypeId(target.ContainingType),
                "receiver");
        for (var index = 0; index < target.Parameters.Length; index++) {
            var parameter = target.Parameters[index];
            result.Parameters.Add(result.Add(
                parameter,
                BoundContractVariableRole.Parameter,
                index,
                _types.GetTypeId(parameter.Type),
                "parameter:" + index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (includeResult &&
            !target.ReturnsVoid &&
            target.MethodKind != MethodKind.Constructor)
            result.Result = result.Add(
                null,
                BoundContractVariableRole.Result,
                -1,
                _types.GetTypeId(target.ReturnType),
                "result");
        return result;
    }

    private Dictionary<IrVarId, IrTerm>? CreateCanonicalSubstitutions(
        IMethodSymbol target,
        IMethodSymbol source,
        bool usesCompanion,
        ContractExpressionBinder expressionBinder,
        CanonicalVariables canonical) {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        var sourceBindings = expressionBinder.VariableBindings;
        foreach (var binding in sourceBindings) {
            IrVarId? canonicalVariable = null;
            if (binding.Symbol is IParameterSymbol parameter &&
                parameter.ContainingSymbol is IMethodSymbol owner &&
                SymbolEqualityComparer.Default.Equals(
                    owner.OriginalDefinition,
                    source.OriginalDefinition)) {
                if (usesCompanion && !target.IsStatic) {
                    if (parameter.Ordinal == 0)
                        canonicalVariable = canonical.Receiver;
                    else if (parameter.Ordinal - 1 < canonical.Parameters.Count)
                        canonicalVariable = canonical.Parameters[parameter.Ordinal - 1];
                }
                else if (parameter.Ordinal < canonical.Parameters.Count) {
                    canonicalVariable = canonical.Parameters[parameter.Ordinal];
                }
            }
            if (!canonicalVariable.HasValue) return null;
            replacements[binding.Variable] =
                _factory.Variable(canonicalVariable.Value);
        }

        if (expressionBinder.ResultVariable.HasValue) {
            if (!canonical.Result.HasValue) return null;
            replacements[expressionBinder.ResultVariable.Value] =
                _factory.Variable(canonical.Result.Value);
        }

        foreach (var receiverVariable in expressionBinder.ReceiverVariables) {
            if (usesCompanion || !canonical.Receiver.HasValue) return null;
            replacements[receiverVariable] =
                _factory.Variable(canonical.Receiver.Value);
        }

        foreach (var preState in expressionBinder.PreStateVariables) {
            if (!replacements.TryGetValue(preState.Key, out var current) ||
                current is not IrVariableTerm currentVariable)
                return null;
            var canonicalPre = canonical.GetOrCreatePreState(
                currentVariable.Variable);
            replacements[preState.Value] = _factory.Variable(canonicalPre);
        }
        return replacements;
    }

    private ClosedAttributeBindingResult BindClosedAttributes(
        IMethodSymbol target,
        CanonicalVariables variables,
        bool requiresOnly) {
        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        for (var index = 0; index < target.Parameters.Length; index++) {
            var result = BindValueAttributes(
                target.Parameters[index].GetAttributes(),
                _factory.Variable(variables.Parameters[index]),
                BoundContractKind.Requires,
                clauses);
            if (result != ContractBindingFailure.None)
                return ClosedAttributeBindingResult.Fail(result);
        }
        if (!requiresOnly && variables.Result.HasValue) {
            var result = BindValueAttributes(
                target.GetReturnTypeAttributes(),
                _factory.Variable(variables.Result.Value),
                BoundContractKind.Ensures,
                clauses);
            if (result != ContractBindingFailure.None)
                return ClosedAttributeBindingResult.Fail(result);
        }
        return new ClosedAttributeBindingResult(
            clauses.ToImmutable(),
            ContractBindingFailure.None);
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        IrTerm value,
        BoundContractKind kind,
        ImmutableArray<BoundContractClause>.Builder clauses) {
        foreach (var attribute in attributes) {
            IrTerm? condition = null;
            if (ContractApiSymbols.IsAttribute(attribute, _api!.NotNull)) {
                var type = _factory.GetTypeInfo(value.Type);
                if (type.Kind is not (
                        IrTypeKind.Reference or IrTypeKind.String or
                        IrTypeKind.Sequence))
                    return ContractBindingFailure.InvalidClosedAttribute;
                condition = _factory.Binary(
                    IrBinaryOperator.NotEqual,
                    value,
                    _factory.Null(value.Type));
            }
            else if (ContractApiSymbols.IsAttribute(attribute, _api.Positive)) {
                if (value.Type != _factory.IntegerType)
                    return ContractBindingFailure.InvalidClosedAttribute;
                condition = _factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    value,
                    _factory.Integer(0));
            }
            else if (ContractApiSymbols.IsAttribute(attribute, _api.InRange)) {
                if (value.Type != _factory.IntegerType ||
                    attribute.ConstructorArguments.Length != 2 ||
                    !TryGetInt64(attribute.ConstructorArguments[0], out var minimum) ||
                    !TryGetInt64(attribute.ConstructorArguments[1], out var maximum) ||
                    minimum > maximum)
                    return ContractBindingFailure.InvalidClosedAttribute;
                condition = _factory.Binary(
                    IrBinaryOperator.AndAlso,
                    _factory.Binary(
                        IrBinaryOperator.GreaterThanOrEqual,
                        value,
                        _factory.Integer(minimum)),
                    _factory.Binary(
                        IrBinaryOperator.LessThanOrEqual,
                        value,
                        _factory.Integer(maximum)));
            }
            if (condition == null) continue;
            clauses.Add(new BoundContractClause(
                kind,
                condition,
                _factory.CreateOperation("closed-attribute"),
                BoundContractEvidence.ClosedAttribute));
        }
        return ContractBindingFailure.None;
    }

    private static bool TryGetInt64(TypedConstant value, out long result) {
        switch (value.Value) {
            case sbyte number: result = number; return true;
            case byte number: result = number; return true;
            case short number: result = number; return true;
            case ushort number: result = number; return true;
            case int number: result = number; return true;
            case uint number: result = number; return true;
            case long number: result = number; return true;
            default: result = 0; return false;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(
        INamespaceSymbol value) {
        foreach (var type in value.GetTypeMembers()) {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }
        foreach (var child in value.GetNamespaceMembers())
            foreach (var type in GetAllTypes(child))
                yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(
        INamedTypeSymbol value) {
        foreach (var type in value.GetTypeMembers()) {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }
    }

    private readonly struct InvocationBindingResult(
        ImmutableArray<BoundContractClause> clauses, ContractBindingFailure failure) {
        internal ImmutableArray<BoundContractClause> Clauses { get; } = clauses;
        internal ContractBindingFailure Failure { get; } = failure;
        internal static InvocationBindingResult Empty { get; } =
            new([], ContractBindingFailure.None);
    }

    private readonly struct CompanionCandidate(INamedTypeSymbol type,
        (INamedTypeSymbol Target, bool IsOpen) target) {
        internal INamedTypeSymbol Type { get; } = type;
        internal (INamedTypeSymbol Target, bool IsOpen) Target { get; } =
            target;
    }

    private readonly struct CompanionResolution(
        IMethodSymbol? method, ContractBindingFailure failure) {
        internal IMethodSymbol? Method { get; } = method;
        internal ContractBindingFailure Failure { get; } = failure;
        internal static CompanionResolution None { get; } =
            new(null, ContractBindingFailure.None);
        internal static CompanionResolution Success(IMethodSymbol method) =>
            new(method, ContractBindingFailure.None);
        internal static CompanionResolution Fail(ContractBindingFailure failure) =>
            new(null, failure);
    }

    private readonly struct ClosedAttributeBindingResult(
        ImmutableArray<BoundContractClause> clauses,
        ContractBindingFailure failure) {
        internal ImmutableArray<BoundContractClause> Clauses { get; } = clauses;
        internal ContractBindingFailure Failure { get; } = failure;
        internal static ClosedAttributeBindingResult Fail(
            ContractBindingFailure failure) =>
            new([], failure);
    }

    private sealed class CanonicalVariables {
        private readonly IrFactory _factory;
        private readonly List<BoundContractVariable> _variables = [];
        private readonly Dictionary<IrVarId, IrVarId> _preState = [];

        internal CanonicalVariables(IrFactory factory) => _factory = factory;
        internal IrVarId? Receiver { get; set; }
        internal List<IrVarId> Parameters { get; } = [];
        internal IrVarId? Result { get; set; }

        internal IrVarId Add(
            ISymbol? symbol,
            BoundContractVariableRole role,
            int ordinal,
            IrTypeId type,
            string name) {
            var variable = _factory.CreateVariable(name, type);
            _variables.Add(new BoundContractVariable(
                symbol,
                role,
                ordinal,
                variable,
                null));
            return variable;
        }

        internal IrVarId GetOrCreatePreState(IrVarId current) {
            if (_preState.TryGetValue(current, out var existing))
                return existing;
            var info = _factory.GetVariableInfo(current);
            var variable = _factory.CreateVariable(
                "pre:" + current.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                info.Type);
            _preState.Add(current, variable);
            _variables.Add(new BoundContractVariable(
                null,
                BoundContractVariableRole.PreState,
                -1,
                variable,
                current));
            return variable;
        }

        internal ImmutableArray<BoundContractVariable> ToBoundVariables() =>
            [.. _variables];
    }
}
