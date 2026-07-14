using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityService
{
    public SymbolicComplexityResult Query(
        SymbolicSourceInput source,
        SymbolicQueryTarget target,
        SymbolicQueryOptions options,
        CancellationToken cancellationToken)
    {
        return SymbolicSourceInputDispatcher.Execute(
            source,
            target,
            options,
            SymbolicSourceCompilationKind.Complexity,
            "Complexity source kind is not supported.",
            QuerySyntaxTree,
            QueryNode,
            cancellationToken);
    }

    private SymbolicComplexityResult QuerySyntaxTree(
        SyntaxTree syntaxTree,
        Compilation compilation,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        if (syntaxTree == null) throw new ArgumentNullException(nameof(syntaxTree));

        if (compilation == null) throw new ArgumentNullException(nameof(compilation));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var resolved = SymbolicMethodLikeTargetResolver.Resolve(
            syntaxTree,
            semanticModel,
            target,
            "Complexity queries support point, position, line, or node targets only.",
            IsMethodLikeDeclaration,
            ResolveMethodLikeDeclaration,
            cancellationToken);
        return ExecuteAnalysis(resolved, compilation, cancellationToken);
    }

    private SymbolicComplexityResult QueryNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        SymbolicQueryTarget target,
        CancellationToken cancellationToken)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        if (target.Kind != SymbolicQueryTargetKind.Node)
            throw new NotSupportedException("Node complexity queries require a node target.");

        var resolved = SymbolicMethodLikeTargetResolver.ResolveNode(
            node,
            semanticModel,
            IsMethodLikeDeclaration,
            ResolveMethodLikeDeclaration,
            cancellationToken);
        return ExecuteAnalysis(resolved, semanticModel.Compilation, cancellationToken);
    }

    private static SymbolicComplexityResult ExecuteAnalysis(
        ResolvedComplexityTarget target,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var summary = new AnalysisSession(compilation, cancellationToken).Analyze(target);
        return CreateResult(target, summary, cancellationToken);
    }

    private static ResolvedComplexityTarget ResolveMethodLikeDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var bodyNode = SymbolicMethodSourceResolver.GetBodyNode(declaration);
        if (bodyNode == null)
            throw new ArgumentException("The requested method-like declaration does not have a body.");

        var symbol = GetMethodLikeSymbol(declaration, semanticModel, cancellationToken);
        if (symbol == null)
            throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");

        var syntaxTree = declaration.SyntaxTree;
        var span = SymbolicSourceLocation.GetNodeSourceSpan(syntaxTree, declaration.Span, cancellationToken);
        var methodName = GetMethodName(symbol, declaration);
        return new ResolvedComplexityTarget(
            syntaxTree,
            semanticModel,
            declaration,
            bodyNode,
            symbol,
            syntaxTree.FilePath ?? string.Empty,
            methodName,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            GetDeclarationKind(declaration),
            declaration.Span.Start,
            declaration.Span.End,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn);
    }

    private static SymbolicComplexityResult CreateResult(
        ResolvedComplexityTarget target,
        MethodAnalysisSummary summary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var complexity = new SymbolicComplexityInfo(
            summary.Cost.ToBigOText(target.Symbol),
            summary.Cost.ToPublicKind(),
            summary.Cost.IsConservative,
            summary.Cost.IsUnknown,
            summary.Cost.IsRecursiveUnknown);
        return new SymbolicComplexityResult(
            target.FilePath,
            target.MethodName,
            target.MethodDisplayName,
            target.DeclarationKind,
            target.SpanStart,
            target.SpanEnd,
            target.StartLine,
            target.StartColumn,
            target.EndLine,
            target.EndColumn,
            complexity,
            DistinctDrivers(summary.Drivers),
            DistinctUnknownReasons(summary.UnknownReasons),
            DistinctCalleeSummaries(summary.CalleeSummaries));
    }

    private static IReadOnlyList<SymbolicComplexityDriverInfo> DistinctDrivers(
        IEnumerable<SymbolicComplexityDriverInfo> drivers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicComplexityDriverInfo>();
        foreach (var driver in drivers)
        {
            var key = string.Join(
                "\u001f",
                driver.Kind,
                driver.Description,
                driver.SourceSpanStart.ToString(CultureInfo.InvariantCulture),
                driver.SourceSpanLength.ToString(CultureInfo.InvariantCulture),
                driver.SourceLine.ToString(CultureInfo.InvariantCulture),
                driver.SourceColumn.ToString(CultureInfo.InvariantCulture));
            if (seen.Add(key)) distinct.Add(driver);
        }

        return distinct;
    }

    private static IReadOnlyList<SymbolicComplexityUnknownReason> DistinctUnknownReasons(
        IEnumerable<SymbolicComplexityUnknownReason> reasons)
    {
        return reasons
            .Where(static reason => reason != SymbolicComplexityUnknownReason.None)
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SymbolicComplexityCalleeInfo> DistinctCalleeSummaries(
        IEnumerable<SymbolicComplexityCalleeInfo> callees)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new List<SymbolicComplexityCalleeInfo>();
        foreach (var callee in callees)
        {
            var key = string.Join(
                "\u001f",
                callee.MethodDisplayName,
                callee.ComplexityText,
                callee.Kind.ToString(),
                callee.IsConservative.ToString(),
                callee.UnknownReason.ToString());
            if (seen.Add(key)) distinct.Add(callee);
        }

        return distinct;
    }

    private static bool IsMethodLikeDeclaration(SyntaxNode node)
    {
        return node is BaseMethodDeclarationSyntax ||
               node is AccessorDeclarationSyntax ||
               node is PropertyDeclarationSyntax ||
               node is IndexerDeclarationSyntax ||
               node is LocalFunctionStatementSyntax ||
               node is AnonymousFunctionExpressionSyntax;
    }

    private static IMethodSymbol? GetMethodLikeSymbol(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (declaration is AnonymousFunctionExpressionSyntax anonymousFunction)
            return semanticModel.GetOperation(anonymousFunction, cancellationToken) is IAnonymousFunctionOperation
                lambda
                ? lambda.Symbol
                : null;

        return semanticModel.GetDeclaredSymbol(declaration, cancellationToken) switch
        {
            IMethodSymbol method => method,
            IPropertySymbol property => property.GetMethod,
            _ => null
        };
    }

    private static string GetDeclarationKind(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            DestructorDeclarationSyntax => "destructor",
            OperatorDeclarationSyntax => "operator",
            ConversionOperatorDeclarationSyntax => "conversion_operator",
            AccessorDeclarationSyntax accessor => "accessor:" + accessor.Keyword.ValueText,
            PropertyDeclarationSyntax => "property_getter",
            IndexerDeclarationSyntax => "indexer_getter",
            LocalFunctionStatementSyntax => "local_function",
            AnonymousFunctionExpressionSyntax => "anonymous_function",
            _ => declaration.Kind().ToString()
        };
    }

    private static string GetMethodName(IMethodSymbol symbol, SyntaxNode declaration)
    {
        if (!string.IsNullOrWhiteSpace(symbol.Name)) return symbol.Name;

        return declaration switch
        {
            AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
            AnonymousFunctionExpressionSyntax => "anonymous_function",
            _ => declaration.Kind().ToString()
        };
    }

    private sealed class AnalysisSession
    {
        private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;

        private readonly Dictionary<IMethodSymbol, MethodAnalysisSummary> _summaryCache =
            new(SymbolEqualityComparer.Default);

        public AnalysisSession(Compilation compilation, CancellationToken cancellationToken)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _cancellationToken = cancellationToken;
        }

        public MethodAnalysisSummary Analyze(ResolvedComplexityTarget target)
        {
            return AnalyzeMethod(target.Symbol, target.Declaration, target.BodyNode, target.SemanticModel);
        }

        private MethodAnalysisSummary AnalyzeMethod(
            IMethodSymbol methodSymbol,
            SyntaxNode declaration,
            SyntaxNode bodyNode,
            SemanticModel semanticModel)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var canonical = methodSymbol.OriginalDefinition;
            if (_summaryCache.TryGetValue(canonical, out var cached)) return cached;

            if (_active.Contains(canonical))
                return CreateSummary(
                    SymbolicCostExpression.RecursiveUnknown(),
                    Array.Empty<SymbolicComplexityDriverInfo>(),
                    new[] { SymbolicComplexityUnknownReason.RecursiveCycle },
                    new[]
                    {
                        CreateCalleeInfo(
                            canonical.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            SymbolicCostExpression.RecursiveUnknown(),
                            canonical)
                    });

            _active.Add(canonical);
            try
            {
                var operation = semanticModel.GetOperation(bodyNode, _cancellationToken);
                var bodyCost = operation != null
                    ? AnalyzeOperation(operation, semanticModel, canonical)
                    : AnalyzeTopLevelInvocations(bodyNode, semanticModel, canonical);
                var summary = CreateSummary(
                    bodyCost.Cost,
                    bodyCost.Drivers,
                    bodyCost.UnknownReasons,
                    bodyCost.CalleeSummaries);
                _summaryCache[canonical] = summary;
                return summary;
            }
            finally
            {
                _active.Remove(canonical);
            }
        }

        private IMethodSymbol? ResolveInvocationTargetMethod(
            InvocationExpressionSyntax invocationSyntax,
            SemanticModel semanticModel,
            out IInvocationOperation? invocationOperation)
        {
            invocationOperation =
                semanticModel.GetOperation(invocationSyntax, _cancellationToken) as IInvocationOperation;
            var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax, _cancellationToken);
            var expressionSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax.Expression, _cancellationToken);
            return invocationOperation?.TargetMethod ??
                   invocationSymbolInfo.Symbol as IMethodSymbol ??
                   expressionSymbolInfo.Symbol as IMethodSymbol ??
                   invocationSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault() ??
                   expressionSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        }

        private ComplexityArtifacts AnalyzeExternalInvocationFallbacks(
            SyntaxNode bodyNode,
            SemanticModel semanticModel)
        {
            foreach (var invocation in EnumerateTopLevelInvocationTargets(bodyNode, semanticModel))
            {
                var (invocationSyntax, _, targetMethod) = invocation;
                if (targetMethod == null)
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnknownCallee,
                        invocationSyntax,
                        invocationSyntax.SyntaxTree,
                        _cancellationToken);

                if (TryGetKnownMethodCost(targetMethod, out _)) continue;

                if (!IsSourceMethod(targetMethod))
                    return CreateUnknownCalleeArtifacts(
                        targetMethod,
                        SymbolicComplexityUnknownReason.ExternalCallee,
                        invocationSyntax);
            }

            return ComplexityArtifacts.Constant;
        }

        private ComplexityArtifacts AnalyzeTopLevelInvocations(
            SyntaxNode bodyNode,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var invocationCosts = new List<ComplexityArtifacts>();
            foreach (var invocation in EnumerateTopLevelInvocationTargets(bodyNode, semanticModel))
            {
                var (invocationSyntax, invocationOperation, targetMethod) = invocation;
                if (targetMethod == null)
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnknownCallee,
                        invocationSyntax,
                        invocationSyntax.SyntaxTree,
                        _cancellationToken);

                invocationCosts.Add(AnalyzeMethodCall(
                    targetMethod,
                    invocationOperation,
                    invocationSyntax,
                    semanticModel,
                    currentMethod,
                    invocationOperation != null
                        ? GetArgumentSyntaxes(targetMethod, invocationOperation.Arguments)
                        : invocationSyntax.ArgumentList.Arguments
                            .Select(static argument => (SyntaxNode)argument.Expression)
                            .ToImmutableArray(),
                    invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                        ? memberAccess.Expression
                        : null));
            }

            return CombineSequence(invocationCosts);
        }

        private IEnumerable<(
            InvocationExpressionSyntax Syntax,
            IInvocationOperation? Operation,
            IMethodSymbol? TargetMethod)> EnumerateTopLevelInvocationTargets(
            SyntaxNode bodyNode,
            SemanticModel semanticModel)
        {
            foreach (var invocationSyntax in bodyNode.DescendantNodes(static candidate =>
                             !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                         .OfType<InvocationExpressionSyntax>())
            {
                var targetMethod =
                    ResolveInvocationTargetMethod(invocationSyntax, semanticModel, out var invocationOperation);
                yield return (invocationSyntax, invocationOperation, targetMethod);
            }
        }

        private ComplexityArtifacts AnalyzeOperation(
            IOperation? operation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (operation == null) return ComplexityArtifacts.Constant;

            switch (operation)
            {
                case IBlockOperation block:
                    return CombineSequence(block.Operations.Select(child =>
                        AnalyzeOperation(child, semanticModel, currentMethod)));

                case IVariableDeclarationGroupOperation group:
                    {
                        var parts = new List<ComplexityArtifacts>();
                        foreach (var declaration in group.Declarations)
                            foreach (var declarator in declaration.Declarators)
                                if (declarator.Initializer != null)
                                    parts.Add(AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod));

                        return CombineSequence(parts);
                    }

                case IVariableDeclaratorOperation declarator:
                    return declarator.Initializer == null
                        ? ComplexityArtifacts.Constant
                        : AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod);

                case IExpressionStatementOperation expressionStatement:
                    return AnalyzeOperation(expressionStatement.Operation, semanticModel, currentMethod);

                case IReturnOperation returnOperation:
                    return returnOperation.ReturnedValue != null
                        ? CombineSequence(
                            new[]
                            {
                                AnalyzeOperation(returnOperation.ReturnedValue, semanticModel, currentMethod)
                            }.Concat(returnOperation.ChildOperations
                                .Where(child => !ReferenceEquals(child, returnOperation.ReturnedValue))
                                .Select(child => AnalyzeOperation(child, semanticModel, currentMethod))))
                        : CombineSequence(returnOperation.ChildOperations.Select(child =>
                            AnalyzeOperation(child, semanticModel, currentMethod)));

                case IConditionalOperation conditionalOperation:
                    return AnalyzeConditionalOperation(conditionalOperation, semanticModel, currentMethod);

                case IForLoopOperation forLoopOperation:
                    return AnalyzeForLoop(forLoopOperation, semanticModel, currentMethod);

                case IForEachLoopOperation forEachLoopOperation:
                    return AnalyzeForEachLoop(forEachLoopOperation, semanticModel, currentMethod);

                case IWhileLoopOperation whileLoopOperation:
                    return AnalyzeWhileLikeLoop(whileLoopOperation, semanticModel, currentMethod);

                case IInvocationOperation invocationOperation:
                    return AnalyzeInvocation(invocationOperation, semanticModel, currentMethod);

                case IObjectCreationOperation objectCreationOperation:
                    return AnalyzeObjectCreation(objectCreationOperation, semanticModel, currentMethod);

                case IPropertyReferenceOperation propertyReferenceOperation:
                    return AnalyzePropertyReference(propertyReferenceOperation, semanticModel, currentMethod);

                case IArrayCreationOperation arrayCreationOperation:
                    return AnalyzeArrayCreation(arrayCreationOperation, semanticModel, currentMethod);

                case IDelegateCreationOperation:
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                case IMethodReferenceOperation:
                    return ComplexityArtifacts.Constant;

                case ISwitchOperation switchOperation:
                    return AnalyzeSwitchOperation(switchOperation, semanticModel, currentMethod);

                case ISwitchExpressionOperation switchExpressionOperation:
                    return AnalyzeSwitchExpressionOperation(switchExpressionOperation, semanticModel, currentMethod);

                case ITryOperation tryOperation:
                    return AnalyzeTryOperation(tryOperation, semanticModel, currentMethod);

                case IAwaitOperation awaitOperation:
                    return AnalyzeOperation(awaitOperation.Operation, semanticModel, currentMethod);

                case IDynamicInvocationOperation:
                case IDynamicIndexerAccessOperation:
                case IDynamicObjectCreationOperation:
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedOperation,
                        operation.Syntax,
                        operation.Syntax.SyntaxTree,
                        _cancellationToken);

                default:
                    return CombineSequence(operation.ChildOperations.Select(child =>
                        AnalyzeOperation(child, semanticModel, currentMethod)));
            }
        }

        private ComplexityArtifacts AnalyzeConditionalOperation(
            IConditionalOperation conditionalOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var conditionCost = AnalyzeOperation(conditionalOperation.Condition, semanticModel, currentMethod);
            if (TryGetConstantBoolean(conditionalOperation.Condition.Syntax, semanticModel, out var constantValue))
                return CombineSequence(
                    conditionCost,
                    constantValue
                        ? AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod)
                        : AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod));

            return CombineSequence(
                conditionCost,
                CombineBranch(
                    AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod),
                    AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod)));
        }

        private ComplexityArtifacts AnalyzeForLoop(
            IForLoopOperation forLoopOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var beforeCost =
                CombineSequence(
                    forLoopOperation.Before.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
            var conditionCost = AnalyzeOperation(forLoopOperation.Condition, semanticModel, currentMethod);
            var bottomCost =
                CombineSequence(
                    forLoopOperation.AtLoopBottom.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
            var bodyCost = AnalyzeOperation(forLoopOperation.Body, semanticModel, currentMethod);

            if (forLoopOperation.Syntax is not ForStatementSyntax forStatement ||
                !TryGetForLoopBound(forStatement, semanticModel, currentMethod, out var bound))
                return CombineSequence(
                    beforeCost,
                    ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                        forLoopOperation.Syntax,
                        forLoopOperation.Syntax.SyntaxTree,
                        _cancellationToken,
                        conditionCost,
                        bottomCost,
                        bodyCost));

            var perIteration = CombineSequence(conditionCost, bottomCost, bodyCost);
            var multiplied = Multiply(bound.Cost, perIteration);
            multiplied = multiplied.WithDriver(CreateDriver(
                "ForLoop",
                "for-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                forStatement,
                forStatement.SyntaxTree,
                _cancellationToken));
            return CombineSequence(beforeCost, multiplied);
        }

        private ComplexityArtifacts AnalyzeForEachLoop(
            IForEachLoopOperation forEachLoopOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var collectionCost = AnalyzeOperation(forEachLoopOperation.Collection, semanticModel, currentMethod);
            var bodyCost = AnalyzeOperation(forEachLoopOperation.Body, semanticModel, currentMethod);

            if (forEachLoopOperation.Syntax is not CommonForEachStatementSyntax foreachSyntax ||
                !TryGetForeachBound(forEachLoopOperation.Collection.Syntax, semanticModel, currentMethod,
                    out var bound))
                return CombineSequence(
                    collectionCost,
                    ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                        forEachLoopOperation.Syntax,
                        forEachLoopOperation.Syntax.SyntaxTree,
                        _cancellationToken,
                        bodyCost));

            var multiplied = Multiply(bound.Cost, bodyCost);
            multiplied = multiplied.WithDriver(CreateDriver(
                "ForeachLoop",
                "foreach bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                foreachSyntax,
                foreachSyntax.SyntaxTree,
                _cancellationToken));
            return CombineSequence(collectionCost, multiplied);
        }

        private ComplexityArtifacts AnalyzeWhileLikeLoop(
            IWhileLoopOperation loopOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var conditionCost = AnalyzeOperation(loopOperation.Condition, semanticModel, currentMethod);
            var bodyCost = AnalyzeOperation(loopOperation.Body, semanticModel, currentMethod);
            var (condition, body, driverKind, description) = loopOperation.Syntax switch
            {
                WhileStatementSyntax statement =>
                    (statement.Condition, statement.Statement, "WhileLoop", "while-loop"),
                DoStatementSyntax statement =>
                    (statement.Condition, statement.Statement, "DoLoop", "do-loop"),
                _ => (null, null, string.Empty, string.Empty)
            };

            if (condition == null ||
                body == null ||
                !TryGetWhileLikeBound(
                    condition,
                    body,
                    semanticModel,
                    currentMethod,
                    out var bound))
                return ComplexityArtifacts.Unknown(
                    SymbolicComplexityUnknownReason.UnsupportedWhileLoop,
                    loopOperation.Syntax,
                    loopOperation.Syntax.SyntaxTree,
                    _cancellationToken,
                    conditionCost,
                    bodyCost);

            var multiplied = Multiply(bound.Cost, CombineSequence(conditionCost, bodyCost));
            multiplied = multiplied.WithDriver(CreateDriver(
                driverKind,
                description + " bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                loopOperation.Syntax,
                loopOperation.Syntax.SyntaxTree,
                _cancellationToken));
            return multiplied;
        }

        private ComplexityArtifacts AnalyzeInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var receiverAndArguments = new List<ComplexityArtifacts>();
            if (invocationOperation.Instance != null)
                receiverAndArguments.Add(AnalyzeOperation(invocationOperation.Instance, semanticModel, currentMethod));

            foreach (var argument in invocationOperation.Arguments)
                receiverAndArguments.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

            var callCost = AnalyzeMethodCall(
                invocationOperation.TargetMethod,
                invocationOperation,
                invocationOperation.Syntax,
                semanticModel,
                currentMethod,
                GetArgumentSyntaxes(invocationOperation.TargetMethod, invocationOperation.Arguments),
                invocationOperation.Instance?.Syntax);
            receiverAndArguments.Add(callCost);
            return CombineSequence(receiverAndArguments);
        }

        private static ImmutableArray<SyntaxNode> GetArgumentSyntaxes(
            IMethodSymbol method,
            ImmutableArray<IArgumentOperation> arguments)
        {
            if (arguments.IsDefaultOrEmpty) return ImmutableArray<SyntaxNode>.Empty;

            // Callee factors are parameter-ordinal based. Roslyn includes implicit optional and
            // expanded params arguments in the operation list, while source ArgumentList syntax does not.
            return arguments
                .OrderBy(argument => argument.Parameter?.Ordinal ?? method.Parameters.Length)
                .Select(static argument => argument.Value.Syntax)
                .ToImmutableArray();
        }

        private ComplexityArtifacts AnalyzeObjectCreation(
            IObjectCreationOperation objectCreationOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var parts = new List<ComplexityArtifacts>();
            foreach (var argument in objectCreationOperation.Arguments)
                parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

            if (objectCreationOperation.Initializer != null)
                parts.Add(AnalyzeOperation(objectCreationOperation.Initializer, semanticModel, currentMethod));

            if (objectCreationOperation.Constructor != null)
                parts.Add(AnalyzeMethodCall(
                    objectCreationOperation.Constructor,
                    objectCreationOperation,
                    objectCreationOperation.Syntax,
                    semanticModel,
                    currentMethod,
                    GetArgumentSyntaxes(objectCreationOperation.Constructor, objectCreationOperation.Arguments),
                    null));

            return CombineSequence(parts);
        }

        private ComplexityArtifacts AnalyzePropertyReference(
            IPropertyReferenceOperation propertyReferenceOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var parts = new List<ComplexityArtifacts>();
            if (propertyReferenceOperation.Instance != null)
                parts.Add(AnalyzeOperation(propertyReferenceOperation.Instance, semanticModel, currentMethod));

            foreach (var argument in propertyReferenceOperation.Arguments)
                parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

            var getter = propertyReferenceOperation.Property.GetMethod;
            if (getter != null)
                parts.Add(AnalyzeMethodCall(
                    getter,
                    propertyReferenceOperation,
                    propertyReferenceOperation.Syntax,
                    semanticModel,
                    currentMethod,
                    GetArgumentSyntaxes(getter, propertyReferenceOperation.Arguments),
                    propertyReferenceOperation.Instance?.Syntax));

            return CombineSequence(parts);
        }

        private ComplexityArtifacts AnalyzeArrayCreation(
            IArrayCreationOperation arrayCreationOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var dimensionCosts = arrayCreationOperation.DimensionSizes
                .Select(size => AnalyzeOperation(size, semanticModel, currentMethod))
                .ToArray();
            var initializerCost = AnalyzeOperation(arrayCreationOperation.Initializer, semanticModel, currentMethod);
            return CombineSequence(dimensionCosts.Concat(new[] { initializerCost }));
        }

        private ComplexityArtifacts AnalyzeSwitchOperation(
            ISwitchOperation switchOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var conditionCost = AnalyzeOperation(switchOperation.Value, semanticModel, currentMethod);
            var branchCosts = switchOperation.Cases
                .Select(@case =>
                    CombineSequence(@case.Body.Select(statement =>
                        AnalyzeOperation(statement, semanticModel, currentMethod))))
                .ToArray();
            if (branchCosts.Length == 0) return conditionCost;

            return CombineSequence(conditionCost, CombineBranch(branchCosts));
        }

        private ComplexityArtifacts AnalyzeSwitchExpressionOperation(
            ISwitchExpressionOperation switchExpressionOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var valueCost = AnalyzeOperation(switchExpressionOperation.Value, semanticModel, currentMethod);
            var armCosts = switchExpressionOperation.Arms
                .Select(arm => CombineSequence(
                    AnalyzeOperation(arm.Pattern, semanticModel, currentMethod),
                    AnalyzeOperation(arm.Guard, semanticModel, currentMethod),
                    AnalyzeOperation(arm.Value, semanticModel, currentMethod)))
                .ToArray();
            if (armCosts.Length == 0) return valueCost;

            return CombineSequence(valueCost, CombineBranch(armCosts));
        }

        private ComplexityArtifacts AnalyzeTryOperation(
            ITryOperation tryOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var paths = new List<ComplexityArtifacts>
            {
                AnalyzeOperation(tryOperation.Body, semanticModel, currentMethod)
            };
            foreach (var @catch in tryOperation.Catches)
                paths.Add(AnalyzeOperation(@catch.Handler, semanticModel, currentMethod));

            var finallyCost = AnalyzeOperation(tryOperation.Finally, semanticModel, currentMethod);
            return CombineSequence(CombineBranch(paths), finallyCost);
        }

        private ComplexityArtifacts AnalyzeMethodCall(
            IMethodSymbol methodSymbol,
            IOperation? operation,
            SyntaxNode syntax,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            ImmutableArray<SyntaxNode> argumentSyntaxes,
            SyntaxNode? receiverSyntax)
        {
            if (TryGetKnownMethodCost(methodSymbol, out var knownCost))
                return ComplexityArtifacts.FromCost(
                    knownCost,
                    calleeSummaries: new[]
                    {
                        CreateCalleeInfo(
                            methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            knownCost,
                            currentMethod)
                    });

            if (operation != null &&
                SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(methodSymbol, operation))
                return CreateUnknownCalleeArtifacts(
                    methodSymbol,
                    SymbolicComplexityUnknownReason.DynamicDispatch,
                    syntax);

            if (!IsSourceMethod(methodSymbol))
                return CreateUnknownCalleeArtifacts(
                    methodSymbol,
                    SymbolicComplexityUnknownReason.ExternalCallee,
                    syntax);

            if (!TryResolveSourceMethod(methodSymbol, out var declaration, out var bodyNode, out var sourceModel))
                return CreateUnknownCalleeArtifacts(
                    methodSymbol,
                    SymbolicComplexityUnknownReason.UnknownCallee,
                    syntax);

            var calleeSummary = AnalyzeMethod(methodSymbol, declaration, bodyNode, sourceModel);
            var substitutionResult = SubstituteCalleeCost(
                calleeSummary.Cost,
                methodSymbol,
                argumentSyntaxes,
                receiverSyntax,
                semanticModel,
                currentMethod);
            var calleeInfo = CreateCalleeInfo(
                methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                substitutionResult.Cost,
                currentMethod);
            var drivers = new List<SymbolicComplexityDriverInfo>(substitutionResult.Drivers.Count + 1);
            drivers.AddRange(substitutionResult.Drivers);
            if (!substitutionResult.Cost.IsConstant)
                drivers.Add(CreateDriver(
                    "Call",
                    "call to " + calleeInfo.MethodDisplayName + " contributes " + calleeInfo.ComplexityText,
                    syntax,
                    syntax.SyntaxTree,
                    _cancellationToken));

            return ComplexityArtifacts.FromCost(
                substitutionResult.Cost,
                drivers.Concat(calleeSummary.Drivers),
                substitutionResult.UnknownReasons.Concat(calleeSummary.UnknownReasons),
                new[] { calleeInfo }.Concat(calleeSummary.CalleeSummaries));
        }

        private ComplexityArtifacts CreateUnknownCalleeArtifacts(
            IMethodSymbol methodSymbol,
            SymbolicComplexityUnknownReason reason,
            SyntaxNode syntax)
        {
            return ComplexityArtifacts.Unknown(
                reason,
                syntax,
                syntax.SyntaxTree,
                _cancellationToken,
                calleeSummaries: new[]
                {
                    new SymbolicComplexityCalleeInfo(
                        methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        "Unknown",
                        SymbolicComplexityKind.Unknown,
                        true,
                        reason)
                });
        }

        private static bool IsSourceMethod(IMethodSymbol methodSymbol)
        {
            return SymbolicMethodSourceResolver.IsBackedBySource(methodSymbol);
        }

        private bool TryResolveSourceMethod(
            IMethodSymbol methodSymbol,
            out SyntaxNode declaration,
            out SyntaxNode bodyNode,
            out SemanticModel semanticModel)
        {
            if (SymbolicMethodSourceResolver.TryResolve(
                    _compilation,
                    methodSymbol,
                    static _ => true,
                    false,
                    _cancellationToken,
                    out declaration,
                    out var body,
                    out semanticModel) &&
                body != null)
            {
                bodyNode = body;
                return true;
            }

            bodyNode = null!;
            return false;
        }

        private static bool TryGetKnownMethodCost(
            IMethodSymbol methodSymbol,
            out SymbolicCostExpression cost)
        {
            cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
            if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
                methodSymbol.AssociatedSymbol is IPropertySymbol property)
            {
                if (property.IsIndexer &&
                    methodSymbol.Parameters.Length <= 1)
                {
                    cost = SymbolicCostExpression.Constant();
                    return true;
                }

                if ((string.Equals(property.Name, "Length", StringComparison.Ordinal) ||
                     string.Equals(property.Name, "Count", StringComparison.Ordinal)) &&
                    IsKnownSizedType(property.ContainingType))
                {
                    cost = SymbolicCostExpression.Constant();
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownSizedType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null) return false;

            if (typeSymbol is IArrayTypeSymbol) return true;

            if (typeSymbol.SpecialType == SpecialType.System_String) return true;

            var originalDefinition = typeSymbol.OriginalDefinition;
            var containingNamespace = originalDefinition.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var metadataName = originalDefinition.MetadataName;
            return (string.Equals(containingNamespace, "System", StringComparison.Ordinal) &&
                    (string.Equals(metadataName, "Span`1", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "ReadOnlySpan`1", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "Memory`1", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "ReadOnlyMemory`1", StringComparison.Ordinal))) ||
                   (string.Equals(containingNamespace, "System.Collections.Generic", StringComparison.Ordinal) &&
                    (string.Equals(metadataName, "List`1", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "Dictionary`2", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "ICollection`1", StringComparison.Ordinal) ||
                     string.Equals(metadataName, "IReadOnlyCollection`1", StringComparison.Ordinal))) ||
                   (string.Equals(containingNamespace, "System.Collections.Immutable", StringComparison.Ordinal) &&
                    string.Equals(metadataName, "ImmutableArray`1", StringComparison.Ordinal));
        }

        private SubstitutionResult SubstituteCalleeCost(
            SymbolicCostExpression cost,
            IMethodSymbol callee,
            ImmutableArray<SyntaxNode> argumentSyntaxes,
            SyntaxNode? receiverSyntax,
            SemanticModel callerSemanticModel,
            IMethodSymbol callerMethod)
        {
            if (cost.IsUnknown || cost.IsRecursiveUnknown)
                return new SubstitutionResult(cost, Array.Empty<SymbolicComplexityDriverInfo>(),
                    Array.Empty<SymbolicComplexityUnknownReason>());

            SymbolicCostExpression? ResolveFactor(string key)
            {
                if (TryParseParameterKey(key, out var parameterIndex, out var projection))
                {
                    if (parameterIndex < 0 || parameterIndex >= argumentSyntaxes.Length)
                        return SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

                    return TryCreateCostFromExpression(
                        argumentSyntaxes[parameterIndex] as ExpressionSyntax,
                        callerSemanticModel,
                        callerMethod,
                        projection,
                        true,
                        out var expressionCost)
                        ? expressionCost
                        : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
                }

                if (string.Equals(key, "$this.length", StringComparison.Ordinal))
                    return TryCreateCostFromExpression(
                        receiverSyntax as ExpressionSyntax,
                        callerSemanticModel,
                        callerMethod,
                        CostProjection.LengthOrCount,
                        false,
                        out var receiverCost)
                        ? receiverCost
                        : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

                if (string.Equals(key, "$this", StringComparison.Ordinal))
                    return TryCreateCostFromExpression(
                        receiverSyntax as ExpressionSyntax,
                        callerSemanticModel,
                        callerMethod,
                        CostProjection.Value,
                        true,
                        out var receiverCost)
                        ? receiverCost
                        : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

                return null;
            }

            var substituted = cost.Substitute(ResolveFactor);
            var reasons = substituted.IsUnknown
                ? new[]
                {
                    substituted.UnknownReason == SymbolicComplexityUnknownReason.None
                        ? SymbolicComplexityUnknownReason.UnknownCallee
                        : substituted.UnknownReason
                }
                : Array.Empty<SymbolicComplexityUnknownReason>();
            return new SubstitutionResult(substituted, Array.Empty<SymbolicComplexityDriverInfo>(), reasons);
        }

        private static bool TryParseParameterKey(
            string key,
            out int parameterIndex,
            out CostProjection projection)
        {
            parameterIndex = -1;
            projection = CostProjection.Value;
            if (!key.StartsWith("$p", StringComparison.Ordinal)) return false;

            var suffixStart = key.IndexOf(':');
            if (suffixStart < 0) return false;

            if (!int.TryParse(key.Substring(2, suffixStart - 2), NumberStyles.None, CultureInfo.InvariantCulture,
                    out parameterIndex)) return false;

            var suffix = key.Substring(suffixStart + 1);
            projection = string.Equals(suffix, "length", StringComparison.Ordinal)
                ? CostProjection.LengthOrCount
                : CostProjection.Value;
            return true;
        }

        private static ComplexityArtifacts CombineSequence(IEnumerable<ComplexityArtifacts> parts)
        {
            return CombineInternal(parts, false);
        }

        private static ComplexityArtifacts CombineSequence(params ComplexityArtifacts[] parts)
        {
            return CombineInternal(parts, false);
        }

        private static ComplexityArtifacts CombineBranch(IEnumerable<ComplexityArtifacts> parts)
        {
            return CombineInternal(parts, true);
        }

        private static ComplexityArtifacts CombineBranch(params ComplexityArtifacts[] parts)
        {
            return CombineInternal(parts, true);
        }

        private static ComplexityArtifacts CombineInternal(IEnumerable<ComplexityArtifacts> parts, bool useBranchMax)
        {
            var costExpressions = new List<SymbolicCostExpression>();
            var drivers = new List<SymbolicComplexityDriverInfo>();
            var reasons = new List<SymbolicComplexityUnknownReason>();
            var callees = new List<SymbolicComplexityCalleeInfo>();
            foreach (var part in parts.Where(static part => part != null))
            {
                costExpressions.Add(part.Cost);
                drivers.AddRange(part.Drivers);
                reasons.AddRange(part.UnknownReasons);
                callees.AddRange(part.CalleeSummaries);
            }

            var combinedCost = SymbolicCostExpression.Max(costExpressions);
            if (combinedCost.IsUnknown && combinedCost.UnknownReason != SymbolicComplexityUnknownReason.None)
                reasons.Add(combinedCost.UnknownReason);

            return ComplexityArtifacts.FromCost(combinedCost, drivers, reasons, callees);
        }

        private static ComplexityArtifacts Multiply(SymbolicCostExpression multiplier, ComplexityArtifacts body)
        {
            var cost = SymbolicCostExpression.Multiply(multiplier, body.Cost);
            var reasons = new List<SymbolicComplexityUnknownReason>(body.UnknownReasons);
            if (cost.IsUnknown && cost.UnknownReason != SymbolicComplexityUnknownReason.None)
                reasons.Add(cost.UnknownReason);

            return ComplexityArtifacts.FromCost(cost, body.Drivers, reasons, body.CalleeSummaries);
        }

        private static MethodAnalysisSummary CreateSummary(
            SymbolicCostExpression cost,
            IEnumerable<SymbolicComplexityDriverInfo> drivers,
            IEnumerable<SymbolicComplexityUnknownReason> reasons,
            IEnumerable<SymbolicComplexityCalleeInfo> callees)
        {
            return new MethodAnalysisSummary(
                cost,
                drivers.ToImmutableArray(),
                reasons.ToImmutableArray(),
                callees.ToImmutableArray());
        }

        private static SymbolicComplexityDriverInfo CreateDriver(
            string kind,
            string description,
            SyntaxNode node,
            SyntaxTree syntaxTree,
            CancellationToken cancellationToken)
        {
            var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
                syntaxTree,
                node.SpanStart,
                cancellationToken,
                true);
            return new SymbolicComplexityDriverInfo(
                kind,
                description,
                node.SpanStart,
                node.Span.Length,
                lineColumn.Line,
                lineColumn.Column);
        }

        private static SymbolicComplexityCalleeInfo CreateCalleeInfo(
            string methodDisplayName,
            SymbolicCostExpression cost,
            IMethodSymbol contextMethod)
        {
            return new SymbolicComplexityCalleeInfo(
                methodDisplayName,
                cost.ToBigOText(contextMethod),
                cost.ToPublicKind(),
                cost.IsConservative,
                cost.UnknownReason);
        }

        private bool TryGetForLoopBound(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out LoopBoundInfo bound)
        {
            bound = default;
            if (!TryGetForLoopVariable(forStatement, semanticModel, out var loopSymbol, out var initializerExpression))
                return false;

            if (!TryGetIntegralConstant(initializerExpression, semanticModel, out _)) return false;

            if (forStatement.Condition is not BinaryExpressionSyntax condition ||
                !TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction,
                    out var boundCost, out var boundExpressionText, out var dependentSymbols))
                return false;

            if (!TryParseForLoopStep(forStatement, loopSymbol, semanticModel, out var stepDirection) ||
                stepDirection != direction)
                return false;

            if (dependentSymbols.Any(symbol =>
                    IsSymbolMutatedInStatement(symbol, forStatement.Statement, semanticModel)) ||
                IsSymbolMutatedInStatement(loopSymbol, forStatement.Statement, semanticModel))
                return false;

            bound = new LoopBoundInfo(boundCost, boundExpressionText);
            return true;
        }

        private bool TryGetWhileLikeBound(
            ExpressionSyntax conditionExpression,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out LoopBoundInfo bound)
        {
            bound = default;
            if (conditionExpression is not BinaryExpressionSyntax condition) return false;

            if (!TryGetLoopConditionVariable(condition, semanticModel, out var loopSymbol)) return false;

            if (!TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction,
                    out var boundCost, out var boundExpressionText, out var dependentSymbols)) return false;

            var updates = GetRecognizedLoopUpdates(loopBody, loopSymbol, semanticModel);
            if (updates.Count != 1 || updates[0] != direction) return false;

            if (dependentSymbols.Any(symbol => IsSymbolMutatedInStatement(symbol, loopBody, semanticModel)) ||
                !IsSymbolMutatedInStatement(loopSymbol, loopBody, semanticModel, true))
                return false;

            bound = new LoopBoundInfo(boundCost, boundExpressionText);
            return true;
        }

        private bool TryGetForeachBound(
            SyntaxNode collectionSyntaxNode,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out LoopBoundInfo bound)
        {
            if (collectionSyntaxNode is not ExpressionSyntax collectionExpression ||
                !TryCreateCostFromExpression(
                    collectionExpression,
                    semanticModel,
                    currentMethod,
                    CostProjection.LengthOrCount,
                    false,
                    out var cost))
            {
                bound = default;
                return false;
            }

            bound = new LoopBoundInfo(cost, collectionExpression.ToString());
            return true;
        }

        private bool TryGetForLoopVariable(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            out ISymbol loopSymbol,
            out ExpressionSyntax initializerExpression)
        {
            if (forStatement.Declaration is { Variables.Count: 1 } declaration &&
                declaration.Variables[0].Initializer != null &&
                semanticModel.GetDeclaredSymbol(declaration.Variables[0], _cancellationToken) is ISymbol declaredSymbol)
            {
                loopSymbol = declaredSymbol;
                initializerExpression = declaration.Variables[0].Initializer!.Value;
                return true;
            }

            if (forStatement.Initializers.Count == 1 &&
                forStatement.Initializers[0] is AssignmentExpressionSyntax assignment &&
                semanticModel.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol is { } assignedSymbol &&
                assignedSymbol is ILocalSymbol or IParameterSymbol)
            {
                loopSymbol = assignedSymbol;
                initializerExpression = assignment.Right;
                return true;
            }

            loopSymbol = null!;
            initializerExpression = null!;
            return false;
        }

        private bool TryGetLoopConditionVariable(
            BinaryExpressionSyntax condition,
            SemanticModel semanticModel,
            out ISymbol symbol)
        {
            symbol = semanticModel.GetSymbolInfo(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Left),
                _cancellationToken).Symbol!;
            if (symbol is ILocalSymbol or IParameterSymbol) return true;

            symbol = semanticModel.GetSymbolInfo(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Right),
                _cancellationToken).Symbol!;
            return symbol is ILocalSymbol or IParameterSymbol;
        }

        private bool TryParseLoopCondition(
            BinaryExpressionSyntax condition,
            ISymbol loopSymbol,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out StepDirection direction,
            out SymbolicCostExpression boundCost,
            out string boundDescription,
            out ImmutableArray<ISymbol> dependentSymbols)
        {
            direction = StepDirection.Up;
            boundCost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape);
            boundDescription = string.Empty;
            dependentSymbols = ImmutableArray<ISymbol>.Empty;

            var left = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Left);
            var right = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Right);
            var leftSymbol = semanticModel.GetSymbolInfo(left, _cancellationToken).Symbol;
            var rightSymbol = semanticModel.GetSymbolInfo(right, _cancellationToken).Symbol;

            ExpressionSyntax? boundExpression = null;
            if (SymbolEquals(leftSymbol, loopSymbol))
            {
                direction = condition.IsKind(SyntaxKind.LessThanExpression) ||
                            condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                    ? StepDirection.Up
                    : condition.IsKind(SyntaxKind.GreaterThanExpression) ||
                      condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                        ? StepDirection.Down
                        : StepDirection.None;
                boundExpression = right;
            }
            else if (SymbolEquals(rightSymbol, loopSymbol))
            {
                direction = condition.IsKind(SyntaxKind.GreaterThanExpression) ||
                            condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                    ? StepDirection.Up
                    : condition.IsKind(SyntaxKind.LessThanExpression) ||
                      condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                        ? StepDirection.Down
                        : StepDirection.None;
                boundExpression = left;
            }

            if (direction == StepDirection.None ||
                boundExpression == null ||
                !TryCreateCostFromExpression(boundExpression, semanticModel, currentMethod, CostProjection.Value, true,
                    out boundCost))
                return false;

            boundDescription = boundExpression.ToString();
            dependentSymbols = GetDependentSymbols(boundExpression, semanticModel);
            return true;
        }

        private bool TryParseForLoopStep(
            ForStatementSyntax forStatement,
            ISymbol loopSymbol,
            SemanticModel semanticModel,
            out StepDirection direction)
        {
            direction = StepDirection.None;
            if (forStatement.Incrementors.Count != 1) return false;

            return TryParseLoopStep(forStatement.Incrementors[0], loopSymbol, semanticModel, out direction);
        }

        private bool TryParseLoopStep(
            ExpressionSyntax expression,
            ISymbol loopSymbol,
            SemanticModel semanticModel,
            out StepDirection direction)
        {
            direction = StepDirection.None;
            expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
            if (CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(expression, out var operand, out var delta) &&
                SymbolEquals(semanticModel.GetSymbolInfo(operand, _cancellationToken).Symbol, loopSymbol))
            {
                direction = delta > 0 ? StepDirection.Up : StepDirection.Down;
                return true;
            }

            switch (expression)
            {
                case AssignmentExpressionSyntax assignment
                    when SymbolEquals(semanticModel.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol,
                        loopSymbol):
                    if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                        TryGetIntegralConstant(assignment.Right, semanticModel, out var addValue) &&
                        addValue > 0)
                    {
                        direction = StepDirection.Up;
                        return true;
                    }

                    if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
                        TryGetIntegralConstant(assignment.Right, semanticModel, out var subtractValue) &&
                        subtractValue > 0)
                    {
                        direction = StepDirection.Down;
                        return true;
                    }

                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                        assignment.Right is BinaryExpressionSyntax binaryExpression)
                    {
                        if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                            IsReferenceToSymbol(binaryExpression.Left, loopSymbol, semanticModel) &&
                            TryGetIntegralConstant(binaryExpression.Right, semanticModel, out var rightAdd) &&
                            rightAdd > 0)
                        {
                            direction = StepDirection.Up;
                            return true;
                        }

                        if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                            TryGetIntegralConstant(binaryExpression.Left, semanticModel, out var leftAdd) &&
                            leftAdd > 0 &&
                            IsReferenceToSymbol(binaryExpression.Right, loopSymbol, semanticModel))
                        {
                            direction = StepDirection.Up;
                            return true;
                        }

                        if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                            IsReferenceToSymbol(binaryExpression.Left, loopSymbol, semanticModel) &&
                            TryGetIntegralConstant(binaryExpression.Right, semanticModel, out var rightSubtract) &&
                            rightSubtract > 0)
                        {
                            direction = StepDirection.Down;
                            return true;
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }

        private bool IsSymbolMutatedInStatement(
            ISymbol symbol,
            StatementSyntax statement,
            SemanticModel semanticModel,
            bool allowRecognizedLoopUpdates = false)
        {
            var sawMutation = false;
            foreach (var node in statement.DescendantNodesAndSelf(static candidate =>
                         !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            {
                if (!SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression) ||
                    !AssignmentTargetReferencesSymbol(mutatedExpression, symbol, semanticModel))
                    continue;

                if (allowRecognizedLoopUpdates &&
                    node is ExpressionSyntax mutationExpression &&
                    TryParseLoopStep(mutationExpression, symbol, semanticModel, out _))
                {
                    sawMutation = true;
                    continue;
                }

                return true;
            }

            return allowRecognizedLoopUpdates ? sawMutation : false;
        }

        private bool AssignmentTargetReferencesSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel)
        {
            var operation = semanticModel.GetOperation(expression, _cancellationToken);
            return operation != null
                ? AssignmentTargetReferencesSymbol(operation, symbol)
                : expression is TupleExpressionSyntax tuple &&
                  tuple.Arguments.Any(argument =>
                      AssignmentTargetReferencesSymbol(argument.Expression, symbol, semanticModel));
        }

        private static bool AssignmentTargetReferencesSymbol(IOperation operation, ISymbol symbol)
        {
            switch (operation)
            {
                case ILocalReferenceOperation local:
                    return SymbolEquals(local.Local, symbol);
                case IParameterReferenceOperation parameter:
                    return SymbolEquals(parameter.Parameter, symbol);
                case ITupleOperation tuple:
                    return tuple.Elements.Any(element => AssignmentTargetReferencesSymbol(element, symbol));
                case IDeclarationExpressionOperation declaration:
                    return AssignmentTargetReferencesSymbol(declaration.Expression, symbol);
                case IConversionOperation conversion:
                    return AssignmentTargetReferencesSymbol(conversion.Operand, symbol);
                case IParenthesizedOperation parenthesized:
                    return AssignmentTargetReferencesSymbol(parenthesized.Operand, symbol);
                default:
                    return false;
            }
        }

        private List<StepDirection> GetRecognizedLoopUpdates(
            StatementSyntax loopBody,
            ISymbol loopSymbol,
            SemanticModel semanticModel)
        {
            var updates = new List<StepDirection>();
            foreach (var expression in loopBody.DescendantNodesAndSelf(static candidate =>
                             !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                         .OfType<ExpressionSyntax>())
                if (TryParseLoopStep(expression, loopSymbol, semanticModel, out var direction))
                    updates.Add(direction);

            return updates;
        }

        private ImmutableArray<ISymbol> GetDependentSymbols(
            ExpressionSyntax expression,
            SemanticModel semanticModel)
        {
            var builder = ImmutableArray.CreateBuilder<ISymbol>();
            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                if (semanticModel.GetSymbolInfo(identifier, _cancellationToken).Symbol is ISymbol symbol &&
                    builder.All(existing => !SymbolEquals(existing, symbol)))
                    builder.Add(symbol);

            return builder.ToImmutable();
        }

        private bool TryCreateCostFromExpression(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            CostProjection projection,
            bool allowConstants,
            out SymbolicCostExpression cost)
        {
            cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
            if (expression == null) return false;

            expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);

            if (allowConstants && TryGetIntegralConstant(expression, semanticModel, out _))
            {
                cost = SymbolicCostExpression.Constant();
                return true;
            }

            if (projection == CostProjection.LengthOrCount)
            {
                if (TryCreateLengthOrCountCost(expression, semanticModel, currentMethod, out cost)) return true;
            }
            else if (TryCreateScalarCost(expression, semanticModel, currentMethod, out cost))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression &&
                (binaryExpression.IsKind(SyntaxKind.AddExpression) ||
                 binaryExpression.IsKind(SyntaxKind.SubtractExpression)))
            {
                if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, out _) &&
                    TryCreateCostFromExpression(binaryExpression.Left, semanticModel, currentMethod, projection,
                        allowConstants, out cost))
                    return true;

                if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                    TryGetIntegralConstant(binaryExpression.Left, semanticModel, out _) &&
                    TryCreateCostFromExpression(binaryExpression.Right, semanticModel, currentMethod, projection,
                        allowConstants, out cost))
                    return true;
            }

            return false;
        }

        private bool TryCreateScalarCost(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out SymbolicCostExpression cost)
        {
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
                 string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)) &&
                TryCreateLengthOrCountCost(expression, semanticModel, currentMethod, out cost))
                return true;

            if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is IParameterSymbol parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition,
                    currentMethod.OriginalDefinition))
            {
                cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":value");
                return true;
            }

            if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is ISymbol symbol)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, currentMethod.AssociatedSymbol))
                {
                    cost = SymbolicCostExpression.Variable("$this");
                    return true;
                }

                cost = SymbolicCostExpression.Variable("name:" + symbol.Name);
                return true;
            }

            cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
            return false;
        }

        private bool TryCreateLengthOrCountCost(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod,
            out SymbolicCostExpression cost)
        {
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
                 string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)))
            {
                if (semanticModel.GetSymbolInfo(memberAccess.Expression, _cancellationToken).Symbol is IParameterSymbol
                        parameter &&
                    SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition,
                        currentMethod.OriginalDefinition))
                {
                    cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                    return true;
                }

                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    cost = SymbolicCostExpression.Variable("$this.length");
                    return true;
                }

                if (semanticModel.GetSymbolInfo(memberAccess.Expression, _cancellationToken).Symbol is ISymbol
                    receiverSymbol)
                {
                    cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + "." +
                                                           memberAccess.Name.Identifier.ValueText);
                    return true;
                }
            }

            var expressionType = semanticModel.GetTypeInfo(expression, _cancellationToken).Type;
            if (expressionType != null && IsKnownSizedType(expressionType))
            {
                if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is IParameterSymbol parameter &&
                    SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition,
                        currentMethod.OriginalDefinition))
                {
                    cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                    return true;
                }

                if (expression is ThisExpressionSyntax)
                {
                    cost = SymbolicCostExpression.Variable("$this.length");
                    return true;
                }

                if (semanticModel.GetSymbolInfo(expression, _cancellationToken).Symbol is ISymbol receiverSymbol)
                {
                    cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + ".Length");
                    return true;
                }
            }

            cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
            return false;
        }

        private bool TryGetIntegralConstant(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out long value)
        {
            return SymbolicLoweringValueFacts.TryGetIntegralConstant(
                expression,
                semanticModel,
                _cancellationToken,
                out value);
        }

        private bool TryGetConstantBoolean(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            out bool value)
        {
            if (syntaxNode is ExpressionSyntax expression &&
                semanticModel.GetConstantValue(expression, _cancellationToken) is
                { HasValue: true, Value: bool boolValue })
            {
                value = boolValue;
                return true;
            }

            value = false;
            return false;
        }

        private bool IsReferenceToSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel)
        {
            return SymbolEquals(
                semanticModel.GetSymbolInfo(
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression),
                    _cancellationToken).Symbol,
                symbol);
        }

        private static bool SymbolEquals(ISymbol? left, ISymbol? right)
        {
            return left != null &&
                   right != null &&
                   SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);
        }
    }
}
