namespace SharpProof.Worker;

internal sealed class CallableVerifier(
    CSharpCompilation compilation,
    ISmtBackend backend,
    int maximumExpressionDepth) {
    private readonly CSharpCompilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly ProofKernel _kernel =
        new(backend ?? throw new ArgumentNullException(nameof(backend)));
    private readonly IrFactory _factory = new();
    private readonly ResolvedApiSpecTable _apiSpecs =
        new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
    private readonly int _maximumExpressionDepth = maximumExpressionDepth > 0
        ? maximumExpressionDepth
        : throw new ArgumentOutOfRangeException(nameof(maximumExpressionDepth));
    private readonly INamedTypeSymbol? _contractType =
        compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
    private readonly INamedTypeSymbol? _contractForType =
        compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.ContractForAttribute");

    internal ImmutableArray<CallableTarget> Discover() {
        var targets = ImmutableArray.CreateBuilder<CallableTarget>();
        foreach (var tree in _compilation.SyntaxTrees
                     .OrderBy(static tree => tree.FilePath, StringComparer.Ordinal)) {
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(_compilation, tree);
            foreach (var declaration in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<BaseMethodDeclarationSyntax>()) {
                if (declaration is not (
                        MethodDeclarationSyntax or
                        ConstructorDeclarationSyntax) ||
                    model.GetDeclaredSymbol(declaration) is not IMethodSymbol method ||
                    method.MethodKind is not (
                        MethodKind.Ordinary or MethodKind.Constructor) ||
                    IsCompanionType(method.ContainingType))
                    continue;
                targets.Add(new CallableTarget(
                    method,
                    declaration,
                    model,
                    CreateCallableId(method)));
            }
        }
        return [.. targets
            .OrderBy(static target => target.CallableId, StringComparer.Ordinal)
            .ThenBy(static target => target.Declaration.SpanStart)];
    }

    internal async Task<ImmutableArray<WorkerVerificationRecord>> VerifyAsync(
        CallableTarget target,
        MethodResourceBudget resourceBudget,
        CancellationToken cancellationToken) {
        if (resourceBudget == null)
            throw new ArgumentNullException(nameof(resourceBudget));
        cancellationToken.ThrowIfCancellationRequested();
        var factory = _factory;
        var binding = new ContractBinder(_compilation, factory)
            .Bind(target.Method);
        if (!binding.IsSuccess) {
            if (!HasContractSurface(target)) return [];
            return [CreateUnknown(
                target,
                0,
                MapBindingFailure(binding.Failure))];
        }
        var contracts = binding.Contracts!;
        var ensures = contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Ensures)
            .ToImmutableArray();
        if (ensures.IsDefaultOrEmpty) return [];
        if (!HasContiguousContractPrologue(contracts))
            return [.. ensures.Select((_, index) =>
                CreateUnknown(
                    target,
                    index,
                    WorkerVerificationReason.UnsupportedContract))];
        var body = LowerBody(target, contracts, factory);
        if (!body.IsSuccess)
            return [.. ensures.Select((_, index) =>
                CreateUnknown(target, index, body.Reason))];

        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var assumptionLabels = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var assumptionOrdinal = 0;
        foreach (var clause in contracts.Clauses) {
            if (clause.Kind == BoundContractKind.Ensures) continue;
            var predicate = ApplyBodySubstitutions(
                factory,
                clause.Condition,
                contracts,
                body.ReturnTerm);
            if (predicate == null ||
                GetDepth(predicate) > _maximumExpressionDepth)
                return [.. ensures.Select((_, index) =>
                    CreateUnknown(
                        target,
                        index,
                        WorkerVerificationReason.UnsupportedExpression))];
            ProofJustification justification = clause.Kind ==
                BoundContractKind.Assume
                ? new UserAssumedJustification(
                    new SourceLocationId(assumptionOrdinal))
                : new LoweredJustification(clause.SourceOperation);
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            assumptionLabels.Add(
                justification,
                clause.Kind.ToString().ToLowerInvariant() + ":" +
                assumptionOrdinal.ToString(CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }
        foreach (var specAssumption in body.SpecAssumptions) {
            if (GetDepth(specAssumption.Predicate) >
                _maximumExpressionDepth)
                return [.. ensures.Select((_, index) =>
                    CreateUnknown(
                        target,
                        index,
                        WorkerVerificationReason.UnsupportedExpression))];
            ProofJustification justification =
                new SpecJustification(specAssumption.Spec);
            assumptions.Add(new Assumption(
                factory,
                specAssumption.Predicate,
                justification));
            assumptionLabels.Add(
                justification,
                "spec:" + specAssumption.WitnessIdentifier);
        }
        if (!TryAddSourceDomainAssumptions(
                factory,
                contracts,
                body.ReturnTerm,
                assumptions,
                assumptionLabels))
            return [.. ensures.Select((_, index) =>
                CreateUnknown(
                    target,
                    index,
                    WorkerVerificationReason.UnsupportedExpression))];
        AddNormalCompletionAssumption(
            factory,
            body.ReturnTerm,
            assumptions,
            assumptionLabels);
        var assumptionsUseSupportedDomain = assumptions.All(assumption =>
            IsSupportedProofDomain(factory, assumption.Predicate));

        var records = ImmutableArray.CreateBuilder<WorkerVerificationRecord>(
            ensures.Length);
        for (var index = 0; index < ensures.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = ApplyBodySubstitutions(
                factory,
                ensures[index].Condition,
                contracts,
                body.ReturnTerm);
            if (condition == null) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerVerificationReason.MissingReturnValue));
                continue;
            }
            if (GetDepth(condition) > _maximumExpressionDepth) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerVerificationReason.DeepEnsures));
                continue;
            }
            if (!assumptionsUseSupportedDomain ||
                !IsSupportedProofDomain(factory, condition)) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerVerificationReason.UnsupportedExpression));
                continue;
            }
            if (!resourceBudget.TryStartQuery()) {
                AddResourceLimitRecords(
                    records,
                    target,
                    index,
                    ensures.Length);
                break;
            }
            var query = new VerificationQuery(
                factory,
                assumptions,
                new Goal(
                    factory,
                    condition,
                    ProofDiagnosticKind.Postcondition,
                    new SourceLocationId(index)));
            var outcome = await _kernel.VerifyAsync(
                query,
                cancellationToken).ConfigureAwait(false);
            if (resourceBudget.IsExceeded) {
                AddResourceLimitRecords(
                    records,
                    target,
                    index,
                    ensures.Length);
                break;
            }
            records.Add(CreateRecord(
                target,
                index,
                outcome,
                contracts,
                assumptionLabels,
                body.UsesSpecModeledCallResult));
        }
        return records.ToImmutable();
    }

    private static bool TryAddSourceDomainAssumptions(
        IrFactory factory,
        BoundMethodContracts contracts,
        IrTerm? returnTerm,
        ImmutableArray<Assumption>.Builder assumptions,
        IDictionary<ProofJustification, string> assumptionLabels) {
        var seenPredicates = assumptions
            .Select(static assumption => assumption.Predicate.Id)
            .ToHashSet();
        foreach (var variable in contracts.Variables
                     .Where(static variable => variable.Role is
                         BoundContractVariableRole.Receiver or
                         BoundContractVariableRole.Parameter or
                         BoundContractVariableRole.Result)
                     .OrderBy(static variable =>
                         GetDomainRoleOrder(variable.Role))
                     .ThenBy(static variable => variable.Ordinal)) {
            var sourceType = GetSourceType(variable, contracts);
            if (!TryGetNarrowIntegerRange(
                    sourceType?.SpecialType ?? SpecialType.None,
                    out var minimum,
                    out var maximum))
                continue;
            var value = variable.Role == BoundContractVariableRole.Result
                ? returnTerm
                : factory.Variable(variable.Variable);
            if (value == null || value.Type != factory.IntegerType)
                return false;
            var lower = factory.Binary(
                IrBinaryOperator.GreaterThanOrEqual,
                value,
                factory.Integer(minimum));
            var upper = factory.Binary(
                IrBinaryOperator.LessThanOrEqual,
                value,
                factory.Integer(maximum));
            var predicate = factory.Binary(
                IrBinaryOperator.AndAlso,
                lower,
                upper);
            if (predicate is IrBooleanTerm { Value: true } ||
                !seenPredicates.Add(predicate.Id))
                continue;
            var label = CreateDomainLabel(variable);
            ProofJustification justification = new LoweredJustification(
                factory.CreateOperation("source-" + label));
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            assumptionLabels.Add(justification, label);
        }
        return true;
    }

    private static void AddNormalCompletionAssumption(
        IrFactory factory,
        IrTerm? returnTerm,
        ImmutableArray<Assumption>.Builder assumptions,
        IDictionary<ProofJustification, string> assumptionLabels) {
        if (returnTerm == null) return;
        var predicate = factory.Binary(
            IrBinaryOperator.Equal,
            returnTerm,
            returnTerm);
        if (predicate is IrBooleanTerm { Value: true } ||
            assumptions.Any(assumption =>
                assumption.Predicate.Id == predicate.Id))
            return;
        ProofJustification justification = new LoweredJustification(
            factory.CreateOperation("body:normal-completion"));
        assumptions.Add(new Assumption(
            factory,
            predicate,
            justification));
        assumptionLabels.Add(
            justification,
            "body:normal-completion");
    }

    private static int GetDomainRoleOrder(BoundContractVariableRole role) =>
        role switch {
            BoundContractVariableRole.Receiver => 0,
            BoundContractVariableRole.Parameter => 1,
            BoundContractVariableRole.Result => 2,
            _ => 3
        };

    private static ITypeSymbol? GetSourceType(
        BoundContractVariable variable,
        BoundMethodContracts contracts) =>
        variable.Role switch {
            BoundContractVariableRole.Parameter
                when variable.Symbol is IParameterSymbol parameter =>
                parameter.Type,
            BoundContractVariableRole.Receiver =>
                variable.Symbol as ITypeSymbol,
            BoundContractVariableRole.Result => contracts.Target.ReturnType,
            _ => null
        };

    private static string CreateDomainLabel(
        BoundContractVariable variable) =>
        variable.Role switch {
            BoundContractVariableRole.Receiver => "domain:receiver",
            BoundContractVariableRole.Parameter =>
                "domain:parameter:" +
                variable.Ordinal.ToString(CultureInfo.InvariantCulture),
            BoundContractVariableRole.Result => "domain:result",
            _ => throw new ArgumentOutOfRangeException(nameof(variable))
        };

    private static bool TryGetNarrowIntegerRange(
        SpecialType type,
        out long minimum,
        out long maximum) {
        switch (type) {
            case SpecialType.System_SByte:
                minimum = sbyte.MinValue;
                maximum = sbyte.MaxValue;
                return true;
            case SpecialType.System_Byte:
                minimum = byte.MinValue;
                maximum = byte.MaxValue;
                return true;
            case SpecialType.System_Int16:
                minimum = short.MinValue;
                maximum = short.MaxValue;
                return true;
            case SpecialType.System_UInt16:
                minimum = ushort.MinValue;
                maximum = ushort.MaxValue;
                return true;
            case SpecialType.System_Char:
                minimum = char.MinValue;
                maximum = char.MaxValue;
                return true;
            case SpecialType.System_Int32:
                minimum = int.MinValue;
                maximum = int.MaxValue;
                return true;
            case SpecialType.System_UInt32:
                minimum = uint.MinValue;
                maximum = uint.MaxValue;
                return true;
            default:
                minimum = default;
                maximum = default;
                return false;
        }
    }

    private static bool IsSupportedProofDomain(
        IrFactory factory,
        IrTerm root) {
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable) {
                var kind = factory.GetTypeInfo(variable.Type).Kind;
                if (kind is not (IrTypeKind.Boolean or IrTypeKind.Integer))
                    return false;
            }
            if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat })
                return false;
            if (term is IrLengthTerm length &&
                length.Value.Type == factory.StringType)
                return false;
            foreach (var child in GetChildren(term)) pending.Push(child);
        }
        return true;
    }

    private bool HasContiguousContractPrologue(
        BoundMethodContracts contracts) {
        var expectedClauseCount = contracts.Clauses.Count(static clause =>
            clause.Evidence is
                BoundContractEvidence.CompilerBoundInvocation or
                BoundContractEvidence.Companion);
        if (expectedClauseCount == 0) return true;

        var observedClauseCount = 0;
        foreach (var syntaxReference in contracts.Source
                     .DeclaringSyntaxReferences
                     .OrderBy(static reference =>
                         reference.SyntaxTree.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static reference => reference.Span.Start)) {
            if (syntaxReference.GetSyntax() is not
                BaseMethodDeclarationSyntax { Body: { } body })
                continue;
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(
                        _compilation,
                        syntaxReference.SyntaxTree);
            var inPrologue = true;
            foreach (var statement in body.Statements) {
                if (statement is EmptyStatementSyntax) continue;
                if (TryGetDirectContractClause(
                        statement,
                        model,
                        out var invocation)) {
                    if (!inPrologue) return false;
                    observedClauseCount++;
                    if (invocation.Arguments
                        .SelectMany(static argument =>
                            argument.Value.DescendantsAndSelf())
                        .OfType<IInvocationOperation>()
                        .Any(IsContractClause))
                        return false;
                    continue;
                }

                inPrologue = false;
                var operation = model.GetOperation(statement);
                if (operation != null &&
                    operation.DescendantsAndSelf()
                        .OfType<IInvocationOperation>()
                        .Any(IsContractClause))
                    return false;
            }
        }
        return observedClauseCount == expectedClauseCount;
    }

    private bool TryGetDirectContractClause(
        StatementSyntax statement,
        SemanticModel model,
        [NotNullWhen(true)] out IInvocationOperation? invocation) {
        invocation = statement is ExpressionStatementSyntax expression
            ? model.GetOperation(expression.Expression) as IInvocationOperation
            : null;
        return invocation != null && IsContractClause(invocation);
    }

    private bool IsContractClause(IInvocationOperation invocation) =>
        _contractType != null &&
        SymbolEqualityComparer.Default.Equals(
            invocation.TargetMethod.ContainingType,
            _contractType) &&
        invocation.TargetMethod.Name is
            nameof(SharpProof.Attributes.Contract.Requires) or
            nameof(SharpProof.Attributes.Contract.Ensures) or
            nameof(SharpProof.Attributes.Contract.Assume);

    private void AddResourceLimitRecords(
        ImmutableArray<WorkerVerificationRecord>.Builder records,
        CallableTarget target,
        int start,
        int count) {
        for (var index = start; index < count; index++)
            records.Add(CreateUnknown(
                target,
                index,
                WorkerVerificationReason.ResourceLimit));
    }

    private BodyLoweringResult LowerBody(
        CallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory) {
        if (target.Method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None))
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        if (target.Method.ReturnsVoid ||
            target.Method.MethodKind == MethodKind.Constructor) {
            return ContainsOnlyContractStatements(target)
                ? BodyLoweringResult.Success(null)
                : BodyLoweringResult.Fail(
                    WorkerVerificationReason.UnsupportedBody);
        }

        var returnExpression = FindSingleReturnExpression(target);
        if (returnExpression == null)
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        var operation = target.SemanticModel.GetOperation(returnExpression);
        if (operation == null)
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        if (operation is IInvocationOperation invocation)
            return LowerSpecInvocation(
                target,
                contracts,
                factory,
                invocation);
        var lowering = new RoslynOperationLowerer(
            factory,
            IsKnownPure).Lower(operation);
        if (!TryRewriteLoweredTerm(
                target,
                contracts,
                factory,
                lowering,
                out var rewritten))
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        return GetDepth(rewritten) <= _maximumExpressionDepth
            ? BodyLoweringResult.Success(rewritten)
            : BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
    }

    private BodyLoweringResult LowerSpecInvocation(
        CallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory,
        IInvocationOperation invocation) {
        if (invocation.TargetMethod.ReducedFrom != null ||
            invocation.TargetMethod.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None) ||
            !_apiSpecs.TryGet(invocation.TargetMethod, out var resolved) ||
            resolved.Template.Facets.Effects.Effects != SpecEffect.None ||
            resolved.Template.Facets.Allocation.Behavior !=
                SpecAllocationBehavior.None ||
            resolved.Template.Postconditions.IsDefaultOrEmpty ||
            !resolved.Template.Result.HasValue ||
            !TryGetSpecResultType(
                factory,
                invocation.Type,
                resolved.Template.Target.ResultType,
                out var resultType) ||
            invocation.Arguments.Length !=
                resolved.Template.Parameters.Length)
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);

        var substitutions = new Dictionary<SpecVarId, IrTerm>();
        if (resolved.Template.Receiver.HasValue) {
            if (invocation.Instance == null ||
                !TryLowerSpecInput(
                    target,
                    contracts,
                    factory,
                    invocation.Instance,
                    out var receiver))
                return BodyLoweringResult.Fail(
                    WorkerVerificationReason.UnsupportedBody);
            substitutions.Add(
                resolved.Template.Receiver.Value,
                receiver);
        }
        else if (invocation.Instance != null) {
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        }

        var arguments = new IArgumentOperation?[
            resolved.Template.Parameters.Length];
        foreach (var argument in invocation.Arguments) {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                ordinal < 0 ||
                ordinal >= arguments.Length ||
                arguments[ordinal] != null)
                return BodyLoweringResult.Fail(
                    WorkerVerificationReason.UnsupportedBody);
            arguments[ordinal] = argument;
        }
        for (var index = 0; index < arguments.Length; index++) {
            var argument = arguments[index];
            if (argument == null ||
                !TryLowerSpecInput(
                    target,
                    contracts,
                    factory,
                    argument.Value,
                    out var lowered))
                return BodyLoweringResult.Fail(
                    WorkerVerificationReason.UnsupportedBody);
            substitutions.Add(
                resolved.Template.Parameters[index],
                lowered);
        }

        var resultVariable = factory.CreateVariable(
            "spec-call-result:" +
            resolved.Template.Target.WitnessIdentifier,
            resultType);
        var resultTerm = factory.Variable(resultVariable);
        substitutions.Add(
            resolved.Template.Result.Value,
            resultTerm);
        var instantiated =
            ApiSpecInstantiator.InstantiatePostconditions(
                resolved.Template,
                factory,
                substitutions);
        if (instantiated.Status != SpecInstantiationStatus.Succeeded ||
            instantiated.Postconditions.IsDefaultOrEmpty)
            return BodyLoweringResult.Fail(
                WorkerVerificationReason.UnsupportedBody);
        return BodyLoweringResult.SpecModeled(
            resultTerm,
            [.. instantiated.Postconditions.Select(predicate =>
                new BodySpecAssumption(
                    resolved.Template.Id,
                    resolved.Template.Target.WitnessIdentifier,
                    predicate))]);
    }

    private bool TryLowerSpecInput(
        CallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory,
        IOperation operation,
        [NotNullWhen(true)] out IrTerm? term) {
        var lowering = new RoslynOperationLowerer(
            factory,
            IsKnownPure).Lower(operation);
        if (!TryRewriteLoweredTerm(
                target,
                contracts,
                factory,
                lowering,
                out term) ||
            GetDepth(term) > _maximumExpressionDepth) {
            term = null;
            return false;
        }
        return true;
    }

    private static bool TryRewriteLoweredTerm(
        CallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory,
        FrontendLoweringResult lowering,
        [NotNullWhen(true)] out IrTerm? rewritten) {
        if (!lowering.IsExact) {
            rewritten = null;
            return false;
        }
        var replacements = new Dictionary<IrVarId, IrTerm>();
        var canonicalParameters = contracts.Variables
            .Where(static variable =>
                variable.Role == BoundContractVariableRole.Parameter)
            .ToDictionary(static variable => variable.Ordinal);
        foreach (var binding in lowering.Variables) {
            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    target.Method) ||
                 !canonicalParameters.TryGetValue(
                     parameter.Ordinal,
                     out var canonical)) {
                rewritten = null;
                return false;
            }
            replacements[binding.Variable] =
                factory.Variable(canonical.Variable);
        }
        foreach (var variable in CollectVariables(lowering.Term)) {
            if (replacements.ContainsKey(variable)) continue;
            rewritten = null;
            return false;
        }
        try {
            rewritten = IrSubstitution.Substitute(
                factory,
                lowering.Term,
                replacements);
            return true;
        }
        catch (ArgumentException) {
            rewritten = null;
            return false;
        }
    }

    private static bool TryGetSpecResultType(
        IrFactory factory,
        ITypeSymbol? sourceType,
        SpecValueType? specType,
        out IrTypeId resultType) {
        switch (specType) {
            case SpecValueType.Boolean
                when sourceType?.SpecialType ==
                    SpecialType.System_Boolean:
                resultType = factory.BooleanType;
                return true;
            case SpecValueType.Integer
                when sourceType?.SpecialType is
                    SpecialType.System_SByte or
                    SpecialType.System_Byte or
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Char or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64:
                resultType = factory.IntegerType;
                return true;
            case SpecValueType.String
                when sourceType?.SpecialType ==
                    SpecialType.System_String:
                resultType = factory.StringType;
                return true;
            default:
                resultType = default;
                return false;
        }
    }

    private ExpressionSyntax? FindSingleReturnExpression(
        CallableTarget target) {
        if (target.Declaration.ExpressionBody != null)
            return target.Declaration.ExpressionBody.Expression;
        if (target.Declaration.Body == null) return null;
        ExpressionSyntax? result = null;
        foreach (var statement in target.Declaration.Body.Statements) {
            if (statement is ReturnStatementSyntax { Expression: { } expression }) {
                if (result != null) return null;
                result = expression;
            }
            else if (!IsContractStatement(statement)) {
                return null;
            }
        }
        return result;
    }

    private bool ContainsOnlyContractStatements(CallableTarget target) =>
        target.Declaration.Body != null &&
        target.Declaration.Body.Statements.All(IsContractStatement);

    private bool IsContractStatement(StatementSyntax statement) {
        if (statement is EmptyStatementSyntax) return true;
        if (statement is not ExpressionStatementSyntax expression ||
            targetOperation(expression) is not IInvocationOperation invocation)
            return false;
        return _contractType != null &&
               SymbolEqualityComparer.Default.Equals(
                   invocation.TargetMethod.ContainingType,
                   _contractType);

        IOperation? targetOperation(ExpressionStatementSyntax value) =>
            SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, value.SyntaxTree)
                .GetOperation(value.Expression);
    }

    private static IrTerm? ApplyBodySubstitutions(
        IrFactory factory,
        IrTerm term,
        BoundMethodContracts contracts,
        IrTerm? returnTerm) {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables) {
            if (variable.Role == BoundContractVariableRole.PreState &&
                variable.CurrentStateVariable.HasValue)
                replacements[variable.Variable] =
                    factory.Variable(variable.CurrentStateVariable.Value);
            else if (variable.Role == BoundContractVariableRole.Result) {
                if (returnTerm == null) return null;
                replacements[variable.Variable] = returnTerm;
            }
        }
        try {
            return IrSubstitution.Substitute(
                factory,
                term,
                replacements);
        }
        catch (ArgumentException) {
            return null;
        }
    }

    private WorkerVerificationRecord CreateRecord(
        CallableTarget target,
        int contractOrdinal,
        ProofOutcome outcome,
        BoundMethodContracts contracts,
        IReadOnlyDictionary<ProofJustification, string> assumptionLabels,
        bool usesSpecModeledCallResult) {
        var record = CreateBaseRecord(target, contractOrdinal);
        switch (outcome) {
            case ProvenOutcome proven:
                record.Status = WorkerVerificationStatus.Proven;
                record.Reason = WorkerVerificationReason.None;
                record.ProofCore = [.. proven.Core
                    .Select(justification =>
                        assumptionLabels.TryGetValue(justification, out var label)
                            ? label
                            : "hygienic")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static label => label, StringComparer.Ordinal)];
                break;
            case RefutedOutcome when usesSpecModeledCallResult:
                record.Status = WorkerVerificationStatus.Unknown;
                record.Reason =
                    WorkerVerificationReason.CounterexampleReplayFailed;
                break;
            case RefutedOutcome refuted:
                record.Status = WorkerVerificationStatus.Refuted;
                record.Reason = WorkerVerificationReason.None;
                record.Model = CreateModel(refuted, contracts);
                break;
            case UnknownOutcome unknown:
                record.Status = WorkerVerificationStatus.Unknown;
                record.Reason = MapAbstention(unknown.Reason);
                break;
            default:
                record.Status = WorkerVerificationStatus.Unknown;
                record.Reason =
                    WorkerVerificationReason.MalformedBackendResult;
                break;
        }
        return record;
    }

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome,
        BoundMethodContracts contracts) {
        var names = contracts.Variables.ToDictionary(
            static variable => variable.Variable,
            static variable => variable.Role switch {
                BoundContractVariableRole.Parameter =>
                    "parameter:" + variable.Ordinal.ToString(
                        CultureInfo.InvariantCulture),
                BoundContractVariableRole.Receiver => "receiver",
                BoundContractVariableRole.Result => "result",
                BoundContractVariableRole.PreState =>
                    "pre:" + (variable.CurrentStateVariable?.Value ?? -1)
                        .ToString(CultureInfo.InvariantCulture),
                _ => "variable:" + variable.Variable.Value.ToString(
                    CultureInfo.InvariantCulture)
            });
        return [.. outcome.Model.Assignments
            .Select(assignment => new WorkerModelValue {
                Variable = names.TryGetValue(assignment.Key, out var name)
                    ? name
                    : "variable:" + assignment.Key.Value.ToString(
                        CultureInfo.InvariantCulture),
                Kind = assignment.Value.Kind.ToString(),
                Value = FormatValue(assignment.Value)
            })
            .OrderBy(static value => value.Variable, StringComparer.Ordinal)];
    }

    private static string FormatValue(IrValue value) => value.Kind switch {
        IrValueKind.Boolean => value.Boolean ? "true" : "false",
        IrValueKind.Integer => value.Integer.ToString(CultureInfo.InvariantCulture),
        IrValueKind.String => value.String,
        IrValueKind.Null => "null",
        _ => "<opaque>"
    };

    private WorkerVerificationRecord CreateUnknown(
        CallableTarget target,
        int contractOrdinal,
        WorkerVerificationReason reason) {
        var record = CreateBaseRecord(target, contractOrdinal);
        record.Status = WorkerVerificationStatus.Unknown;
        record.Reason = reason;
        return record;
    }

    private static WorkerVerificationRecord CreateBaseRecord(
        CallableTarget target,
        int contractOrdinal) {
        var location = target.Method.Locations
            .FirstOrDefault(static location => location.IsInSource);
        return new WorkerVerificationRecord {
            CallableId = target.CallableId,
            ContractOrdinal = contractOrdinal,
            SourcePath = location?.SourceTree?.FilePath ?? string.Empty,
            SourceStart = location?.SourceSpan.Start ?? target.Declaration.SpanStart,
            Status = WorkerVerificationStatus.Unknown,
            Reason = WorkerVerificationReason.InfrastructureFailure
        };
    }

    private bool HasContractSurface(CallableTarget target) {
        if (target.Method.GetAttributes().Any(IsSharpProofAttribute) ||
            target.Method.GetReturnTypeAttributes().Any(IsSharpProofAttribute) ||
            target.Method.Parameters.Any(parameter =>
                parameter.GetAttributes().Any(IsSharpProofAttribute)))
            return true;
        return target.Declaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation =>
                target.SemanticModel.GetOperation(invocation))
            .OfType<IInvocationOperation>()
            .Any(invocation =>
                _contractType != null &&
                SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.ContainingType,
                    _contractType));
    }

    private static bool IsSharpProofAttribute(AttributeData attribute) =>
        string.Equals(
            attribute.AttributeClass?.ContainingNamespace?.Name,
            "Attributes",
            StringComparison.Ordinal) &&
        string.Equals(
            attribute.AttributeClass?.ContainingNamespace?.ContainingNamespace?.Name,
            "SharpProof",
            StringComparison.Ordinal);

    private bool IsCompanionType(INamedTypeSymbol type) =>
        _contractForType != null &&
        type.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition,
                _contractForType));

    private static string CreateCallableId(IMethodSymbol method) =>
        DocumentationCommentId.CreateDeclarationId(method) ??
        method.ContainingType.MetadataName + "." + method.MetadataName;

    private static WorkerVerificationReason MapBindingFailure(
        ContractBindingFailure failure) => failure switch {
            ContractBindingFailure.UnsupportedExpression =>
                WorkerVerificationReason.UnsupportedExpression,
            ContractBindingFailure.ResultOutsideEnsures or
            ContractBindingFailure.OldOutsideEnsures or
            ContractBindingFailure.NestedOld or
            ContractBindingFailure.InvalidIntrinsicSignature or
            ContractBindingFailure.NonBooleanCondition or
            ContractBindingFailure.InvalidClosedAttribute =>
                WorkerVerificationReason.UnsupportedContract,
            _ => WorkerVerificationReason.UnsupportedCallable
        };

    private static WorkerVerificationReason MapAbstention(
        AbstentionReason reason) => reason switch {
            AbstentionReason.UnsupportedOperation =>
                WorkerVerificationReason.UnsupportedExpression,
            AbstentionReason.UnsupportedEncoding =>
                WorkerVerificationReason.UnsupportedExpression,
            AbstentionReason.ResourceLimit =>
                WorkerVerificationReason.ResourceLimit,
            AbstentionReason.Timeout =>
                WorkerVerificationReason.MethodTimeout,
            AbstentionReason.BackendUnavailable =>
                WorkerVerificationReason.BackendUnavailable,
            AbstentionReason.InfrastructureFailure =>
                WorkerVerificationReason.InfrastructureFailure,
            AbstentionReason.MalformedBackendResult =>
                WorkerVerificationReason.MalformedBackendResult,
            AbstentionReason.CounterexampleReplayFailed =>
                WorkerVerificationReason.CounterexampleReplayFailed,
            _ => WorkerVerificationReason.UnsupportedExpression
        };

    private bool IsKnownPure(IMethodSymbol method) =>
        _apiSpecs.IsPureAndAllocationFree(method);

    private static int GetDepth(IrTerm root) {
        var memo = new Dictionary<IrId, int>();
        return Visit(root);

        int Visit(IrTerm term) {
            if (memo.TryGetValue(term.Id, out var existing)) return existing;
            var children = GetChildren(term);
            var depth = children.Length == 0
                ? 1
                : 1 + children.Max(Visit);
            memo.Add(term.Id, depth);
            return depth;
        }
    }

    private static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root) {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable)
                result.Add(variable.Variable);
            foreach (var child in GetChildren(term)) pending.Push(child);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<IrTerm> GetChildren(IrTerm term) =>
        term switch {
            IrOpaqueTerm opaque =>
                [.. opaque.Receiver == null
                    ? opaque.Arguments
                    : opaque.Arguments.Insert(0, opaque.Receiver)],
            IrUnaryTerm unary => [unary.Operand],
            IrBinaryTerm binary => [binary.Left, binary.Right],
            IrConditionalTerm conditional =>
                [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            IrCastTerm cast => [cast.Operand],
            IrLengthTerm length => [length.Value],
            IrSequenceAccessTerm access => [access.Sequence, access.Index],
            _ => []
        };

    private readonly struct BodyLoweringResult {
        private BodyLoweringResult(
            IrTerm? returnTerm,
            ImmutableArray<BodySpecAssumption> specAssumptions,
            bool usesSpecModeledCallResult,
            WorkerVerificationReason reason,
            bool isSuccess) {
            ReturnTerm = returnTerm;
            SpecAssumptions = specAssumptions;
            UsesSpecModeledCallResult = usesSpecModeledCallResult;
            Reason = reason;
            IsSuccess = isSuccess;
        }

        internal IrTerm? ReturnTerm { get; }
        internal ImmutableArray<BodySpecAssumption> SpecAssumptions { get; }
        internal bool UsesSpecModeledCallResult { get; }
        internal WorkerVerificationReason Reason { get; }
        internal bool IsSuccess { get; }
        internal static BodyLoweringResult Success(IrTerm? term) =>
            new(term, [], false, WorkerVerificationReason.None, true);
        internal static BodyLoweringResult SpecModeled(
            IrTerm term,
            ImmutableArray<BodySpecAssumption> assumptions) =>
            new(
                term,
                assumptions,
                true,
                WorkerVerificationReason.None,
                true);
        internal static BodyLoweringResult Fail(
            WorkerVerificationReason reason) =>
            new(null, [], false, reason, false);
    }

    private readonly record struct BodySpecAssumption(
        SpecId Spec,
        string WitnessIdentifier,
        IrTerm Predicate);
}

internal sealed class CallableTarget(
    IMethodSymbol method,
    BaseMethodDeclarationSyntax declaration,
    SemanticModel semanticModel,
    string callableId) {
    internal IMethodSymbol Method { get; } = method;
    internal BaseMethodDeclarationSyntax Declaration { get; } = declaration;
    internal SemanticModel SemanticModel { get; } = semanticModel;
    internal string CallableId { get; } = callableId;
}
