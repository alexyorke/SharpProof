using SharpProof.Attributes;
namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityAnalysisSession {
    private readonly Compilation _compilation;
    private readonly CancellationToken _cancellationToken;
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, Summary> _cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ControlFlowGraph?> _graphs = new(SymbolEqualityComparer.Default);
    private readonly MethodEffectAnalysisSession _effectAnalysis;

    internal SymbolicComplexityAnalysisSession(Compilation compilation, CancellationToken cancellationToken) {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _cancellationToken = cancellationToken;
        _effectAnalysis = new(compilation, cancellationToken);
    }

    public SymbolicComplexityResult Analyze(ResolvedMethodLikeTarget target) {
        var method = target.MethodSymbol!;
        var summary = AnalyzeMethod(method, target.BodyNode!, target.SemanticModel);
        return new SymbolicComplexityResult(
            new SymbolicComplexityInfo(
                summary.Cost.Text(method),
                summary.Cost.Kind,
                summary.Cost.IsUnknown,
                summary.Cost.IsUnknown,
                summary.Cost.IsRecursive),
            summary.Drivers.Distinct().ToArray(),
            summary.Reasons.Where(static reason => reason != SymbolicComplexityUnknownReason.None).Distinct().ToArray(),
            summary.Callees.Distinct().ToArray());
    }

    private Summary AnalyzeMethod(IMethodSymbol method, SyntaxNode body, SemanticModel model) {
        _cancellationToken.ThrowIfCancellationRequested();
        var canonical = method.OriginalDefinition;
        if (_cache.TryGetValue(canonical, out var cached)) return cached;
        if (!_active.Add(canonical)) {
            var recursive = Cost.Recursive();
            return new Summary(recursive, [], [SymbolicComplexityUnknownReason.RecursiveCycle],
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

    private Summary AnalyzeOperation(IOperation? operation, SemanticModel model, IMethodSymbol method) {
        _cancellationToken.ThrowIfCancellationRequested();
        if (operation == null) return Summary.Constant;
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
                IMethodReferenceOperation => Summary.Constant,
            _ => Sequence(operation.ChildOperations.Select(child => AnalyzeOperation(child, model, method)))
        };
    }

    private Summary AnalyzeConditional(IConditionalOperation operation, SemanticModel model, IMethodSymbol method) {
        var condition = AnalyzeOperation(operation.Condition, model, method);
        var constant = model.GetConstantValue(operation.Condition.Syntax, _cancellationToken);
        if (constant is { HasValue: true, Value: bool value })
            return Sequence(condition, AnalyzeOperation(
                value ? operation.WhenTrue : operation.WhenFalse, model, method));
        return Sequence(condition, Sequence(
            AnalyzeOperation(operation.WhenTrue, model, method),
            AnalyzeOperation(operation.WhenFalse, model, method)));
    }

    private Summary AnalyzeFor(IForLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var before = Sequence(operation.Before.Select(item => AnalyzeOperation(item, model, method)));
        if (operation.Syntax is not ForStatementSyntax syntax ||
            !TryForBound(syntax, model, method, out var bound, out var description))
            return Sequence(before, Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape, operation.Syntax));
        var iteration = Sequence(operation.AtLoopBottom
            .Select(item => AnalyzeOperation(item, model, method))
            .Prepend(AnalyzeOperation(operation.Condition, model, method))
            .Append(AnalyzeOperation(operation.Body, model, method)));
        return Sequence(before, Multiply(bound, iteration).WithDriver(
            Driver("ForLoop", "for-loop bound " + bound.Text(method) + " from " + description, syntax)));
    }

    private Summary AnalyzeForEach(IForEachLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var collection = AnalyzeOperation(operation.Collection, model, method);
        if (!TryCollectionBound(operation.Collection, model, method, out var bound))
            return Sequence(collection, Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape, operation.Syntax));
        return Sequence(collection, Multiply(bound, AnalyzeOperation(operation.Body, model, method)).WithDriver(
            Driver("ForeachLoop", "foreach bound " + bound.Text(method) + " from " +
                operation.Collection.Syntax, operation.Syntax)));
    }

    private Summary AnalyzeWhile(IWhileLoopOperation operation, SemanticModel model, IMethodSymbol method) {
        var parts = Sequence(
            AnalyzeOperation(operation.Condition, model, method),
            AnalyzeOperation(operation.Body, model, method));
        ExpressionSyntax? condition;
        StatementSyntax? body;
        string kind;
        string label;
        if (operation.Syntax is WhileStatementSyntax @while) {
            (condition, body, kind, label) = (@while.Condition, @while.Statement, "WhileLoop", "while-loop");
        }
        else if (operation.Syntax is DoStatementSyntax @do) {
            (condition, body, kind, label) = (@do.Condition, @do.Statement, "DoLoop", "do-loop");
        }
        else {
            return Unknown(SymbolicComplexityUnknownReason.UnsupportedWhileLoop, operation.Syntax, parts);
        }
        if (!TryWhileBound(condition, body, model, method, out var bound, out var description))
            return Unknown(SymbolicComplexityUnknownReason.UnsupportedWhileLoop, operation.Syntax, parts);
        return Multiply(bound, parts).WithDriver(
            Driver(kind, label + " bound " + bound.Text(method) + " from " + description, operation.Syntax));
    }

    private Summary AnalyzeProperty(IPropertyReferenceOperation property, SemanticModel model, IMethodSymbol method) {
        var children = Sequence(property.Instance == null
            ? property.Arguments.Select(argument => AnalyzeOperation(argument.Value, model, method))
            : property.Arguments.Select(argument => AnalyzeOperation(argument.Value, model, method))
                .Prepend(AnalyzeOperation(property.Instance, model, method)));
        if (IsConstantProperty(property.Property)) return children;
        return Sequence(children, AnalyzeCall(property, property.Instance, property.Arguments,
            property.Property.GetMethod, null, model, method));
    }

    private Summary AnalyzeCall(
        IOperation operation,
        IOperation? receiver,
        ImmutableArray<IArgumentOperation> arguments,
        IMethodSymbol? target,
        IOperation? initializer,
        SemanticModel model,
        IMethodSymbol method) {
        var children = new List<Summary>();
        if (receiver != null) children.Add(AnalyzeOperation(receiver, model, method));
        children.AddRange(arguments.Select(argument => AnalyzeOperation(argument.Value, model, method)));
        if (initializer != null) children.Add(AnalyzeOperation(initializer, model, method));
        if (target == null) return Sequence(children);
        if (SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(target, operation))
            return Sequence(children.Append(
                UnknownCallee(target, SymbolicComplexityUnknownReason.DynamicDispatch, operation.Syntax)));
        if (!SymbolicMethodSourceResolver.IsBackedBySource(target))
            return Sequence(children.Append(UnknownCallee(target, SymbolicComplexityUnknownReason.ExternalCallee,
                operation.Syntax, includeUnknownCallee: true)));
        if (!SymbolicMethodSourceResolver.TryResolve(_compilation, target, static _ => true, false,
                _cancellationToken, out _, out var body, out var sourceModel) || body == null)
            return Sequence(children.Append(
                UnknownCallee(target, SymbolicComplexityUnknownReason.UnknownCallee, operation.Syntax)));
        var callee = AnalyzeMethod(target, body, sourceModel);
        var cost = Substitute(callee.Cost, arguments, receiver, model, method);
        var info = Callee(target, cost, cost.IsRecursive
            ? SymbolicComplexityUnknownReason.RecursiveCycle
            : cost.IsUnknown ? SymbolicComplexityUnknownReason.UnknownCallee : SymbolicComplexityUnknownReason.None);
        var result = new Summary(cost, callee.Drivers, callee.Reasons, [info, .. callee.Callees]);
        if (!cost.IsConstant)
            result = result.WithDriver(Driver("Call",
                "call to " + info.MethodDisplayName + " contributes " + info.ComplexityText, operation.Syntax));
        return Sequence(children.Append(result));
    }

    private Summary AnalyzeArray(IArrayCreationOperation operation, SemanticModel model, IMethodSymbol method) {
        var parts = operation.DimensionSizes.Select(size => AnalyzeOperation(size, model, method))
            .Append(AnalyzeOperation(operation.Initializer, model, method)).ToArray();
        var cost = Cost.Constant;
        foreach (var dimension in operation.DimensionSizes) {
            if (!TryExpressionCost(dimension.Syntax as ExpressionSyntax, model, method, false, out var factor))
                return Unknown(SymbolicComplexityUnknownReason.UnsupportedOperation, operation.Syntax, parts);
            cost = Cost.Multiply(cost, factor);
        }
        return Sequence(parts.Append(new Summary(cost,
            [Driver("ArrayInitialization", "array initialization costs " + cost.Text(method), operation.Syntax)],
            [], [])));
    }

    private Summary AnalyzeSwitch(ISwitchOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(AnalyzeOperation(operation.Value, model, method), Sequence(operation.Cases.Select(@case =>
            Sequence(@case.Clauses.Select(clause => AnalyzeOperation(clause, model, method))
                .Concat(@case.Body.Select(item => AnalyzeOperation(item, model, method)))))));

    private Summary AnalyzeSwitchExpression(
        ISwitchExpressionOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(AnalyzeOperation(operation.Value, model, method), Sequence(operation.Arms.Select(arm =>
            Sequence(AnalyzeOperation(arm.Pattern, model, method),
                AnalyzeOperation(arm.Guard, model, method), AnalyzeOperation(arm.Value, model, method)))));

    private Summary AnalyzeTry(ITryOperation operation, SemanticModel model, IMethodSymbol method) =>
        Sequence(Sequence(operation.Catches.Select(@catch => AnalyzeOperation(@catch.Handler, model, method))
                .Prepend(AnalyzeOperation(operation.Body, model, method))),
            AnalyzeOperation(operation.Finally, model, method));

    private bool TryForBound(
        ForStatementSyntax loop,
        SemanticModel model,
        IMethodSymbol method,
        out Cost bound,
        out string description) {
        bound = Cost.Constant;
        description = string.Empty;
        ISymbol? variable = null;
        ExpressionSyntax? initializer = null;
        if (loop.Declaration is { Variables.Count: 1 } declaration) {
            variable = model.GetDeclaredSymbol(declaration.Variables[0], _cancellationToken);
            initializer = declaration.Variables[0].Initializer?.Value;
        }
        else if (loop.Initializers.Count == 1 &&
                 loop.Initializers[0] is AssignmentExpressionSyntax assignment) {
            variable = model.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol;
            initializer = assignment.Right;
        }
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
        out Cost bound,
        out string description) {
        bound = Cost.Constant;
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
        graph.Blocks.FirstOrDefault(block =>
            block.Operations.Append(block.BranchValue).Where(static operation => operation != null)
                .SelectMany(static operation => operation!.DescendantsAndSelf())
                .Any(operation => operation.Syntax.Span == syntax.Span));

    private static bool ContainsSyntax(IEnumerable<IOperation> operations, SyntaxNode syntax) =>
        operations.SelectMany(static operation => operation.DescendantsAndSelf())
            .Any(operation => operation.Syntax.Span == syntax.Span);

    private static bool ContainsSyntax(IOperation? operation, SyntaxNode syntax) =>
        operation != null && operation.DescendantsAndSelf()
            .Any(item => item.Syntax.Span == syntax.Span);

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
        out Cost bound,
        out string description,
        out IReadOnlyList<ISymbol> boundDependencies) {
        direction = Direction.None;
        bound = Cost.Constant;
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
            RefersTo(operand, variable, model)) {
            direction = delta > 0 ? Direction.Up : Direction.Down;
            return true;
        }
        if (expression is not AssignmentExpressionSyntax assignment || !RefersTo(assignment.Left, variable, model))
            return false;
        if ((assignment.IsKind(SyntaxKind.AddAssignmentExpression) ||
             assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)) &&
            IsOne(assignment.Right, model)) {
            direction = assignment.IsKind(SyntaxKind.AddAssignmentExpression) ? Direction.Up : Direction.Down;
            return true;
        }
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Right is not BinaryExpressionSyntax binary)
            return false;
        if (binary.IsKind(SyntaxKind.AddExpression) &&
            ((RefersTo(binary.Left, variable, model) && IsOne(binary.Right, model)) ||
             (IsOne(binary.Left, model) && RefersTo(binary.Right, variable, model)))) {
            direction = Direction.Up;
            return true;
        }
        if (binary.IsKind(SyntaxKind.SubtractExpression) &&
            RefersTo(binary.Left, variable, model) && IsOne(binary.Right, model)) {
            direction = Direction.Down;
            return true;
        }
        return false;
    }

    private bool TryCollectionBound(IOperation collection, SemanticModel model, IMethodSymbol method, out Cost cost) {
        cost = Cost.Constant;
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
        out Cost cost) {
        cost = Cost.Constant;
        if (expression == null) return false;
        expression = Unwrap(expression);
        if (model.GetConstantValue(expression, _cancellationToken).HasValue) return true;
        var symbol = model.GetSymbolInfo(expression, _cancellationToken).Symbol;
        if (symbol is IParameterSymbol parameter) {
            cost = Cost.Variable("$p" + parameter.Ordinal.ToString(CultureInfo.InvariantCulture) +
                                 (length ? ":length" : ":value"));
            return true;
        }
        if (expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText is "Length" or "Count")
            return TryExpressionCost(member.Expression, model, method, true, out cost);
        if (expression is BinaryExpressionSyntax binary &&
            TryExpressionCost(binary.Left, model, method, length, out var left) &&
            TryExpressionCost(binary.Right, model, method, length, out var right)) {
            cost = Cost.Max(left, right);
            return true;
        }
        if (symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol) {
            cost = Cost.Variable("name:" + symbol.Name + (length ? ".Length" : string.Empty));
            return true;
        }
        return false;
    }

    private Cost Substitute(
        Cost cost,
        ImmutableArray<IArgumentOperation> arguments,
        IOperation? receiver,
        SemanticModel model,
        IMethodSymbol method) => cost.Substitute(key => {
            if (key.StartsWith("$p", StringComparison.Ordinal)) {
                var colon = key.IndexOf(':');
                if (colon > 2 && int.TryParse(key.Substring(2, colon - 2), out var ordinal) &&
                    ordinal >= 0 && ordinal < arguments.Length &&
                    TryExpressionCost(arguments[ordinal].Value.Syntax as ExpressionSyntax, model, method,
                        key.EndsWith(":length", StringComparison.Ordinal), out var replacement))
                    return replacement;
                return Cost.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
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

    private IReadOnlyList<ISymbol> GetReferencedSymbols(ExpressionSyntax expression, SemanticModel model) {
        var symbols = new List<ISymbol>();
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(expression).OfType<ExpressionSyntax>()) {
            var symbol = model.GetSymbolInfo(node, _cancellationToken).Symbol;
            if (symbol != null && symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        }
        return symbols;
    }

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

    private static Summary Sequence(params Summary[] parts) => Sequence(parts.AsEnumerable());
    private static Summary Sequence(IEnumerable<Summary> parts) {
        var cost = Cost.Constant;
        var drivers = new List<SymbolicComplexityDriverInfo>();
        var reasons = new List<SymbolicComplexityUnknownReason>();
        var callees = new List<SymbolicComplexityCalleeInfo>();
        foreach (var part in parts) {
            cost = Cost.Max(cost, part.Cost);
            drivers.AddRange(part.Drivers);
            reasons.AddRange(part.Reasons);
            callees.AddRange(part.Callees);
        }
        return new Summary(cost, drivers, reasons, callees);
    }

    private static Summary Multiply(Cost factor, Summary value) =>
        value with { Cost = Cost.Multiply(factor, value.Cost) };

    private static Summary Unknown(
        SymbolicComplexityUnknownReason reason, SyntaxNode syntax, params Summary[] parts) {
        var combined = Sequence(parts);
        return new Summary(Cost.Unknown(reason),
            [.. combined.Drivers, Driver("Unknown", reason.ToString(), syntax)],
            [reason, .. combined.Reasons], combined.Callees);
    }

    private static Summary UnknownCallee(
        IMethodSymbol method,
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        bool includeUnknownCallee = false) =>
        new(Cost.Unknown(reason), [Driver("Unknown", reason.ToString(), syntax)],
            includeUnknownCallee ? [reason, SymbolicComplexityUnknownReason.UnknownCallee] : [reason],
            [Callee(method, Cost.Unknown(reason), reason)]);

    private static SymbolicComplexityDriverInfo Driver(string kind, string description, SyntaxNode syntax) =>
        new(kind, description, syntax.SpanStart, syntax.Span.Length);

    private static SymbolicComplexityCalleeInfo Callee(
        IMethodSymbol method, Cost cost, SymbolicComplexityUnknownReason reason) =>
        new(method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            cost.Text(method), cost.Kind, cost.IsUnknown, reason);

    private enum Direction { None, Up, Down }

    private sealed record Summary(
        Cost Cost,
        IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> Reasons,
        IReadOnlyList<SymbolicComplexityCalleeInfo> Callees) {
        internal static readonly Summary Constant = new(Cost.Constant, [], [], []);
        internal Summary WithDriver(SymbolicComplexityDriverInfo driver) =>
            this with { Drivers = [.. Drivers, driver] };
    }

    private sealed record Cost(
        ImmutableSortedDictionary<string, int>? Factors = null,
        ImmutableArray<Cost> Alternatives = default,
        SymbolicComplexityUnknownReason UnknownReason = SymbolicComplexityUnknownReason.None,
        bool IsRecursive = false) {
        internal static readonly Cost Constant = new(
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal));
        internal bool IsUnknown => IsRecursive || UnknownReason != SymbolicComplexityUnknownReason.None;
        internal bool IsConstant => !IsUnknown && Alternatives.IsDefaultOrEmpty && Factors?.Count == 0;
        internal SymbolicComplexityKind Kind => IsRecursive ? SymbolicComplexityKind.RecursiveUnknown :
            UnknownReason != SymbolicComplexityUnknownReason.None ? SymbolicComplexityKind.Unknown :
            !Alternatives.IsDefaultOrEmpty ? SymbolicComplexityKind.Max :
            Factors?.Count == 0 ? SymbolicComplexityKind.Constant :
            Factors?.Count == 1 && Factors.Single().Value == 1 ? SymbolicComplexityKind.Linear :
            Factors?.Count == 1 && Factors.Single().Value == 2 ? SymbolicComplexityKind.Quadratic :
            SymbolicComplexityKind.Product;

        internal static Cost Variable(string key) => new(
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal).Add(key, 1));
        internal static Cost Unknown(SymbolicComplexityUnknownReason reason) => new(UnknownReason: reason);
        internal static Cost Recursive() => new(
            UnknownReason: SymbolicComplexityUnknownReason.RecursiveCycle, IsRecursive: true);

        internal static Cost Max(Cost left, Cost right) {
            if (left.IsRecursive || right.IsRecursive) return Recursive();
            if (left.UnknownReason != SymbolicComplexityUnknownReason.None) return left;
            if (right.UnknownReason != SymbolicComplexityUnknownReason.None) return right;
            if (Dominates(left, right)) return left;
            if (Dominates(right, left)) return right;
            var alternatives = Expand(left).Concat(Expand(right)).Distinct().ToImmutableArray();
            return alternatives.Length == 1 ? alternatives[0] : new Cost(Alternatives: alternatives);
        }

        internal static Cost Multiply(Cost left, Cost right) {
            if (left.IsRecursive || right.IsRecursive) return Recursive();
            if (left.UnknownReason != SymbolicComplexityUnknownReason.None) return left;
            if (right.UnknownReason != SymbolicComplexityUnknownReason.None) return right;
            if (!left.Alternatives.IsDefaultOrEmpty)
                return left.Alternatives.Select(item => Multiply(item, right)).Aggregate(Max);
            if (!right.Alternatives.IsDefaultOrEmpty)
                return right.Alternatives.Select(item => Multiply(left, item)).Aggregate(Max);
            var factors = left.Factors ?? Constant.Factors!;
            foreach (var pair in right.Factors ?? Constant.Factors!)
                factors = factors.SetItem(pair.Key,
                    factors.TryGetValue(pair.Key, out var exponent) ? exponent + pair.Value : pair.Value);
            return new Cost(factors);
        }

        internal Cost Substitute(Func<string, Cost?> resolve) {
            if (IsUnknown) return this;
            if (!Alternatives.IsDefaultOrEmpty)
                return Alternatives.Select(item => item.Substitute(resolve)).Aggregate(Max);
            var result = Constant;
            foreach (var pair in Factors ?? Constant.Factors!) {
                var factor = resolve(pair.Key) ?? Variable(pair.Key);
                for (var index = 0; index < pair.Value; index++) result = Multiply(result, factor);
            }
            return result;
        }

        internal string Text(IMethodSymbol? method) => "O(" + Term(method) + ")";

        private string Term(IMethodSymbol? method) {
            if (IsRecursive) return "RecursiveUnknown";
            if (UnknownReason != SymbolicComplexityUnknownReason.None) return "Unknown";
            if (!Alternatives.IsDefaultOrEmpty)
                return "max(" + string.Join(", ", Alternatives.Select(item => item.Term(method))) + ")";
            if (Factors?.Count == 0) return "1";
            return string.Join(" * ", Factors!.Select(pair => Render(pair.Key, method) +
                (pair.Value == 1 ? string.Empty : "^" + pair.Value.ToString(CultureInfo.InvariantCulture))));
        }

        private static string Render(string key, IMethodSymbol? method) {
            if (key.StartsWith("$p", StringComparison.Ordinal)) {
                var colon = key.IndexOf(':');
                if (colon > 2 && int.TryParse(key.Substring(2, colon - 2), out var ordinal)) {
                    var name = method != null && ordinal < method.Parameters.Length
                        ? method.Parameters[ordinal].Name
                        : "p" + ordinal.ToString(CultureInfo.InvariantCulture);
                    return key.EndsWith(":length", StringComparison.Ordinal) ? name + ".Length" : name;
                }
            }
            return key.StartsWith("name:", StringComparison.Ordinal) ? key.Substring(5) : key;
        }

        private static IEnumerable<Cost> Expand(Cost cost) =>
            cost.Alternatives.IsDefaultOrEmpty ? [cost] : cost.Alternatives;

        private static bool Dominates(Cost left, Cost right) {
            if (!left.Alternatives.IsDefaultOrEmpty || !right.Alternatives.IsDefaultOrEmpty) return false;
            if (left.Factors?.Count == 0) return right.Factors?.Count == 0;
            if (right.Factors?.Count == 0) return true;
            return right.Factors!.All(pair =>
                left.Factors!.TryGetValue(pair.Key, out var exponent) && exponent >= pair.Value);
        }
    }
}
