namespace SharpProof.Contracts;

public sealed class ContractBinder(Compilation compilation, IrFactory factory) {
    private readonly Compilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ContractApiSymbols? _api =
        ContractApiSymbols.TryCreate(
            compilation ?? throw new ArgumentNullException(nameof(compilation)));
    private readonly ContractTypeMapper _types =
        new(factory ?? throw new ArgumentNullException(nameof(factory)));

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null) =>
        BindCore(target, implementationBody, requiresOnly: false);

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null) =>
        BindCore(target, implementationBody, requiresOnly: true);

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

        var directBody = implementationBody ?? TryGetBody(target);
        var source = target;
        var sourceBody = directBody;
        var usesCompanion = false;

        if (!ContainsDirectClause(directBody, target, requiresOnly)) {
            var companion = ResolveCompanion(target);
            if (companion.Failure != ContractBindingFailure.None)
                return ContractBindingResult.Fail(companion.Failure);
            if (companion.Method != null) {
                source = companion.Method;
                sourceBody = TryGetBody(source);
                if (sourceBody == null)
                    return ContractBindingResult.Fail(
                        ContractBindingFailure.CompanionBodyUnavailable);
                usesCompanion = true;
            }
        }

        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source);
        var invocationResult = BindInvocations(
            expressionBinder,
            source,
            sourceBody,
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
                attributeResult.IsPure,
                usesCompanion));
    }

    private InvocationBindingResult BindInvocations(
        ContractExpressionBinder expressionBinder,
        IMethodSymbol source,
        IOperation? body,
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
        foreach (var invocation in invocations) {
            var kind = _api!.GetClauseKind(invocation.TargetMethod);
            if (!kind.HasValue) continue;
            if (requiresOnly && kind.Value != BoundContractKind.Requires)
                continue;
            if (invocation.Arguments.Length != 1)
                return new InvocationBindingResult(
                    [],
                    ContractBindingFailure.InvalidIntrinsicSignature);
            var expression = expressionBinder.Bind(
                invocation.Arguments[0].Value,
                kind.Value);
            if (!expression.IsSuccess)
                return new InvocationBindingResult([], expression.Failure);
            if (expression.Term!.Type != _factory.BooleanType)
                return new InvocationBindingResult(
                    [],
                    ContractBindingFailure.NonBooleanCondition);
            clauses.Add(new BoundContractClause(
                kind.Value,
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

    private bool ContainsDirectClause(
        IOperation? body,
        IMethodSymbol method,
        bool requiresOnly) =>
        body != null &&
        body.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Any(invocation =>
                IsOwnedBy(invocation, method) &&
                _api!.GetClauseKind(invocation.TargetMethod) is { } kind &&
                (!requiresOnly || kind == BoundContractKind.Requires));

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
        return SymbolEqualityComparer.Default.Equals(enclosing, method);
    }

    private CompanionResolution ResolveCompanion(IMethodSymbol target) {
        var companionTypes = GetAllTypes(_compilation.Assembly.GlobalNamespace)
            .Where(type => IsCompanionFor(type, target.ContainingType))
            .ToImmutableArray();
        if (companionTypes.Length == 0)
            return CompanionResolution.None;
        if (companionTypes.Length != 1)
            return CompanionResolution.Fail(
                ContractBindingFailure.AmbiguousCompanion);

        var named = companionTypes[0].GetMembers(target.Name)
            .OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary)
            .ToImmutableArray();
        var matches = named
            .Where(candidate => CompanionSignaturesMatch(target, candidate))
            .ToImmutableArray();
        if (matches.Length == 1)
            return CompanionResolution.Success(matches[0]);
        if (matches.Length > 1)
            return CompanionResolution.Fail(
                ContractBindingFailure.AmbiguousCompanion);
        return CompanionResolution.Fail(
            named.Length == 0
                ? ContractBindingFailure.MissingCompanion
                : ContractBindingFailure.CompanionSignatureMismatch);
    }

    private bool IsCompanionFor(
        INamedTypeSymbol candidate,
        INamedTypeSymbol targetType) {
        var attributes = candidate.GetAttributes()
            .Where(attribute => _api!.IsAttribute(attribute, _api.ContractFor))
            .ToImmutableArray();
        if (attributes.Length != 1 ||
            attributes[0].ConstructorArguments.Length != 1)
            return false;
        return attributes[0].ConstructorArguments[0].Value is ITypeSymbol value &&
               SymbolEqualityComparer.Default.Equals(value, targetType);
    }

    private static bool CompanionSignaturesMatch(
        IMethodSymbol target,
        IMethodSymbol companion) {
        if (!companion.IsStatic ||
            companion.Arity != target.Arity ||
            companion.ReturnsVoid != target.ReturnsVoid ||
            !TypesMatch(target.ReturnType, companion.ReturnType))
            return false;
        var receiverOffset = target.IsStatic ? 0 : 1;
        if (companion.Parameters.Length != target.Parameters.Length + receiverOffset)
            return false;
        if (!target.IsStatic) {
            var receiver = companion.Parameters[0];
            if (receiver.RefKind != RefKind.None ||
                !TypesMatch(target.ContainingType, receiver.Type))
                return false;
        }
        for (var index = 0; index < target.Parameters.Length; index++) {
            var left = target.Parameters[index];
            var right = companion.Parameters[index + receiverOffset];
            if (left.RefKind != right.RefKind ||
                !TypesMatch(left.Type, right.Type))
                return false;
        }
        for (var index = 0; index < target.TypeParameters.Length; index++) {
            if (!TypeParameterConstraintsMatch(
                    target.TypeParameters[index],
                    companion.TypeParameters[index]))
                return false;
        }
        return true;
    }

    private static bool TypeParameterConstraintsMatch(
        ITypeParameterSymbol left,
        ITypeParameterSymbol right) {
        if (left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
            return false;
        for (var index = 0; index < left.ConstraintTypes.Length; index++)
            if (!TypesMatch(
                    left.ConstraintTypes[index],
                    right.ConstraintTypes[index]))
                return false;
        return true;
    }

    private static bool TypesMatch(ITypeSymbol left, ITypeSymbol right) {
        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
            return leftParameter.TypeParameterKind ==
                   rightParameter.TypeParameterKind &&
                   leftParameter.Ordinal == rightParameter.Ordinal;
        if (left is IArrayTypeSymbol leftArray &&
            right is IArrayTypeSymbol rightArray)
            return leftArray.Rank == rightArray.Rank &&
                   TypesMatch(leftArray.ElementType, rightArray.ElementType);
        if (left is INamedTypeSymbol leftNamed &&
            right is INamedTypeSymbol rightNamed) {
            if (!SymbolEqualityComparer.Default.Equals(
                    leftNamed.OriginalDefinition,
                    rightNamed.OriginalDefinition) ||
                leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length)
                return false;
            for (var index = 0; index < leftNamed.TypeArguments.Length; index++)
                if (!TypesMatch(
                        leftNamed.TypeArguments[index],
                        rightNamed.TypeArguments[index]))
                    return false;
            return true;
        }
        return SymbolEqualityComparer.Default.Equals(left, right);
    }

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

    private IReadOnlyDictionary<IrVarId, IrTerm>? CreateCanonicalSubstitutions(
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
                SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    source)) {
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
        var pureCount = target.GetAttributes()
            .Count(attribute => _api!.IsAttribute(attribute, _api.Pure));
        if (pureCount > 1)
            return ClosedAttributeBindingResult.Fail(
                ContractBindingFailure.InvalidClosedAttribute);
        return new ClosedAttributeBindingResult(
            clauses.ToImmutable(),
            pureCount == 1,
            ContractBindingFailure.None);
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        IrTerm value,
        BoundContractKind kind,
        ImmutableArray<BoundContractClause>.Builder clauses) {
        foreach (var attribute in attributes) {
            IrTerm? condition = null;
            if (_api!.IsAttribute(attribute, _api.NotNull)) {
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
            else if (_api.IsAttribute(attribute, _api.Positive)) {
                if (value.Type != _factory.IntegerType)
                    return ContractBindingFailure.InvalidClosedAttribute;
                condition = _factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    value,
                    _factory.Integer(0));
            }
            else if (_api.IsAttribute(attribute, _api.InRange)) {
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

    private IOperation? TryGetBody(IMethodSymbol method) {
        foreach (var syntaxReference in method.DeclaringSyntaxReferences) {
            var syntax = syntaxReference.GetSyntax();
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax);
            if (operation != null) return operation;
            if (syntax is BaseMethodDeclarationSyntax declaration) {
                if (declaration.Body != null) {
                    operation = model.GetOperation(declaration.Body);
                    if (operation != null) return operation;
                }
                if (declaration.ExpressionBody != null) {
                    operation = model.GetOperation(
                        declaration.ExpressionBody.Expression);
                    if (operation != null) return operation;
                }
            }
        }
        return null;
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

    private readonly struct InvocationBindingResult {
        internal InvocationBindingResult(
            ImmutableArray<BoundContractClause> clauses,
            ContractBindingFailure failure) {
            Clauses = clauses;
            Failure = failure;
        }

        internal ImmutableArray<BoundContractClause> Clauses { get; }
        internal ContractBindingFailure Failure { get; }
        internal static InvocationBindingResult Empty { get; } =
            new([], ContractBindingFailure.None);
    }

    private readonly struct CompanionResolution {
        private CompanionResolution(
            IMethodSymbol? method,
            ContractBindingFailure failure) {
            Method = method;
            Failure = failure;
        }

        internal IMethodSymbol? Method { get; }
        internal ContractBindingFailure Failure { get; }
        internal static CompanionResolution None { get; } =
            new(null, ContractBindingFailure.None);
        internal static CompanionResolution Success(IMethodSymbol method) =>
            new(method, ContractBindingFailure.None);
        internal static CompanionResolution Fail(ContractBindingFailure failure) =>
            new(null, failure);
    }

    private readonly struct ClosedAttributeBindingResult {
        internal ClosedAttributeBindingResult(
            ImmutableArray<BoundContractClause> clauses,
            bool isPure,
            ContractBindingFailure failure) {
            Clauses = clauses;
            IsPure = isPure;
            Failure = failure;
        }

        internal ImmutableArray<BoundContractClause> Clauses { get; }
        internal bool IsPure { get; }
        internal ContractBindingFailure Failure { get; }
        internal static ClosedAttributeBindingResult Fail(
            ContractBindingFailure failure) =>
            new([], false, failure);
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
