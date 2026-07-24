using SharpProof.Attributes;
using static SharpProof.Symbolic.ComplexitySummary;
namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityAnalysisSession {
    private readonly Compilation _compilation;
    private readonly CancellationToken _cancellationToken;
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ComplexitySummary> _cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ControlFlowGraph?> _graphs = new(SymbolEqualityComparer.Default);
    private readonly MethodEffectAnalysisSession _effectAnalysis;

    internal SymbolicComplexityAnalysisSession(Compilation compilation, CancellationToken cancellationToken) {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _cancellationToken = cancellationToken;
        _effectAnalysis = new(compilation, cancellationToken);
    }

    public SymbolicComplexityResult Analyze(ResolvedMethodLikeTarget target) {
        var method = target.MethodSymbol!;
        return AnalyzeMethod(method, target.BodyNode!, target.SemanticModel).ToResult(method);
    }

    private ComplexitySummary AnalyzeMethod(IMethodSymbol method, SyntaxNode body, SemanticModel model) {
        _cancellationToken.ThrowIfCancellationRequested();
        var canonical = method.OriginalDefinition;
        if (_cache.TryGetValue(canonical, out var cached)) return cached;
        if (!_active.Add(canonical)) {
            var recursive = ComplexityValue.Recursive();
            return new ComplexitySummary(recursive, [], [SymbolicComplexityUnknownReason.RecursiveCycle],
                [Callee(canonical, recursive, SymbolicComplexityUnknownReason.RecursiveCycle)]);
        }
        try {
            var operation = MethodBodyOperationResolver.GetMethodBodyRootOperation(
                body, model, _cancellationToken) ?? model.GetOperation(body, _cancellationToken);
            var result = AnalyzeOperation(operation, model, canonical);
            _cache[canonical] = result;
            return result;
        }
        finally {
            _active.Remove(canonical);
        }
    }

    private ComplexitySummary AnalyzeOperation(IOperation? operation, SemanticModel model, IMethodSymbol method) {
        _cancellationToken.ThrowIfCancellationRequested();
        if (operation == null) return ComplexitySummary.Constant;
        return operation switch {
            IConditionalOperation conditional => AnalyzeConditional(conditional, model, method),
            IForLoopOperation loop => AnalyzeFor(loop, model, method),
            IForEachLoopOperation loop => AnalyzeForEach(loop, model, method),
            IWhileLoopOperation loop => AnalyzeWhile(loop, model, method),
            IInvocationOperation call => AnalyzeCall(call, call.Instance, call.Arguments, call.TargetMethod,
                null, model, method),
            IObjectCreationOperation creation => AnalyzeCall(creation, null, creation.Arguments,
                creation.Constructor, creation.Initializer, model, method),
            IPropertyReferenceOperation property => AnalyzeProperty(property, model, method),
            IArrayCreationOperation array => AnalyzeArray(array, model, method),
            ISwitchOperation @switch => AnalyzeSwitch(@switch, model, method),
            ISwitchExpressionOperation @switch => AnalyzeSwitchExpression(@switch, model, method),
            ITryOperation @try => AnalyzeTry(@try, model, method),
            IDynamicInvocationOperation or IDynamicIndexerAccessOperation or IDynamicObjectCreationOperation
                => Unknown(SymbolicComplexityUnknownReason.UnsupportedOperation, operation.Syntax),
            IDelegateCreationOperation or IAnonymousFunctionOperation or ILocalFunctionOperation or
                IMethodReferenceOperation => ComplexitySummary.Constant,
            _ => AnalyzeOperations(operation.ChildOperations, model, method)
        };
    }

    private ComplexitySummary AnalyzeOperations(
        IEnumerable<IOperation?> operations, SemanticModel model, IMethodSymbol method) =>
        Sequence(operations.Select(operation => AnalyzeOperation(operation, model, method)));

    private ComplexitySummary AnalyzeConditional(IConditionalOperation operation, SemanticModel model, IMethodSymbol method) {
        var condition = AnalyzeOperation(operation.Condition, model, method);
        var constant = model.GetConstantValue(operation.Condition.Syntax, _cancellationToken);
        if (constant is { HasValue: true, Value: bool value })
            return Sequence(condition, AnalyzeOperation(
                value ? operation.WhenTrue : operation.WhenFalse, model, method));
        return Sequence(condition,
            AnalyzeOperation(operation.WhenTrue, model, method),
            AnalyzeOperation(operation.WhenFalse, model, method));
    }

    private ComplexitySummary AnalyzeFor(IForLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var before = AnalyzeOperations(operation.Before, model, method);
        if (operation.Syntax is not ForStatementSyntax syntax ||
            !TryForBound(syntax, model, method, out var bound, out var description))
            return Sequence(before, Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape, operation.Syntax));
        var iteration = AnalyzeOperations(operation.AtLoopBottom
            .Prepend(operation.Condition).Append(operation.Body), model, method);
        return new ComplexityLoopModel(
            before, iteration, bound, "ForLoop", "for-loop", description, syntax).Apply(method);
    }

    private ComplexitySummary AnalyzeForEach(IForEachLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var collection = AnalyzeOperation(operation.Collection, model, method);
        if (!TryCollectionBound(operation.Collection, model, method, out var bound))
            return Sequence(collection, Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape, operation.Syntax));
        return new ComplexityLoopModel(
            collection,
            AnalyzeOperation(operation.Body, model, method),
            bound,
            "ForeachLoop",
            "foreach",
            operation.Collection.Syntax.ToString(),
            operation.Syntax).Apply(method);
    }

    private ComplexitySummary AnalyzeWhile(IWhileLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var parts = Sequence(
            AnalyzeOperation(operation.Condition, model, method),
            AnalyzeOperation(operation.Body, model, method));
        (ExpressionSyntax? Condition, StatementSyntax? Body, string Kind, string Label) loop = operation.Syntax switch {
            WhileStatementSyntax syntax => (syntax.Condition, syntax.Statement, "WhileLoop", "while-loop"),
            DoStatementSyntax syntax => (syntax.Condition, syntax.Statement, "DoLoop", "do-loop"),
            _ => (null, null, string.Empty, string.Empty)
        };
        if (loop.Condition == null)
            return Unknown(SymbolicComplexityUnknownReason.UnsupportedWhileLoop, operation.Syntax, parts);
        if (!TryWhileBound(loop.Condition, loop.Body!, model, method, out var bound, out var description))
            return Unknown(SymbolicComplexityUnknownReason.UnsupportedWhileLoop, operation.Syntax, parts);
        return new ComplexityLoopModel(
            ComplexitySummary.Constant, parts, bound, loop.Kind, loop.Label, description, operation.Syntax).Apply(method);
    }

    private ComplexitySummary AnalyzeProperty(IPropertyReferenceOperation property, SemanticModel model, IMethodSymbol method) {
        var arguments = property.Arguments.Select(argument => argument.Value);
        var children = AnalyzeOperations(property.Instance == null ? arguments : arguments.Prepend(property.Instance),
            model, method);
        return IsConstantProperty(property.Property) ? children : AnalyzeCall(
            property, property.Instance, property.Arguments, property.Property.GetMethod, null, model, method);
    }

    private ComplexitySummary AnalyzeCall(
        IOperation operation,
        IOperation? receiver,
        ImmutableArray<IArgumentOperation> arguments,
        IMethodSymbol? target,
        IOperation? initializer,
        SemanticModel model,
        IMethodSymbol method) {
        IEnumerable<IOperation> childOperations = arguments.Select(argument => argument.Value);
        if (receiver != null) childOperations = childOperations.Prepend(receiver);
        if (initializer != null) childOperations = childOperations.Append(initializer);
        var children = AnalyzeOperations(childOperations, model, method);
        if (target == null) return children;
        if (SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(target, operation))
            return Sequence(children,
                UnknownCallee(target, SymbolicComplexityUnknownReason.DynamicDispatch, operation.Syntax));
        if (!SymbolicMethodSourceResolver.IsBackedBySource(target))
            return Sequence(children, UnknownCallee(target, SymbolicComplexityUnknownReason.ExternalCallee,
                operation.Syntax, includeUnknownCallee: true));
        if (!SymbolicMethodSourceResolver.TryResolve(_compilation, target, static _ => true, false,
                _cancellationToken, out _, out var body, out var sourceModel) || body == null)
            return Sequence(children,
                UnknownCallee(target, SymbolicComplexityUnknownReason.UnknownCallee, operation.Syntax));
        var callee = AnalyzeMethod(target, body, sourceModel);
        var cost = Substitute(callee.Cost, arguments, receiver, model, method);
        var info = Callee(target, cost, cost.IsRecursive
            ? SymbolicComplexityUnknownReason.RecursiveCycle
            : cost.IsUnknown ? SymbolicComplexityUnknownReason.UnknownCallee : SymbolicComplexityUnknownReason.None);
        var result = new ComplexitySummary(cost, callee.Drivers, callee.Reasons, [info, .. callee.Callees]);
        if (!cost.IsConstant)
            result = result.WithDriver(Driver("Call",
                "call to " + info.MethodDisplayName + " contributes " + info.ComplexityText, operation.Syntax));
        return Sequence(children, result);
    }

    private ComplexitySummary AnalyzeArray(IArrayCreationOperation operation, SemanticModel model, IMethodSymbol method) {
        var parts = operation.DimensionSizes.Select(size => AnalyzeOperation(size, model, method))
            .Append(AnalyzeOperation(operation.Initializer, model, method)).ToArray();
        var cost = ComplexityValue.Constant;
        foreach (var dimension in operation.DimensionSizes) {
            if (!TryExpressionCost(dimension.Syntax as ExpressionSyntax, model, method, false, out var factor))
                return Unknown(SymbolicComplexityUnknownReason.UnsupportedOperation, operation.Syntax, parts);
            cost = ComplexityValue.Multiply(cost, factor);
        }
        return Sequence(parts.Append(new ComplexitySummary(cost,
            [Driver("ArrayInitialization", "array initialization costs " + cost.Text(method), operation.Syntax)],
            [], [])));
    }

    private ComplexitySummary AnalyzeSwitch(ISwitchOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(operation.Cases.SelectMany(@case =>
            @case.Clauses.Select(clause => AnalyzeOperation(clause, model, method))
                .Concat(@case.Body.Select(item => AnalyzeOperation(item, model, method))))
            .Prepend(AnalyzeOperation(operation.Value, model, method)));

    private ComplexitySummary AnalyzeSwitchExpression(
        ISwitchExpressionOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(operation.Arms.SelectMany(arm => new[] {
            AnalyzeOperation(arm.Pattern, model, method),
            AnalyzeOperation(arm.Guard, model, method),
            AnalyzeOperation(arm.Value, model, method)
        }).Prepend(AnalyzeOperation(operation.Value, model, method)));

    private ComplexitySummary AnalyzeTry(ITryOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(operation.Catches.Select(@catch => AnalyzeOperation(@catch.Handler, model, method))
            .Prepend(AnalyzeOperation(operation.Body, model, method))
            .Append(AnalyzeOperation(operation.Finally, model, method)));

    private bool TryForBound(
        ForStatementSyntax loop,
        SemanticModel model,
        IMethodSymbol method,
        out ComplexityValue bound,
        out string description) {
        bound = ComplexityValue.Constant;
        description = string.Empty;
        var assignment = loop.Initializers.Count == 1
            ? loop.Initializers[0] as AssignmentExpressionSyntax
            : null;
        (ISymbol? variable, ExpressionSyntax? initializer) =
            loop.Declaration is { Variables.Count: 1 } declaration
                ? (model.GetDeclaredSymbol(declaration.Variables[0], _cancellationToken),
                    declaration.Variables[0].Initializer?.Value)
                : assignment != null
                    ? (model.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol, assignment.Right)
                    : (null, null);
        if (variable is not ILocalSymbol and not IParameterSymbol ||
            initializer == null || !IsIntegralConstant(initializer, model) ||
            loop.Condition is not BinaryExpressionSyntax condition ||
            !TryCondition(condition, variable, model, method, out var direction, out bound, out description,
                out var boundDependencies) ||
            loop.Incrementors.Count != 1 ||
            !TryStep(loop.Incrementors[0], variable, model, out var step) || step != direction)
            return false;
        return !Mutates(variable, loop.Statement, model, ignoreRecognizedStep: false) &&
               !boundDependencies.Any(symbol => Mutates(symbol, loop.Statement, model, ignoreRecognizedStep: false));
    }

    private bool TryWhileBound(
        ExpressionSyntax condition,
        StatementSyntax body,
        SemanticModel model,
        IMethodSymbol method,
        out ComplexityValue bound,
        out string description) {
        bound = ComplexityValue.Constant;
        description = string.Empty;
        if (condition is not BinaryExpressionSyntax binary) return false;
        var left = model.GetSymbolInfo(Unwrap(binary.Left), _cancellationToken).Symbol;
        var right = model.GetSymbolInfo(Unwrap(binary.Right), _cancellationToken).Symbol;
        var variable = left is ILocalSymbol or IParameterSymbol ? left :
            right is ILocalSymbol or IParameterSymbol ? right : null;
        if (variable == null ||
            !TryCondition(binary, variable, model, method, out var direction, out bound, out description,
                out var boundDependencies))
            return false;
        var steps = CSharpSyntaxFacts.DescendantNodesInExecution(body).OfType<ExpressionSyntax>()
            .Select(expression => TryStep(expression, variable, model, out var step) ? (expression, step) : default)
            .Where(static item => item.step != Direction.None).ToArray();
        return steps.Length == 1 && steps[0].step == direction &&
               StepDominatesLoopBackEdges(condition, steps[0].expression, model, method) &&
               !Mutates(variable, body, model, ignoreRecognizedStep: true) &&
               !boundDependencies.Any(symbol => Mutates(symbol, body, model, ignoreRecognizedStep: false));
    }

    private bool StepDominatesLoopBackEdges(
        ExpressionSyntax condition,
        ExpressionSyntax step,
        SemanticModel model,
        IMethodSymbol method) {
        var graph = GetControlFlowGraph(condition, model, method);
        if (graph == null ||
            FindBlock(graph, condition) is not { } conditionBlock ||
            FindBlock(graph, step) is not { } stepBlock)
            return false;
        if (ReferenceEquals(conditionBlock, stepBlock))
            return ContainsSyntax(conditionBlock.Operations, step) &&
                   ContainsSyntax(conditionBlock.BranchValue, condition);
        var pending = new Stack<BasicBlock>(Successors(conditionBlock)
            .Where(successor => !ReferenceEquals(successor, stepBlock)));
        var visited = new HashSet<BasicBlock>();
        while (pending.Count != 0) {
            var block = pending.Pop();
            if (ReferenceEquals(block, conditionBlock)) return false;
            if (!visited.Add(block)) continue;
            foreach (var successor in Successors(block))
                if (!ReferenceEquals(successor, stepBlock))
                    pending.Push(successor);
        }
        return true;
    }

    private ControlFlowGraph? GetControlFlowGraph(
        SyntaxNode site,
        SemanticModel model,
        IMethodSymbol method) {
        method = method.OriginalDefinition;
        if (_graphs.TryGetValue(method, out var cached)) return cached;
        var root = CSharpSyntaxFacts.GetContainingExecutionRoot(site, ExecutionRootPolicy.Callable);
        ControlFlowGraph? graph = null;
        if (root != null) {
            try { graph = ControlFlowGraph.Create(root, model, _cancellationToken); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { }
        }
        _graphs[method] = graph;
        return graph;
    }

    private static BasicBlock? FindBlock(ControlFlowGraph graph, SyntaxNode syntax) =>
        graph.Blocks.FirstOrDefault(block => ContainsSyntax(
            block.Operations.Append(block.BranchValue), syntax));

    private static bool ContainsSyntax(IEnumerable<IOperation?> operations, SyntaxNode syntax) =>
        operations.Where(static operation => operation != null)
            .SelectMany(static operation => operation!.DescendantsAndSelf())
            .Any(operation => operation.Syntax.Span == syntax.Span);

    private static bool ContainsSyntax(IOperation? operation, SyntaxNode syntax) =>
        ContainsSyntax([operation], syntax);

    private static IEnumerable<BasicBlock> Successors(BasicBlock block) {
        var fallThrough = block.FallThroughSuccessor?.Destination;
        if (fallThrough != null) yield return fallThrough;
        var conditional = block.ConditionalSuccessor?.Destination;
        if (conditional != null && !ReferenceEquals(conditional, fallThrough))
            yield return conditional;
    }

    private bool TryCondition(
        BinaryExpressionSyntax condition,
        ISymbol variable,
        SemanticModel model,
        IMethodSymbol method,
        out Direction direction,
        out ComplexityValue bound,
        out string description,
        out IReadOnlyList<ISymbol> boundDependencies) {
        direction = Direction.None;
        bound = ComplexityValue.Constant;
        description = string.Empty;
        boundDependencies = [];
        var left = Unwrap(condition.Left);
        var right = Unwrap(condition.Right);
        var variableOnLeft = SymbolEqualityComparer.Default.Equals(
            model.GetSymbolInfo(left, _cancellationToken).Symbol, variable);
        var variableOnRight = SymbolEqualityComparer.Default.Equals(
            model.GetSymbolInfo(right, _cancellationToken).Symbol, variable);
        if (!variableOnLeft && !variableOnRight) return false;
        direction = (condition.Kind(), variableOnLeft) switch {
            (SyntaxKind.LessThanExpression, true) or (SyntaxKind.GreaterThanExpression, false) => Direction.Up,
            (SyntaxKind.GreaterThanExpression, true) or (SyntaxKind.LessThanExpression, false) => Direction.Down,
            _ => Direction.None
        };
        var expression = variableOnLeft ? right : left;
        if (direction == Direction.None || !TryExpressionCost(expression, model, method, false, out bound))
            return false;
        description = expression.ToString();
        boundDependencies = GetReferencedSymbols(expression, model);
        return true;
    }

    private bool TryStep(
        ExpressionSyntax expression, ISymbol variable, SemanticModel model, out Direction direction) {
        direction = Direction.None;
        expression = Unwrap(expression);
        if (CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(expression, out var operand, out var delta) &&
            RefersTo(operand, variable, model))
            return SetDirection(delta > 0 ? Direction.Up : Direction.Down, out direction);
        if (expression is not AssignmentExpressionSyntax assignment || !RefersTo(assignment.Left, variable, model))
            return false;
        if ((assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
              assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)) &&
            IsOne(assignment.Right, model))
            return SetDirection(
                assignment.IsKind(SyntaxKind.AddAssignmentExpression) ? Direction.Up : Direction.Down, out direction);
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Right is not BinaryExpressionSyntax binary)
            return false;
        if (binary.IsKind(SyntaxKind.AddExpression) &&
            ((RefersTo(binary.Left, variable, model) && IsOne(binary.Right, model)) ||
             (IsOne(binary.Left, model) && RefersTo(binary.Right, variable, model))))
            return SetDirection(Direction.Up, out direction);
        if (binary.IsKind(SyntaxKind.SubtractExpression) &&
            RefersTo(binary.Left, variable, model) && IsOne(binary.Right, model))
            return SetDirection(Direction.Down, out direction);
        return false;
    }

    private bool TryCollectionBound(IOperation collection, SemanticModel model, IMethodSymbol method, out ComplexityValue cost) {
        cost = ComplexityValue.Constant;
        var type = collection.Type;
        var supported = type?.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol ||
            type?.AllInterfaces.Any(interfaceType =>
                interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) == true;
        return supported && TryExpressionCost(collection.Syntax as ExpressionSyntax, model, method, true, out cost);
    }

    private bool TryExpressionCost(
        ExpressionSyntax? expression,
        SemanticModel model,
        IMethodSymbol method,
        bool length,
        out ComplexityValue cost) {
        cost = ComplexityValue.Constant;
        if (expression == null) return false;
        expression = Unwrap(expression);
        if (model.GetConstantValue(expression, _cancellationToken).HasValue) return true;
        var symbol = model.GetSymbolInfo(expression, _cancellationToken).Symbol;
        if (symbol is IParameterSymbol parameter) {
            cost = ComplexityValue.Variable("$p" + parameter.Ordinal.ToString(CultureInfo.InvariantCulture) +
                                 (length ? ":length" : ":value"));
            return true;
        }
        if (expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText is "Length" or "Count")
            return TryExpressionCost(member.Expression, model, method, true, out cost);
        if (expression is BinaryExpressionSyntax binary &&
            TryExpressionCost(binary.Left, model, method, length, out var left) &&
            TryExpressionCost(binary.Right, model, method, length, out var right)) {
            cost = ComplexityValue.Max(left, right);
            return true;
        }
        if (symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol) {
            cost = ComplexityValue.Variable("name:" + symbol.Name + (length ? ".Length" : string.Empty));
            return true;
        }
        return false;
    }

    private ComplexityValue Substitute(
        ComplexityValue cost,
        ImmutableArray<IArgumentOperation> arguments,
        IOperation? receiver,
        SemanticModel model,
        IMethodSymbol method) => cost.Substitute(key => {
            if (ComplexityValue.TryParseParameterKey(key, out var ordinal)) {
                if (ordinal >= 0 && ordinal < arguments.Length &&
                    TryExpressionCost(arguments[ordinal].Value.Syntax as ExpressionSyntax, model, method,
                        key.EndsWith(":length", StringComparison.Ordinal), out var replacement))
                    return replacement;
                return ComplexityValue.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
            }
            if (key.StartsWith("$this", StringComparison.Ordinal) &&
                TryExpressionCost(receiver?.Syntax as ExpressionSyntax, model, method,
                    key.EndsWith(".length", StringComparison.Ordinal), out var receiverCost))
                return receiverCost;
            return null;
        });

    private bool Mutates(ISymbol symbol, SyntaxNode body, SemanticModel model, bool ignoreRecognizedStep) {
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(body)) {
            if (ignoreRecognizedStep && node is ExpressionSyntax expression &&
                TryStep(expression, symbol, model, out _))
                continue;
            if (SymbolMutationFacts.TryGetMutationTarget(node, out var target) &&
                RefersTo(target, symbol, model))
                return true;
            if (node is InvocationExpressionSyntax invocation &&
                InvocationMutatesDependency(invocation, symbol, model))
                return true;
        }
        return false;
    }

    private bool InvocationMutatesDependency(
        InvocationExpressionSyntax invocation,
        ISymbol boundDependency,
        SemanticModel model) {
        if (model.GetOperation(invocation, _cancellationToken) is not IInvocationOperation operation)
            return false;
        var receiverAliases = operation.Instance != null &&
                              ReceiverReferencesDependency(operation.Instance, boundDependency, model);
        var aliasedArguments = operation.Arguments.Where(argument =>
            SymbolMutationFacts.ExpressionReferencesSymbol(
                argument.Value.Syntax, boundDependency, model, _cancellationToken)).ToArray();
        if (!receiverAliases && aliasedArguments.Length == 0) return false;
        if (!SymbolicMethodSourceResolver.TryResolve(
                _compilation, operation.TargetMethod, static _ => true, false, _cancellationToken,
                out var declaration, out _, out var sourceModel))
            return true;
        var summary = _effectAnalysis.AnalyzeCompilerSummary(operation.TargetMethod, declaration, sourceModel);
        var effects = summary.Effects;
        var unknown = (effects.Effects & SharpProofEffect.Unknown) != 0 ||
                      !effects.UnknownReasons.IsDefaultOrEmpty;
        if (receiverAliases &&
            ((effects.Effects & SharpProofEffect.WritesReceiverState) != 0 || unknown))
            return true;
        if (aliasedArguments.Any(argument => summary.WrittenArgumentOrdinals.Contains(argument.Parameter?.Ordinal ?? -1)))
            return true;
        return aliasedArguments.Length != 0 &&
               ((effects.Effects & SharpProofEffect.WritesArgumentState) != 0 &&
                summary.WrittenArgumentOrdinals.IsDefaultOrEmpty || unknown);
    }

    private bool ReceiverReferencesDependency(IOperation instance, ISymbol dependency, SemanticModel model) =>
        dependency is IFieldSymbol or IPropertySymbol &&
        !dependency.IsStatic &&
        instance is IInstanceReferenceOperation ||
        SymbolMutationFacts.ExpressionReferencesSymbol(
            instance.Syntax, dependency, model, _cancellationToken);

    private IReadOnlyList<ISymbol> GetReferencedSymbols(ExpressionSyntax expression, SemanticModel model) =>
        CSharpSyntaxFacts.DescendantNodesInExecution(expression).OfType<ExpressionSyntax>()
            .Select(node => model.GetSymbolInfo(node, _cancellationToken).Symbol)
            .OfType<ISymbol>().Distinct(SymbolEqualityComparer.Default).ToArray();

    private bool RefersTo(ExpressionSyntax expression, ISymbol symbol, SemanticModel model) =>
        SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(Unwrap(expression), _cancellationToken).Symbol, symbol);

    private bool IsIntegralConstant(ExpressionSyntax expression, SemanticModel model) {
        var value = model.GetConstantValue(expression, _cancellationToken);
        return value.HasValue && value.Value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    private bool IsOne(ExpressionSyntax expression, SemanticModel model) {
        var value = model.GetConstantValue(expression, _cancellationToken);
        return value.HasValue && Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture) == 1m;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) =>
        CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);

    private static bool IsConstantProperty(IPropertySymbol property) =>
        property.Name is "Length" or "Count" &&
        (property.ContainingType.SpecialType == SpecialType.System_String ||
         property.ContainingType is IArrayTypeSymbol) ||
        property.IsIndexer && property.ContainingType.SpecialType == SpecialType.System_String;

    private static bool SetDirection(Direction value, out Direction direction) {
        direction = value;
        return true;
    }

    private enum Direction { None, Up, Down }

}
