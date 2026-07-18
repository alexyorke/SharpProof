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
        return SymbolicMethodLikeQueryDispatcher.Execute(
            source,
            target,
            options,
            SymbolicSourceCompilationKind.Complexity,
            "Complexity source kind is not supported.",
            "Complexity queries support point, position, line, or node targets only.",
            "Node complexity queries require a node target.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(
                node,
                includeAnonymousFunctions: true,
                includeDestructors: true),
            ResolveMethodLikeDeclaration,
            ExecuteAnalysis,
            cancellationToken);
    }

    private static SymbolicComplexityResult ExecuteAnalysis(
        ResolvedComplexityTarget target,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var summary = new AnalysisSession(compilation, cancellationToken).Analyze(target);
        return SymbolicComplexityResultProjector.Project(target, summary, cancellationToken);
    }

    private static ResolvedComplexityTarget ResolveMethodLikeDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var bodyNode = SymbolicMethodSourceResolver.GetBodyNode(declaration);
        if (bodyNode == null)
            throw new ArgumentException("The requested method-like declaration does not have a body.");

        var symbol = SymbolicMethodLikeDeclaration.GetMethodSymbol(declaration, semanticModel, cancellationToken);
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
        private readonly SymbolicComplexityCostModel _costModel;
        private readonly SymbolicComplexityLoopModel _loopModel;

        private readonly Dictionary<IMethodSymbol, MethodAnalysisSummary> _summaryCache =
            new(SymbolEqualityComparer.Default);

        public AnalysisSession(Compilation compilation, CancellationToken cancellationToken)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _cancellationToken = cancellationToken;
            _costModel = new SymbolicComplexityCostModel(cancellationToken);
            _loopModel = new SymbolicComplexityLoopModel(_costModel, cancellationToken);
        }

        public MethodAnalysisSummary Analyze(ResolvedComplexityTarget target)
        {
            return AnalyzeMethod(target.Symbol, target.BodyNode, target.SemanticModel);
        }

        private MethodAnalysisSummary AnalyzeMethod(
            IMethodSymbol methodSymbol,
            SyntaxNode bodyNode,
            SemanticModel semanticModel)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var canonical = methodSymbol.OriginalDefinition;
            if (_summaryCache.TryGetValue(canonical, out var cached)) return cached;

            if (_active.Contains(canonical))
                return SymbolicComplexityAlgebra.CreateSummary(
                    SymbolicCostExpression.RecursiveUnknown(),
                    Array.Empty<SymbolicComplexityDriverInfo>(),
                    new[] { SymbolicComplexityUnknownReason.RecursiveCycle },
                    new[]
                    {
                        SymbolicComplexityAlgebra.CreateCalleeInfo(
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
                var summary = SymbolicComplexityAlgebra.CreateSummary(
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

            return SymbolicComplexityAlgebra.CombineSequence(invocationCosts);
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
                    return SymbolicComplexityAlgebra.CombineSequence(block.Operations.Select(child =>
                        AnalyzeOperation(child, semanticModel, currentMethod)));

                case IVariableDeclarationGroupOperation group:
                    {
                        var parts = new List<ComplexityArtifacts>();
                        foreach (var declaration in group.Declarations)
                            foreach (var declarator in declaration.Declarators)
                                if (declarator.Initializer != null)
                                    parts.Add(AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod));

                        return SymbolicComplexityAlgebra.CombineSequence(parts);
                    }

                case IVariableDeclaratorOperation declarator:
                    return declarator.Initializer == null
                        ? ComplexityArtifacts.Constant
                        : AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod);

                case IExpressionStatementOperation expressionStatement:
                    return AnalyzeOperation(expressionStatement.Operation, semanticModel, currentMethod);

                case IReturnOperation returnOperation:
                    return returnOperation.ReturnedValue != null
                        ? SymbolicComplexityAlgebra.CombineSequence(
                            new[]
                            {
                                AnalyzeOperation(returnOperation.ReturnedValue, semanticModel, currentMethod)
                            }.Concat(returnOperation.ChildOperations
                                .Where(child => !ReferenceEquals(child, returnOperation.ReturnedValue))
                                .Select(child => AnalyzeOperation(child, semanticModel, currentMethod))))
                        : SymbolicComplexityAlgebra.CombineSequence(returnOperation.ChildOperations.Select(child =>
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
                    return SymbolicComplexityAlgebra.CombineSequence(operation.ChildOperations.Select(child =>
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
                return SymbolicComplexityAlgebra.CombineSequence(
                    conditionCost,
                    constantValue
                        ? AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod)
                        : AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod));

            return SymbolicComplexityAlgebra.CombineSequence(
                conditionCost,
                SymbolicComplexityAlgebra.CombineBranch(
                    AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod),
                    AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod)));
        }

        private ComplexityArtifacts AnalyzeForLoop(
            IForLoopOperation forLoopOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var beforeCost =
                SymbolicComplexityAlgebra.CombineSequence(
                    forLoopOperation.Before.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
            var conditionCost = AnalyzeOperation(forLoopOperation.Condition, semanticModel, currentMethod);
            var bottomCost =
                SymbolicComplexityAlgebra.CombineSequence(
                    forLoopOperation.AtLoopBottom.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
            var bodyCost = AnalyzeOperation(forLoopOperation.Body, semanticModel, currentMethod);

            if (forLoopOperation.Syntax is not ForStatementSyntax forStatement ||
                !_loopModel.TryGetForLoopBound(forStatement, semanticModel, currentMethod, out var bound))
                return SymbolicComplexityAlgebra.CombineSequence(
                    beforeCost,
                    ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                        forLoopOperation.Syntax,
                        forLoopOperation.Syntax.SyntaxTree,
                        _cancellationToken,
                        conditionCost,
                        bottomCost,
                        bodyCost));

            var perIteration = SymbolicComplexityAlgebra.CombineSequence(conditionCost, bottomCost, bodyCost);
            var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, perIteration);
            multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
                "ForLoop",
                "for-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                forStatement,
                forStatement.SyntaxTree,
                _cancellationToken));
            return SymbolicComplexityAlgebra.CombineSequence(beforeCost, multiplied);
        }

        private ComplexityArtifacts AnalyzeForEachLoop(
            IForEachLoopOperation forEachLoopOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var collectionCost = AnalyzeOperation(forEachLoopOperation.Collection, semanticModel, currentMethod);
            var bodyCost = AnalyzeOperation(forEachLoopOperation.Body, semanticModel, currentMethod);

            if (forEachLoopOperation.Syntax is not CommonForEachStatementSyntax foreachSyntax ||
                !_loopModel.TryGetForeachBound(forEachLoopOperation.Collection.Syntax, semanticModel, currentMethod,
                    out var bound))
                return SymbolicComplexityAlgebra.CombineSequence(
                    collectionCost,
                    ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                        forEachLoopOperation.Syntax,
                        forEachLoopOperation.Syntax.SyntaxTree,
                        _cancellationToken,
                        bodyCost));

            var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, bodyCost);
            multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
                "ForeachLoop",
                "foreach bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                foreachSyntax,
                foreachSyntax.SyntaxTree,
                _cancellationToken));
            return SymbolicComplexityAlgebra.CombineSequence(collectionCost, multiplied);
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
                !_loopModel.TryGetWhileLikeBound(
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

            var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, SymbolicComplexityAlgebra.CombineSequence(conditionCost, bodyCost));
            multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
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
            return SymbolicComplexityAlgebra.CombineSequence(receiverAndArguments);
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

            return SymbolicComplexityAlgebra.CombineSequence(parts);
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

            return SymbolicComplexityAlgebra.CombineSequence(parts);
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
            return SymbolicComplexityAlgebra.CombineSequence(dimensionCosts.Concat(new[] { initializerCost }));
        }

        private ComplexityArtifacts AnalyzeSwitchOperation(
            ISwitchOperation switchOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var conditionCost = AnalyzeOperation(switchOperation.Value, semanticModel, currentMethod);
            var branchCosts = switchOperation.Cases
                .Select(@case =>
                    SymbolicComplexityAlgebra.CombineSequence(@case.Body.Select(statement =>
                        AnalyzeOperation(statement, semanticModel, currentMethod))))
                .ToArray();
            if (branchCosts.Length == 0) return conditionCost;

            return SymbolicComplexityAlgebra.CombineSequence(conditionCost, SymbolicComplexityAlgebra.CombineBranch(branchCosts));
        }

        private ComplexityArtifacts AnalyzeSwitchExpressionOperation(
            ISwitchExpressionOperation switchExpressionOperation,
            SemanticModel semanticModel,
            IMethodSymbol currentMethod)
        {
            var valueCost = AnalyzeOperation(switchExpressionOperation.Value, semanticModel, currentMethod);
            var armCosts = switchExpressionOperation.Arms
                .Select(arm => SymbolicComplexityAlgebra.CombineSequence(
                    AnalyzeOperation(arm.Pattern, semanticModel, currentMethod),
                    AnalyzeOperation(arm.Guard, semanticModel, currentMethod),
                    AnalyzeOperation(arm.Value, semanticModel, currentMethod)))
                .ToArray();
            if (armCosts.Length == 0) return valueCost;

            return SymbolicComplexityAlgebra.CombineSequence(valueCost, SymbolicComplexityAlgebra.CombineBranch(armCosts));
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
            return SymbolicComplexityAlgebra.CombineSequence(SymbolicComplexityAlgebra.CombineBranch(paths), finallyCost);
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
                        SymbolicComplexityAlgebra.CreateCalleeInfo(
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

            var calleeSummary = AnalyzeMethod(methodSymbol, bodyNode, sourceModel);
            var substitutionResult = SubstituteCalleeCost(
                calleeSummary.Cost,
                argumentSyntaxes,
                receiverSyntax,
                semanticModel,
                currentMethod);
            var calleeInfo = SymbolicComplexityAlgebra.CreateCalleeInfo(
                methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                substitutionResult.Cost,
                currentMethod);
            var drivers = new List<SymbolicComplexityDriverInfo>(substitutionResult.Drivers.Count + 1);
            drivers.AddRange(substitutionResult.Drivers);
            if (!substitutionResult.Cost.IsConstant)
                drivers.Add(SymbolicComplexityAlgebra.CreateDriver(
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
                    SymbolicComplexityCostModel.IsKnownSizedType(property.ContainingType))
                {
                    cost = SymbolicCostExpression.Constant();
                    return true;
                }
            }

            return false;
        }

        private SubstitutionResult SubstituteCalleeCost(
            SymbolicCostExpression cost,
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

                    return _costModel.TryCreate(
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
                    return _costModel.TryCreate(
                        receiverSyntax as ExpressionSyntax,
                        callerSemanticModel,
                        callerMethod,
                        CostProjection.LengthOrCount,
                        false,
                        out var receiverCost)
                        ? receiverCost
                        : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

                if (string.Equals(key, "$this", StringComparison.Ordinal))
                    return _costModel.TryCreate(
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

    }
}
