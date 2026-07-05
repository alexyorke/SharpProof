using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.Symbolic
{
    internal sealed class SymbolicComplexityService
    {
        public SymbolicComplexityResult Query(
            SymbolicSourceInput source,
            SymbolicQueryTarget target,
            SymbolicQueryOptions options,
            CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            options ??= SymbolicQueryOptions.Default;

            switch (source.Kind)
            {
                case SymbolicSourceInputKind.File:
                    return QueryFile(source.FilePath!, target, options.References, cancellationToken);
                case SymbolicSourceInputKind.Text:
                    return QuerySource(
                        source.SourceText!,
                        source.FilePath ?? SymbolicSourceInput.DefaultFilePath,
                        target,
                        options.References,
                        cancellationToken);
                case SymbolicSourceInputKind.SyntaxTree:
                    return QuerySyntaxTree(
                        source.SyntaxTree!,
                        source.Compilation!,
                        target,
                        cancellationToken);
                case SymbolicSourceInputKind.Node:
                    return QueryNode(source.Node!, source.SemanticModel!, target, cancellationToken);
                default:
                    throw new NotSupportedException("Complexity source kind is not supported.");
            }
        }

        private SymbolicComplexityResult QueryFile(
            string filePath,
            SymbolicQueryTarget target,
            IEnumerable<MetadataReference>? references,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySource(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                target,
                references,
                cancellationToken);
        }

        private SymbolicComplexityResult QuerySource(
            string sourceText,
            string filePath,
            SymbolicQueryTarget target,
            IEnumerable<MetadataReference>? references,
            CancellationToken cancellationToken)
        {
            var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
                sourceText,
                filePath,
                "SharpProof.Symbolic.Complexity.cs",
                "SharpProof.Symbolic.Complexity",
                references,
                cancellationToken);
            return QuerySyntaxTree(syntaxTree, compilation, target, cancellationToken);
        }

        private SymbolicComplexityResult QuerySyntaxTree(
            SyntaxTree syntaxTree,
            Compilation compilation,
            SymbolicQueryTarget target,
            CancellationToken cancellationToken)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var resolved = ResolveTarget(syntaxTree, semanticModel, target, cancellationToken);
            var session = new AnalysisSession(compilation, cancellationToken);
            var summary = session.Analyze(resolved);
            return CreateResult(resolved, summary, cancellationToken);
        }

        private SymbolicComplexityResult QueryNode(
            SyntaxNode node,
            SemanticModel semanticModel,
            SymbolicQueryTarget target,
            CancellationToken cancellationToken)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (target.Kind != SymbolicQueryTargetKind.Node)
            {
                throw new NotSupportedException("Node complexity queries require a node target.");
            }

            var resolved = ResolveNodeTarget(node, semanticModel, cancellationToken);
            var session = new AnalysisSession(semanticModel.Compilation, cancellationToken);
            var summary = session.Analyze(resolved);
            return CreateResult(resolved, summary, cancellationToken);
        }

        private static ResolvedComplexityTarget ResolveTarget(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            SymbolicQueryTarget target,
            CancellationToken cancellationToken)
        {
            var root = syntaxTree.GetRoot(cancellationToken);
            switch (target.Kind)
            {
                case SymbolicQueryTargetKind.Point:
                {
                    var position = SymbolicSourceLocation.GetPosition(
                        syntaxTree,
                        target.LineNumber!.Value,
                        target.ColumnNumber ?? 1,
                        cancellationToken);
                    return ResolvePositionTarget(root, syntaxTree, semanticModel, position, cancellationToken);
                }

                case SymbolicQueryTargetKind.Position:
                    return ResolvePositionTarget(
                        root,
                        syntaxTree,
                        semanticModel,
                        target.PositionOffset!.Value,
                        cancellationToken);

                case SymbolicQueryTargetKind.Line:
                    return ResolveLineTarget(
                        root,
                        syntaxTree,
                        semanticModel,
                        target.LineNumber!.Value,
                        cancellationToken);

                default:
                    throw new NotSupportedException("Complexity queries support point, position, line, or node targets only.");
            }
        }

        private static ResolvedComplexityTarget ResolvePositionTarget(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            int position,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            if (position < 0 || position > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "--position must be within the source text span.");
            }

            var token = root.FindToken(position);
            var node = token.Parent;
            if (node == null)
            {
                throw new ArgumentException("Could not resolve a method-like body at the requested position.", nameof(position));
            }

            return ResolveContainingMethodLike(node, semanticModel, cancellationToken);
        }

        private static ResolvedComplexityTarget ResolveLineTarget(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            int line,
            CancellationToken cancellationToken)
        {
            var lineSpan = SymbolicSourceLocation.GetLineSpan(syntaxTree, line, cancellationToken);
            var methodLike = root
                .DescendantNodes(static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .Where(static candidate => IsMethodLikeDeclaration(candidate))
                .Where(candidate => candidate.Span.OverlapsWith(lineSpan))
                .OrderBy(candidate => candidate.Span.Length)
                .ThenBy(candidate => candidate.SpanStart)
                .FirstOrDefault();

            if (methodLike == null)
            {
                var token = root.FindToken(lineSpan.Start);
                if (token.Parent == null)
                {
                    throw new ArgumentException("Could not resolve a method-like body on the requested line.", nameof(line));
                }

                return ResolveContainingMethodLike(token.Parent, semanticModel, cancellationToken);
            }

            return ResolveMethodLikeDeclaration(methodLike, semanticModel, cancellationToken);
        }

        private static ResolvedComplexityTarget ResolveNodeTarget(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (IsMethodLikeDeclaration(node))
            {
                return ResolveMethodLikeDeclaration(node, semanticModel, cancellationToken);
            }

            return ResolveContainingMethodLike(node, semanticModel, cancellationToken);
        }

        private static ResolvedComplexityTarget ResolveContainingMethodLike(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                if (!IsMethodLikeDeclaration(ancestor))
                {
                    continue;
                }

                return ResolveMethodLikeDeclaration(ancestor, semanticModel, cancellationToken);
            }

            throw new ArgumentException("Could not resolve a containing method-like body.");
        }

        private static ResolvedComplexityTarget ResolveMethodLikeDeclaration(
            SyntaxNode declaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var bodyNode = GetBodyNode(declaration);
            if (bodyNode == null)
            {
                throw new ArgumentException("The requested method-like declaration does not have a body.");
            }

            var symbol = GetMethodLikeSymbol(declaration, semanticModel, cancellationToken);
            if (symbol == null)
            {
                throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");
            }

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

        private static IReadOnlyList<SymbolicComplexityDriverInfo> DistinctDrivers(IEnumerable<SymbolicComplexityDriverInfo> drivers)
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
                if (seen.Add(key))
                {
                    distinct.Add(driver);
                }
            }

            return distinct;
        }

        private static IReadOnlyList<SymbolicComplexityUnknownReason> DistinctUnknownReasons(IEnumerable<SymbolicComplexityUnknownReason> reasons)
        {
            return reasons
                .Where(static reason => reason != SymbolicComplexityUnknownReason.None)
                .Distinct()
                .ToArray();
        }

        private static IReadOnlyList<SymbolicComplexityCalleeInfo> DistinctCalleeSummaries(IEnumerable<SymbolicComplexityCalleeInfo> callees)
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
                if (seen.Add(key))
                {
                    distinct.Add(callee);
                }
            }

            return distinct;
        }

        private static bool IsMethodLikeDeclaration(SyntaxNode node)
        {
            return node is BaseMethodDeclarationSyntax ||
                node is AccessorDeclarationSyntax ||
                node is LocalFunctionStatementSyntax ||
                node is AnonymousFunctionExpressionSyntax;
        }

        private static SyntaxNode? GetBodyNode(SyntaxNode declaration)
        {
            switch (declaration)
            {
                case BaseMethodDeclarationSyntax method:
                    return (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression;
                case AccessorDeclarationSyntax accessor:
                    return (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression;
                case LocalFunctionStatementSyntax localFunction:
                    return (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression;
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return anonymousFunction.Body;
                default:
                    return null;
            }
        }

        private static IMethodSymbol? GetMethodLikeSymbol(
            SyntaxNode declaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            switch (declaration)
            {
                case MethodDeclarationSyntax method:
                    return semanticModel.GetDeclaredSymbol(method, cancellationToken);
                case ConstructorDeclarationSyntax constructor:
                    return semanticModel.GetDeclaredSymbol(constructor, cancellationToken);
                case DestructorDeclarationSyntax destructor:
                    return semanticModel.GetDeclaredSymbol(destructor, cancellationToken);
                case OperatorDeclarationSyntax operatorDeclaration:
                    return semanticModel.GetDeclaredSymbol(operatorDeclaration, cancellationToken);
                case ConversionOperatorDeclarationSyntax conversionOperator:
                    return semanticModel.GetDeclaredSymbol(conversionOperator, cancellationToken);
                case AccessorDeclarationSyntax accessor:
                    return semanticModel.GetDeclaredSymbol(accessor, cancellationToken);
                case LocalFunctionStatementSyntax localFunction:
                    return semanticModel.GetDeclaredSymbol(localFunction, cancellationToken);
                case AnonymousFunctionExpressionSyntax anonymousFunction:
                    return semanticModel.GetOperation(anonymousFunction, cancellationToken) is IAnonymousFunctionOperation lambda
                        ? lambda.Symbol
                        : null;
                default:
                    return null;
            }
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
                LocalFunctionStatementSyntax => "local_function",
                AnonymousFunctionExpressionSyntax => "anonymous_function",
                _ => declaration.Kind().ToString(),
            };
        }

        private static string GetMethodName(IMethodSymbol symbol, SyntaxNode declaration)
        {
            if (!string.IsNullOrWhiteSpace(symbol.Name))
            {
                return symbol.Name;
            }

            return declaration switch
            {
                AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
                AnonymousFunctionExpressionSyntax => "anonymous_function",
                _ => declaration.Kind().ToString(),
            };
        }

        private sealed class AnalysisSession
        {
            private readonly Compilation _compilation;
            private readonly CancellationToken _cancellationToken;
            private readonly Dictionary<IMethodSymbol, MethodAnalysisSummary> _summaryCache =
                new Dictionary<IMethodSymbol, MethodAnalysisSummary>(SymbolEqualityComparer.Default);
            private readonly HashSet<IMethodSymbol> _active =
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

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
                if (_summaryCache.TryGetValue(canonical, out var cached))
                {
                    return cached;
                }

                if (_active.Contains(canonical))
                {
                    return CreateSummary(
                        SymbolicCostExpression.RecursiveUnknown(),
                        Array.Empty<SymbolicComplexityDriverInfo>(),
                        new[] { SymbolicComplexityUnknownReason.RecursiveCycle },
                        new[]
                        {
                            CreateCalleeInfo(
                                canonical.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                SymbolicCostExpression.RecursiveUnknown(),
                                canonical),
                        });
                }

                _active.Add(canonical);
                try
                {
                    var operation = semanticModel.GetOperation(bodyNode, _cancellationToken);
                    var bodyCost = CombineSequence(
                        AnalyzeOperation(operation, semanticModel, canonical),
                        AnalyzeTopLevelInvocations(bodyNode, semanticModel, canonical),
                        AnalyzeExternalInvocationFallbacks(bodyNode, semanticModel));
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

            private ComplexityArtifacts AnalyzeExternalInvocationFallbacks(
                SyntaxNode bodyNode,
                SemanticModel semanticModel)
            {
                foreach (var invocationSyntax in bodyNode.DescendantNodes(
                             static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                         .OfType<InvocationExpressionSyntax>())
                {
                    var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax, _cancellationToken);
                    var expressionSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax.Expression, _cancellationToken);
                    var targetMethod = invocationSymbolInfo.Symbol as IMethodSymbol ??
                        expressionSymbolInfo.Symbol as IMethodSymbol ??
                        invocationSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault() ??
                        expressionSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                    if (targetMethod == null)
                    {
                        return ComplexityArtifacts.Unknown(
                            SymbolicComplexityUnknownReason.UnknownCallee,
                            invocationSyntax,
                            invocationSyntax.SyntaxTree);
                    }

                    if (TryGetKnownMethodCost(targetMethod, out _))
                    {
                        continue;
                    }

                    if (!IsSourceMethod(targetMethod))
                    {
                        return ComplexityArtifacts.Unknown(
                            SymbolicComplexityUnknownReason.ExternalCallee,
                            invocationSyntax,
                            invocationSyntax.SyntaxTree,
                            calleeSummaries: new[]
                            {
                                new SymbolicComplexityCalleeInfo(
                                    targetMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                    "Unknown",
                                    SymbolicComplexityKind.Unknown,
                                    true,
                                    SymbolicComplexityUnknownReason.ExternalCallee),
                            });
                    }
                }

                return ComplexityArtifacts.Constant;
            }

            private ComplexityArtifacts AnalyzeTopLevelInvocations(
                SyntaxNode bodyNode,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var invocationCosts = new List<ComplexityArtifacts>();
                foreach (var invocationSyntax in bodyNode.DescendantNodes(
                             static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                         .OfType<InvocationExpressionSyntax>())
                {
                    var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax, _cancellationToken);
                    var expressionSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax.Expression, _cancellationToken);
                    var targetMethod = invocationSymbolInfo.Symbol as IMethodSymbol ??
                        expressionSymbolInfo.Symbol as IMethodSymbol ??
                        invocationSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault() ??
                        expressionSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                    if (targetMethod == null)
                    {
                        return ComplexityArtifacts.Unknown(
                            SymbolicComplexityUnknownReason.UnknownCallee,
                            invocationSyntax,
                            invocationSyntax.SyntaxTree);
                    }

                    invocationCosts.Add(AnalyzeMethodCall(
                        targetMethod,
                        invocationSyntax,
                        semanticModel,
                        currentMethod,
                        invocationSyntax.ArgumentList.Arguments.Select(static argument => (SyntaxNode)argument.Expression).ToImmutableArray(),
                        invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess ? memberAccess.Expression : null));
                }

                return CombineSequence(invocationCosts);
            }

            private ComplexityArtifacts AnalyzeOperation(
                IOperation? operation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (operation == null)
                {
                    return ComplexityArtifacts.Constant;
                }

                switch (operation)
                {
                    case IBlockOperation block:
                        return CombineSequence(block.Operations.Select(child => AnalyzeOperation(child, semanticModel, currentMethod)));

                    case IVariableDeclarationGroupOperation group:
                    {
                        var parts = new List<ComplexityArtifacts>();
                        foreach (var declaration in group.Declarations)
                        {
                            foreach (var declarator in declaration.Declarators)
                            {
                                if (declarator.Initializer != null)
                                {
                                    parts.Add(AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod));
                                }
                            }
                        }

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
                                    AnalyzeOperation(returnOperation.ReturnedValue, semanticModel, currentMethod),
                                }.Concat(returnOperation.ChildOperations
                                    .Where(child => !ReferenceEquals(child, returnOperation.ReturnedValue))
                                    .Select(child => AnalyzeOperation(child, semanticModel, currentMethod))))
                            : CombineSequence(returnOperation.ChildOperations.Select(child => AnalyzeOperation(child, semanticModel, currentMethod)));

                    case IConditionalOperation conditionalOperation:
                        return AnalyzeConditionalOperation(conditionalOperation, semanticModel, currentMethod);

                    case IForLoopOperation forLoopOperation:
                        return AnalyzeForLoop(forLoopOperation, semanticModel, currentMethod);

                    case IForEachLoopOperation forEachLoopOperation:
                        return AnalyzeForEachLoop(forEachLoopOperation, semanticModel, currentMethod);

                    case IWhileLoopOperation whileLoopOperation:
                        return whileLoopOperation.ConditionIsTop
                            ? AnalyzeWhileLoop(whileLoopOperation, semanticModel, currentMethod)
                            : AnalyzeDoLoop(whileLoopOperation, semanticModel, currentMethod);

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
                            operation.Syntax.SyntaxTree);

                    default:
                        return CombineSequence(operation.ChildOperations.Select(child => AnalyzeOperation(child, semanticModel, currentMethod)));
                }
            }

            private ComplexityArtifacts AnalyzeConditionalOperation(
                IConditionalOperation conditionalOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var conditionCost = AnalyzeOperation(conditionalOperation.Condition, semanticModel, currentMethod);
                if (TryGetConstantBoolean(conditionalOperation.Condition.Syntax, semanticModel, out var constantValue))
                {
                    return CombineSequence(
                        conditionCost,
                        constantValue
                            ? AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod)
                            : AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod));
                }

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
                var beforeCost = CombineSequence(forLoopOperation.Before.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
                var conditionCost = AnalyzeOperation(forLoopOperation.Condition, semanticModel, currentMethod);
                var bottomCost = CombineSequence(forLoopOperation.AtLoopBottom.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
                var bodyCost = AnalyzeOperation(forLoopOperation.Body, semanticModel, currentMethod);

                if (forLoopOperation.Syntax is not ForStatementSyntax forStatement ||
                    !TryGetForLoopBound(forStatement, semanticModel, currentMethod, out var bound))
                {
                    return CombineSequence(
                        beforeCost,
                        ComplexityArtifacts.Unknown(
                            SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                            forLoopOperation.Syntax,
                            forLoopOperation.Syntax.SyntaxTree,
                            beforeCost,
                            conditionCost,
                            bottomCost,
                            bodyCost));
                }

                var perIteration = CombineSequence(conditionCost, bottomCost, bodyCost);
                var multiplied = Multiply(bound.Cost, perIteration);
                multiplied = multiplied.WithDriver(CreateDriver(
                    "ForLoop",
                    "for-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                    forStatement,
                    forStatement.SyntaxTree));
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
                    !TryGetForeachBound(forEachLoopOperation.Collection.Syntax, semanticModel, currentMethod, out var bound))
                {
                    return CombineSequence(
                        collectionCost,
                        ComplexityArtifacts.Unknown(
                            SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                            forEachLoopOperation.Syntax,
                            forEachLoopOperation.Syntax.SyntaxTree,
                            collectionCost,
                            bodyCost));
                }

                var multiplied = Multiply(bound.Cost, bodyCost);
                multiplied = multiplied.WithDriver(CreateDriver(
                    "ForeachLoop",
                    "foreach bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                    foreachSyntax,
                    foreachSyntax.SyntaxTree));
                return CombineSequence(collectionCost, multiplied);
            }

            private ComplexityArtifacts AnalyzeWhileLoop(
                IWhileLoopOperation whileLoopOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var conditionCost = AnalyzeOperation(whileLoopOperation.Condition, semanticModel, currentMethod);
                var bodyCost = AnalyzeOperation(whileLoopOperation.Body, semanticModel, currentMethod);

                if (whileLoopOperation.Syntax is not WhileStatementSyntax whileStatement ||
                    !TryGetWhileLikeBound(
                        whileStatement.Condition,
                        whileStatement.Statement,
                        semanticModel,
                        currentMethod,
                        out var bound))
                {
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedWhileLoop,
                        whileLoopOperation.Syntax,
                        whileLoopOperation.Syntax.SyntaxTree,
                        conditionCost,
                        bodyCost);
                }

                var multiplied = Multiply(bound.Cost, CombineSequence(conditionCost, bodyCost));
                multiplied = multiplied.WithDriver(CreateDriver(
                    "WhileLoop",
                    "while-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                    whileStatement,
                    whileStatement.SyntaxTree));
                return multiplied;
            }

            private ComplexityArtifacts AnalyzeDoLoop(
                IWhileLoopOperation doLoopOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var conditionCost = AnalyzeOperation(doLoopOperation.Condition, semanticModel, currentMethod);
                var bodyCost = AnalyzeOperation(doLoopOperation.Body, semanticModel, currentMethod);

                if (doLoopOperation.Syntax is not DoStatementSyntax doStatement ||
                    !TryGetWhileLikeBound(
                        doStatement.Condition,
                        doStatement.Statement,
                        semanticModel,
                        currentMethod,
                        out var bound))
                {
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnsupportedWhileLoop,
                        doLoopOperation.Syntax,
                        doLoopOperation.Syntax.SyntaxTree,
                        conditionCost,
                        bodyCost);
                }

                var multiplied = Multiply(bound.Cost, CombineSequence(conditionCost, bodyCost));
                multiplied = multiplied.WithDriver(CreateDriver(
                    "DoLoop",
                    "do-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
                    doStatement,
                    doStatement.SyntaxTree));
                return multiplied;
            }

            private ComplexityArtifacts AnalyzeInvocation(
                IInvocationOperation invocationOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var receiverAndArguments = new List<ComplexityArtifacts>();
                if (invocationOperation.Instance != null)
                {
                    receiverAndArguments.Add(AnalyzeOperation(invocationOperation.Instance, semanticModel, currentMethod));
                }

                foreach (var argument in invocationOperation.Arguments)
                {
                    receiverAndArguments.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));
                }

                var callCost = AnalyzeMethodCall(
                    invocationOperation.TargetMethod,
                    invocationOperation.Syntax,
                    semanticModel,
                    currentMethod,
                    invocationOperation.Arguments.Select(argument => argument.Value.Syntax).ToImmutableArray(),
                    invocationOperation.Instance?.Syntax);
                receiverAndArguments.Add(callCost);
                return CombineSequence(receiverAndArguments);
            }

            private ComplexityArtifacts AnalyzeObjectCreation(
                IObjectCreationOperation objectCreationOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var parts = new List<ComplexityArtifacts>();
                foreach (var argument in objectCreationOperation.Arguments)
                {
                    parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));
                }

                if (objectCreationOperation.Initializer != null)
                {
                    parts.Add(AnalyzeOperation(objectCreationOperation.Initializer, semanticModel, currentMethod));
                }

                if (objectCreationOperation.Constructor != null)
                {
                    parts.Add(AnalyzeMethodCall(
                        objectCreationOperation.Constructor,
                        objectCreationOperation.Syntax,
                        semanticModel,
                        currentMethod,
                        objectCreationOperation.Arguments.Select(argument => argument.Value.Syntax).ToImmutableArray(),
                        receiverSyntax: null));
                }

                return CombineSequence(parts);
            }

            private ComplexityArtifacts AnalyzePropertyReference(
                IPropertyReferenceOperation propertyReferenceOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var parts = new List<ComplexityArtifacts>();
                if (propertyReferenceOperation.Instance != null)
                {
                    parts.Add(AnalyzeOperation(propertyReferenceOperation.Instance, semanticModel, currentMethod));
                }

                foreach (var argument in propertyReferenceOperation.Arguments)
                {
                    parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));
                }

                var getter = propertyReferenceOperation.Property.GetMethod;
                if (getter != null)
                {
                    parts.Add(AnalyzeMethodCall(
                        getter,
                        propertyReferenceOperation.Syntax,
                        semanticModel,
                        currentMethod,
                        propertyReferenceOperation.Arguments.Select(argument => argument.Value.Syntax).ToImmutableArray(),
                        propertyReferenceOperation.Instance?.Syntax));
                }

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
                    .Select(@case => CombineSequence(@case.Body.Select(statement => AnalyzeOperation(statement, semanticModel, currentMethod))))
                    .ToArray();
                if (branchCosts.Length == 0)
                {
                    return conditionCost;
                }

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
                if (armCosts.Length == 0)
                {
                    return valueCost;
                }

                return CombineSequence(valueCost, CombineBranch(armCosts));
            }

            private ComplexityArtifacts AnalyzeTryOperation(
                ITryOperation tryOperation,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod)
            {
                var paths = new List<ComplexityArtifacts>
                {
                    AnalyzeOperation(tryOperation.Body, semanticModel, currentMethod),
                };
                foreach (var @catch in tryOperation.Catches)
                {
                    paths.Add(AnalyzeOperation(@catch.Handler, semanticModel, currentMethod));
                }

                var finallyCost = AnalyzeOperation(tryOperation.Finally, semanticModel, currentMethod);
                return CombineSequence(CombineBranch(paths), finallyCost);
            }

            private ComplexityArtifacts AnalyzeMethodCall(
                IMethodSymbol methodSymbol,
                SyntaxNode syntax,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                ImmutableArray<SyntaxNode> argumentSyntaxes,
                SyntaxNode? receiverSyntax)
            {
                if (TryGetKnownMethodCost(methodSymbol, out var knownCost))
                {
                    return ComplexityArtifacts.FromCost(
                        knownCost,
                        calleeSummaries: new[]
                        {
                            CreateCalleeInfo(
                                methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                knownCost,
                                currentMethod),
                        });
                }

                if (!IsSourceMethod(methodSymbol))
                {
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.ExternalCallee,
                        syntax,
                        syntax.SyntaxTree,
                        calleeSummaries: new[]
                        {
                            new SymbolicComplexityCalleeInfo(
                                methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                "Unknown",
                                SymbolicComplexityKind.Unknown,
                                true,
                                SymbolicComplexityUnknownReason.ExternalCallee),
                        });
                }

                if (!TryResolveSourceMethod(methodSymbol, out var declaration, out var bodyNode, out var sourceModel))
                {
                    return ComplexityArtifacts.Unknown(
                        SymbolicComplexityUnknownReason.UnknownCallee,
                        syntax,
                        syntax.SyntaxTree,
                        calleeSummaries: new[]
                        {
                            new SymbolicComplexityCalleeInfo(
                                methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                                "Unknown",
                                SymbolicComplexityKind.Unknown,
                                true,
                                SymbolicComplexityUnknownReason.UnknownCallee),
                        });
                }

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
                {
                    drivers.Add(CreateDriver(
                        "Call",
                        "call to " + calleeInfo.MethodDisplayName + " contributes " + calleeInfo.ComplexityText,
                        syntax,
                        syntax.SyntaxTree));
                }

                return ComplexityArtifacts.FromCost(
                    substitutionResult.Cost,
                    drivers.Concat(calleeSummary.Drivers),
                    substitutionResult.UnknownReasons.Concat(calleeSummary.UnknownReasons),
                    new[] { calleeInfo }.Concat(calleeSummary.CalleeSummaries));
            }

            private static bool IsSourceMethod(IMethodSymbol methodSymbol)
            {
                return methodSymbol.DeclaringSyntaxReferences.Length != 0;
            }

            private bool TryResolveSourceMethod(
                IMethodSymbol methodSymbol,
                out SyntaxNode declaration,
                out SyntaxNode bodyNode,
                out SemanticModel semanticModel)
            {
                foreach (var syntaxReference in methodSymbol.OriginalDefinition.DeclaringSyntaxReferences)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (syntaxReference.GetSyntax(_cancellationToken) is not SyntaxNode candidate)
                    {
                        continue;
                    }

                    var body = GetBodyNode(candidate);
                    if (body == null)
                    {
                        continue;
                    }

                    declaration = candidate;
                    bodyNode = body;
                    semanticModel = _compilation.GetSemanticModel(candidate.SyntaxTree);
                    return true;
                }

                declaration = null!;
                bodyNode = null!;
                semanticModel = null!;
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
                if (typeSymbol == null)
                {
                    return false;
                }

                if (typeSymbol is IArrayTypeSymbol)
                {
                    return true;
                }

                if (typeSymbol.SpecialType == SpecialType.System_String)
                {
                    return true;
                }

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
                {
                    return new SubstitutionResult(cost, Array.Empty<SymbolicComplexityDriverInfo>(), Array.Empty<SymbolicComplexityUnknownReason>());
                }

                SymbolicCostExpression? ResolveFactor(string key)
                {
                    if (TryParseParameterKey(key, out var parameterIndex, out var projection))
                    {
                        if (parameterIndex < 0 || parameterIndex >= argumentSyntaxes.Length)
                        {
                            return SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
                        }

                        return TryCreateCostFromExpression(
                            argumentSyntaxes[parameterIndex] as ExpressionSyntax,
                            callerSemanticModel,
                            callerMethod,
                            projection,
                            allowConstants: true,
                            out var expressionCost)
                            ? expressionCost
                            : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
                    }

                    if (string.Equals(key, "$this.length", StringComparison.Ordinal))
                    {
                        return TryCreateCostFromExpression(
                            receiverSyntax as ExpressionSyntax,
                            callerSemanticModel,
                            callerMethod,
                            CostProjection.LengthOrCount,
                            allowConstants: false,
                            out var receiverCost)
                            ? receiverCost
                            : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
                    }

                    if (string.Equals(key, "$this", StringComparison.Ordinal))
                    {
                        return TryCreateCostFromExpression(
                            receiverSyntax as ExpressionSyntax,
                            callerSemanticModel,
                            callerMethod,
                            CostProjection.Value,
                            allowConstants: true,
                            out var receiverCost)
                            ? receiverCost
                            : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
                    }

                    return null;
                }

                var substituted = cost.Substitute(ResolveFactor);
                var reasons = substituted.IsUnknown
                    ? new[] { substituted.UnknownReason == SymbolicComplexityUnknownReason.None ? SymbolicComplexityUnknownReason.UnknownCallee : substituted.UnknownReason }
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
                if (!key.StartsWith("$p", StringComparison.Ordinal))
                {
                    return false;
                }

                var suffixStart = key.IndexOf(':');
                if (suffixStart < 0)
                {
                    return false;
                }

                if (!int.TryParse(key.Substring(2, suffixStart - 2), NumberStyles.None, CultureInfo.InvariantCulture, out parameterIndex))
                {
                    return false;
                }

                var suffix = key.Substring(suffixStart + 1);
                projection = string.Equals(suffix, "length", StringComparison.Ordinal)
                    ? CostProjection.LengthOrCount
                    : CostProjection.Value;
                return true;
            }

            private static ComplexityArtifacts CombineSequence(IEnumerable<ComplexityArtifacts> parts)
            {
                return CombineInternal(parts, useBranchMax: false);
            }

            private static ComplexityArtifacts CombineSequence(params ComplexityArtifacts[] parts)
            {
                return CombineInternal(parts, useBranchMax: false);
            }

            private static ComplexityArtifacts CombineBranch(IEnumerable<ComplexityArtifacts> parts)
            {
                return CombineInternal(parts, useBranchMax: true);
            }

            private static ComplexityArtifacts CombineBranch(params ComplexityArtifacts[] parts)
            {
                return CombineInternal(parts, useBranchMax: true);
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
                {
                    reasons.Add(combinedCost.UnknownReason);
                }

                return ComplexityArtifacts.FromCost(combinedCost, drivers, reasons, callees);
            }

            private static ComplexityArtifacts Multiply(SymbolicCostExpression multiplier, ComplexityArtifacts body)
            {
                var cost = SymbolicCostExpression.Multiply(multiplier, body.Cost);
                var reasons = new List<SymbolicComplexityUnknownReason>(body.UnknownReasons);
                if (cost.IsUnknown && cost.UnknownReason != SymbolicComplexityUnknownReason.None)
                {
                    reasons.Add(cost.UnknownReason);
                }

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
                SyntaxTree syntaxTree)
            {
                var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
                    syntaxTree,
                    node.SpanStart,
                    CancellationToken.None,
                    validatePosition: true);
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

            private static bool TryGetForLoopBound(
                ForStatementSyntax forStatement,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                out LoopBoundInfo bound)
            {
                bound = default;
                if (!TryGetForLoopVariable(forStatement, semanticModel, out var loopSymbol, out var initializerExpression))
                {
                    return false;
                }

                if (!TryGetIntegralConstant(initializerExpression, semanticModel, out _))
                {
                    return false;
                }

                if (forStatement.Condition is not BinaryExpressionSyntax condition ||
                    !TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction, out var boundCost, out var boundExpressionText, out var dependentSymbols))
                {
                    return false;
                }

                if (!TryParseForLoopStep(forStatement, loopSymbol, semanticModel, out var stepDirection) ||
                    stepDirection != direction)
                {
                    return false;
                }

                if (dependentSymbols.Any(symbol => IsSymbolMutatedInStatement(symbol, forStatement.Statement, semanticModel)) ||
                    IsSymbolMutatedInStatement(loopSymbol, forStatement.Statement, semanticModel))
                {
                    return false;
                }

                bound = new LoopBoundInfo(boundCost, boundExpressionText);
                return true;
            }

            private static bool TryGetWhileLikeBound(
                ExpressionSyntax conditionExpression,
                StatementSyntax loopBody,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                out LoopBoundInfo bound)
            {
                bound = default;
                if (conditionExpression is not BinaryExpressionSyntax condition)
                {
                    return false;
                }

                if (!TryGetLoopConditionVariable(condition, semanticModel, out var loopSymbol))
                {
                    return false;
                }

                if (!TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction, out var boundCost, out var boundExpressionText, out var dependentSymbols))
                {
                    return false;
                }

                var updates = GetRecognizedLoopUpdates(loopBody, loopSymbol, semanticModel);
                if (updates.Count != 1 || updates[0] != direction)
                {
                    return false;
                }

                if (dependentSymbols.Any(symbol => IsSymbolMutatedInStatement(symbol, loopBody, semanticModel)) ||
                    IsSymbolMutatedInStatement(loopSymbol, loopBody, semanticModel, allowRecognizedLoopUpdates: true) == false)
                {
                    return false;
                }

                bound = new LoopBoundInfo(boundCost, boundExpressionText);
                return true;
            }

            private static bool TryGetForeachBound(
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
                        allowConstants: false,
                        out var cost))
                {
                    bound = default;
                    return false;
                }

                bound = new LoopBoundInfo(cost, collectionExpression.ToString());
                return true;
            }

            private static bool TryGetForLoopVariable(
                ForStatementSyntax forStatement,
                SemanticModel semanticModel,
                out ISymbol loopSymbol,
                out ExpressionSyntax initializerExpression)
            {
                if (forStatement.Declaration is { Variables.Count: 1 } declaration &&
                    declaration.Variables[0].Initializer != null &&
                    semanticModel.GetDeclaredSymbol(declaration.Variables[0]) is ISymbol declaredSymbol)
                {
                    loopSymbol = declaredSymbol;
                    initializerExpression = declaration.Variables[0].Initializer!.Value;
                    return true;
                }

                if (forStatement.Initializers.Count == 1 &&
                    forStatement.Initializers[0] is AssignmentExpressionSyntax assignment &&
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol is ISymbol assignedSymbol)
                {
                    loopSymbol = assignedSymbol;
                    initializerExpression = assignment.Right;
                    return true;
                }

                loopSymbol = null!;
                initializerExpression = null!;
                return false;
            }

            private static bool TryGetLoopConditionVariable(
                BinaryExpressionSyntax condition,
                SemanticModel semanticModel,
                out ISymbol symbol)
            {
                symbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Left)).Symbol!;
                if (symbol != null)
                {
                    return true;
                }

                symbol = semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Right)).Symbol!;
                return symbol != null;
            }

            private static bool TryParseLoopCondition(
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
                var leftSymbol = semanticModel.GetSymbolInfo(left).Symbol;
                var rightSymbol = semanticModel.GetSymbolInfo(right).Symbol;

                ExpressionSyntax? boundExpression = null;
                if (SymbolEquals(leftSymbol, loopSymbol))
                {
                    direction = condition.IsKind(SyntaxKind.LessThanExpression) || condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                        ? StepDirection.Up
                        : condition.IsKind(SyntaxKind.GreaterThanExpression) || condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                            ? StepDirection.Down
                            : StepDirection.None;
                    boundExpression = right;
                }
                else if (SymbolEquals(rightSymbol, loopSymbol))
                {
                    direction = condition.IsKind(SyntaxKind.GreaterThanExpression) || condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                        ? StepDirection.Up
                        : condition.IsKind(SyntaxKind.LessThanExpression) || condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                            ? StepDirection.Down
                            : StepDirection.None;
                    boundExpression = left;
                }

                if (direction == StepDirection.None ||
                    boundExpression == null ||
                    !TryCreateCostFromExpression(boundExpression, semanticModel, currentMethod, CostProjection.Value, allowConstants: true, out boundCost))
                {
                    return false;
                }

                boundDescription = boundExpression.ToString();
                dependentSymbols = GetDependentSymbols(boundExpression, semanticModel);
                return true;
            }

            private static bool TryParseForLoopStep(
                ForStatementSyntax forStatement,
                ISymbol loopSymbol,
                SemanticModel semanticModel,
                out StepDirection direction)
            {
                direction = StepDirection.None;
                if (forStatement.Incrementors.Count != 1)
                {
                    return false;
                }

                return TryParseLoopStep(forStatement.Incrementors[0], loopSymbol, semanticModel, out direction);
            }

            private static bool TryParseLoopStep(
                ExpressionSyntax expression,
                ISymbol loopSymbol,
                SemanticModel semanticModel,
                out StepDirection direction)
            {
                direction = StepDirection.None;
                expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
                switch (expression)
                {
                    case PostfixUnaryExpressionSyntax postfix
                        when (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) &&
                             SymbolEquals(semanticModel.GetSymbolInfo(postfix.Operand).Symbol, loopSymbol):
                        direction = postfix.IsKind(SyntaxKind.PostIncrementExpression) ? StepDirection.Up : StepDirection.Down;
                        return true;

                    case PrefixUnaryExpressionSyntax prefix
                        when (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) &&
                             SymbolEquals(semanticModel.GetSymbolInfo(prefix.Operand).Symbol, loopSymbol):
                        direction = prefix.IsKind(SyntaxKind.PreIncrementExpression) ? StepDirection.Up : StepDirection.Down;
                        return true;

                    case AssignmentExpressionSyntax assignment
                        when SymbolEquals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, loopSymbol):
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

            private static bool IsSymbolMutatedInStatement(
                ISymbol symbol,
                StatementSyntax statement,
                SemanticModel semanticModel,
                bool allowRecognizedLoopUpdates = false)
            {
                var sawMutation = false;
                foreach (var node in statement.DescendantNodesAndSelf(
                             static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
                {
                    var mutatedExpression = node switch
                    {
                        AssignmentExpressionSyntax assignment => assignment.Left,
                        PrefixUnaryExpressionSyntax prefix
                            when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) =>
                            prefix.Operand,
                        PostfixUnaryExpressionSyntax postfix
                            when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression) =>
                            postfix.Operand,
                        ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
                        _ => null,
                    };

                    if (mutatedExpression == null ||
                        !IsReferenceToSymbol(mutatedExpression, symbol, semanticModel))
                    {
                        continue;
                    }

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

            private static List<StepDirection> GetRecognizedLoopUpdates(
                StatementSyntax loopBody,
                ISymbol loopSymbol,
                SemanticModel semanticModel)
            {
                var updates = new List<StepDirection>();
                foreach (var expression in loopBody.DescendantNodesAndSelf(
                             static candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                         .OfType<ExpressionSyntax>())
                {
                    if (TryParseLoopStep(expression, loopSymbol, semanticModel, out var direction))
                    {
                        updates.Add(direction);
                    }
                }

                return updates;
            }

            private static ImmutableArray<ISymbol> GetDependentSymbols(
                ExpressionSyntax expression,
                SemanticModel semanticModel)
            {
                var builder = ImmutableArray.CreateBuilder<ISymbol>();
                foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    if (semanticModel.GetSymbolInfo(identifier).Symbol is ISymbol symbol &&
                        builder.All(existing => !SymbolEquals(existing, symbol)))
                    {
                        builder.Add(symbol);
                    }
                }

                return builder.ToImmutable();
            }

            private static bool TryCreateCostFromExpression(
                ExpressionSyntax? expression,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                CostProjection projection,
                bool allowConstants,
                out SymbolicCostExpression cost)
            {
                cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
                if (expression == null)
                {
                    return false;
                }

                expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);

                if (allowConstants && TryGetIntegralConstant(expression, semanticModel, out _))
                {
                    cost = SymbolicCostExpression.Constant();
                    return true;
                }

                if (projection == CostProjection.LengthOrCount)
                {
                    if (TryCreateLengthOrCountCost(expression, semanticModel, currentMethod, out cost))
                    {
                        return true;
                    }
                }
                else if (TryCreateScalarCost(expression, semanticModel, currentMethod, out cost))
                {
                    return true;
                }

                if (expression is BinaryExpressionSyntax binaryExpression &&
                    (binaryExpression.IsKind(SyntaxKind.AddExpression) || binaryExpression.IsKind(SyntaxKind.SubtractExpression)))
                {
                    if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, out _) &&
                        TryCreateCostFromExpression(binaryExpression.Left, semanticModel, currentMethod, projection, allowConstants, out cost))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                        TryGetIntegralConstant(binaryExpression.Left, semanticModel, out _) &&
                        TryCreateCostFromExpression(binaryExpression.Right, semanticModel, currentMethod, projection, allowConstants, out cost))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryCreateScalarCost(
                ExpressionSyntax expression,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                out SymbolicCostExpression cost)
            {
                if (expression is MemberAccessExpressionSyntax memberAccess &&
                    (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
                     string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)) &&
                    TryCreateLengthOrCountCost(expression, semanticModel, currentMethod, out cost))
                {
                    return true;
                }

                if (semanticModel.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter &&
                    SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition))
                {
                    cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":value");
                    return true;
                }

                if (semanticModel.GetSymbolInfo(expression).Symbol is ISymbol symbol)
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

            private static bool TryCreateLengthOrCountCost(
                ExpressionSyntax expression,
                SemanticModel semanticModel,
                IMethodSymbol currentMethod,
                out SymbolicCostExpression cost)
            {
                if (expression is MemberAccessExpressionSyntax memberAccess &&
                    (string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) ||
                     string.Equals(memberAccess.Name.Identifier.ValueText, "Count", StringComparison.Ordinal)))
                {
                    if (semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is IParameterSymbol parameter &&
                        SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition))
                    {
                        cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                        return true;
                    }

                    if (memberAccess.Expression is ThisExpressionSyntax)
                    {
                        cost = SymbolicCostExpression.Variable("$this.length");
                        return true;
                    }

                    if (semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is ISymbol receiverSymbol)
                    {
                        cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + "." + memberAccess.Name.Identifier.ValueText);
                        return true;
                    }
                }

                var expressionType = semanticModel.GetTypeInfo(expression).Type;
                if (expressionType != null && IsKnownSizedType(expressionType))
                {
                    if (semanticModel.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter &&
                        SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, currentMethod.OriginalDefinition))
                    {
                        cost = SymbolicCostExpression.Variable("$p" + parameter.Ordinal + ":length");
                        return true;
                    }

                    if (expression is ThisExpressionSyntax)
                    {
                        cost = SymbolicCostExpression.Variable("$this.length");
                        return true;
                    }

                    if (semanticModel.GetSymbolInfo(expression).Symbol is ISymbol receiverSymbol)
                    {
                        cost = SymbolicCostExpression.Variable("name:" + receiverSymbol.Name + ".Length");
                        return true;
                    }
                }

                cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
                return false;
            }

            private static bool TryGetIntegralConstant(
                ExpressionSyntax expression,
                SemanticModel semanticModel,
                out long value)
            {
                var constant = semanticModel.GetConstantValue(expression);
                if (!constant.HasValue)
                {
                    value = 0;
                    return false;
                }

                switch (constant.Value)
                {
                    case byte byteValue:
                        value = byteValue;
                        return true;
                    case sbyte sbyteValue:
                        value = sbyteValue;
                        return true;
                    case short shortValue:
                        value = shortValue;
                        return true;
                    case ushort ushortValue:
                        value = ushortValue;
                        return true;
                    case int intValue:
                        value = intValue;
                        return true;
                    case uint uintValue:
                        value = uintValue;
                        return true;
                    case long longValue:
                        value = longValue;
                        return true;
                    case ulong ulongValue when ulongValue <= long.MaxValue:
                        value = (long)ulongValue;
                        return true;
                    case char charValue:
                        value = charValue;
                        return true;
                    default:
                        value = 0;
                        return false;
                }
            }

            private static bool TryGetConstantBoolean(
                SyntaxNode syntaxNode,
                SemanticModel semanticModel,
                out bool value)
            {
                if (syntaxNode is ExpressionSyntax expression &&
                    semanticModel.GetConstantValue(expression) is { HasValue: true, Value: bool boolValue })
                {
                    value = boolValue;
                    return true;
                }

                value = false;
                return false;
            }

            private static bool IsReferenceToSymbol(
                ExpressionSyntax expression,
                ISymbol symbol,
                SemanticModel semanticModel)
            {
                return SymbolEquals(
                    semanticModel.GetSymbolInfo(CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression)).Symbol,
                    symbol);
            }

            private static bool SymbolEquals(ISymbol? left, ISymbol? right)
            {
                return left != null &&
                    right != null &&
                    SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);
            }
        }

        private sealed class ResolvedComplexityTarget
        {
            public ResolvedComplexityTarget(
                SyntaxTree syntaxTree,
                SemanticModel semanticModel,
                SyntaxNode declaration,
                SyntaxNode bodyNode,
                IMethodSymbol symbol,
                string filePath,
                string methodName,
                string methodDisplayName,
                string declarationKind,
                int spanStart,
                int spanEnd,
                int startLine,
                int startColumn,
                int endLine,
                int endColumn)
            {
                SyntaxTree = syntaxTree;
                SemanticModel = semanticModel;
                Declaration = declaration;
                BodyNode = bodyNode;
                Symbol = symbol;
                FilePath = filePath;
                MethodName = methodName;
                MethodDisplayName = methodDisplayName;
                DeclarationKind = declarationKind;
                SpanStart = spanStart;
                SpanEnd = spanEnd;
                StartLine = startLine;
                StartColumn = startColumn;
                EndLine = endLine;
                EndColumn = endColumn;
            }

            public SyntaxTree SyntaxTree { get; }

            public SemanticModel SemanticModel { get; }

            public SyntaxNode Declaration { get; }

            public SyntaxNode BodyNode { get; }

            public IMethodSymbol Symbol { get; }

            public string FilePath { get; }

            public string MethodName { get; }

            public string MethodDisplayName { get; }

            public string DeclarationKind { get; }

            public int SpanStart { get; }

            public int SpanEnd { get; }

            public int StartLine { get; }

            public int StartColumn { get; }

            public int EndLine { get; }

            public int EndColumn { get; }
        }

        private sealed class MethodAnalysisSummary
        {
            public MethodAnalysisSummary(
                SymbolicCostExpression cost,
                ImmutableArray<SymbolicComplexityDriverInfo> drivers,
                ImmutableArray<SymbolicComplexityUnknownReason> unknownReasons,
                ImmutableArray<SymbolicComplexityCalleeInfo> calleeSummaries)
            {
                Cost = cost;
                Drivers = drivers;
                UnknownReasons = unknownReasons;
                CalleeSummaries = calleeSummaries;
            }

            public SymbolicCostExpression Cost { get; }

            public ImmutableArray<SymbolicComplexityDriverInfo> Drivers { get; }

            public ImmutableArray<SymbolicComplexityUnknownReason> UnknownReasons { get; }

            public ImmutableArray<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }
        }

        private sealed class ComplexityArtifacts
        {
            public static readonly ComplexityArtifacts Constant = new ComplexityArtifacts(
                SymbolicCostExpression.Constant(),
                Array.Empty<SymbolicComplexityDriverInfo>(),
                Array.Empty<SymbolicComplexityUnknownReason>(),
                Array.Empty<SymbolicComplexityCalleeInfo>());

            private ComplexityArtifacts(
                SymbolicCostExpression cost,
                IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
                IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons,
                IReadOnlyList<SymbolicComplexityCalleeInfo> calleeSummaries)
            {
                Cost = cost;
                Drivers = drivers;
                UnknownReasons = unknownReasons;
                CalleeSummaries = calleeSummaries;
            }

            public SymbolicCostExpression Cost { get; }

            public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

            public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

            public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

            public static ComplexityArtifacts FromCost(
                SymbolicCostExpression cost,
                IEnumerable<SymbolicComplexityDriverInfo>? drivers = null,
                IEnumerable<SymbolicComplexityUnknownReason>? unknownReasons = null,
                IEnumerable<SymbolicComplexityCalleeInfo>? calleeSummaries = null)
            {
                return new ComplexityArtifacts(
                    cost,
                    drivers?.ToArray() ?? Array.Empty<SymbolicComplexityDriverInfo>(),
                    unknownReasons?.ToArray() ?? Array.Empty<SymbolicComplexityUnknownReason>(),
                    calleeSummaries?.ToArray() ?? Array.Empty<SymbolicComplexityCalleeInfo>());
            }

            public static ComplexityArtifacts Unknown(
                SymbolicComplexityUnknownReason reason,
                SyntaxNode syntax,
                SyntaxTree syntaxTree,
                params ComplexityArtifacts[] parts)
            {
                return Unknown(reason, syntax, syntaxTree, parts.AsEnumerable(), null);
            }

            public static ComplexityArtifacts Unknown(
                SymbolicComplexityUnknownReason reason,
                SyntaxNode syntax,
                SyntaxTree syntaxTree,
                IEnumerable<ComplexityArtifacts>? parts = null,
                IEnumerable<SymbolicComplexityCalleeInfo>? calleeSummaries = null)
            {
                var drivers = new List<SymbolicComplexityDriverInfo>();
                var reasons = new List<SymbolicComplexityUnknownReason> { reason };
                var callees = new List<SymbolicComplexityCalleeInfo>();
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        drivers.AddRange(part.Drivers);
                        reasons.AddRange(part.UnknownReasons);
                        callees.AddRange(part.CalleeSummaries);
                    }
                }

                if (calleeSummaries != null)
                {
                    callees.AddRange(calleeSummaries);
                }

                drivers.Add(CreateUnknownDriver(reason, syntax, syntaxTree));
                return FromCost(SymbolicCostExpression.Unknown(reason), drivers, reasons, callees);
            }

            public ComplexityArtifacts WithDriver(SymbolicComplexityDriverInfo driver)
            {
                var drivers = Drivers.ToList();
                drivers.Add(driver);
                return new ComplexityArtifacts(Cost, drivers, UnknownReasons, CalleeSummaries);
            }

            private static SymbolicComplexityDriverInfo CreateUnknownDriver(
                SymbolicComplexityUnknownReason reason,
                SyntaxNode syntax,
                SyntaxTree syntaxTree)
            {
                var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
                    syntaxTree,
                    syntax.SpanStart,
                    CancellationToken.None,
                    validatePosition: true);
                return new SymbolicComplexityDriverInfo(
                    "Unknown",
                    reason.ToString(),
                    syntax.SpanStart,
                    syntax.Span.Length,
                    lineColumn.Line,
                    lineColumn.Column);
            }
        }

        private sealed class SubstitutionResult
        {
            public SubstitutionResult(
                SymbolicCostExpression cost,
                IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
                IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons)
            {
                Cost = cost;
                Drivers = drivers;
                UnknownReasons = unknownReasons;
            }

            public SymbolicCostExpression Cost { get; }

            public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

            public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }
        }

        private readonly struct LoopBoundInfo
        {
            public LoopBoundInfo(SymbolicCostExpression cost, string description)
            {
                Cost = cost;
                Description = description;
            }

            public SymbolicCostExpression Cost { get; }

            public string Description { get; }
        }

        private enum StepDirection
        {
            None,
            Up,
            Down,
        }

        private enum CostProjection
        {
            Value,
            LengthOrCount,
        }

        internal sealed class SymbolicCostExpression
        {
            private SymbolicCostExpression(
                CostNodeKind kind,
                ImmutableSortedDictionary<string, int>? factors = null,
                ImmutableArray<SymbolicCostExpression>? alternatives = null,
                SymbolicComplexityUnknownReason unknownReason = SymbolicComplexityUnknownReason.None)
            {
                Kind = kind;
                Factors = factors ?? ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                Alternatives = alternatives ?? ImmutableArray<SymbolicCostExpression>.Empty;
                UnknownReason = unknownReason;
            }

            private CostNodeKind Kind { get; }

            private ImmutableSortedDictionary<string, int> Factors { get; }

            private ImmutableArray<SymbolicCostExpression> Alternatives { get; }

            public SymbolicComplexityUnknownReason UnknownReason { get; }

            public bool IsUnknown => Kind == CostNodeKind.Unknown;

            public bool IsRecursiveUnknown => Kind == CostNodeKind.RecursiveUnknown;

            public bool IsConservative => IsUnknown || IsRecursiveUnknown || (Kind == CostNodeKind.Max && Alternatives.Any(static alternative => alternative.IsConservative));

            public bool IsConstant => Kind == CostNodeKind.Monomial && Factors.Count == 0;

            public static SymbolicCostExpression Constant()
            {
                return new SymbolicCostExpression(CostNodeKind.Monomial);
            }

            public static SymbolicCostExpression Variable(string key)
            {
                return new SymbolicCostExpression(
                    CostNodeKind.Monomial,
                    ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal).Add(key, 1));
            }

            public static SymbolicCostExpression Unknown(SymbolicComplexityUnknownReason reason)
            {
                return new SymbolicCostExpression(CostNodeKind.Unknown, unknownReason: reason);
            }

            public static SymbolicCostExpression RecursiveUnknown()
            {
                return new SymbolicCostExpression(CostNodeKind.RecursiveUnknown, unknownReason: SymbolicComplexityUnknownReason.RecursiveCycle);
            }

            public static SymbolicCostExpression Max(IEnumerable<SymbolicCostExpression> expressions)
            {
                if (expressions == null)
                {
                    return Constant();
                }

                var flattened = new List<SymbolicCostExpression>();
                foreach (var expression in expressions.Where(static expression => expression != null))
                {
                    if (expression.IsRecursiveUnknown)
                    {
                        return RecursiveUnknown();
                    }

                    if (expression.IsUnknown)
                    {
                        return Unknown(expression.UnknownReason);
                    }

                    if (expression.Kind == CostNodeKind.Max)
                    {
                        flattened.AddRange(expression.Alternatives);
                    }
                    else
                    {
                        flattened.Add(expression);
                    }
                }

                if (flattened.Count == 0)
                {
                    return Constant();
                }

                var reduced = new List<SymbolicCostExpression>();
                foreach (var expression in flattened)
                {
                    if (reduced.Any(existing => existing.Equals(expression)))
                    {
                        continue;
                    }

                    if (reduced.Any(existing => Dominates(existing, expression)))
                    {
                        continue;
                    }

                    reduced.RemoveAll(existing => Dominates(expression, existing));
                    reduced.Add(expression);
                }

                if (reduced.Count == 1)
                {
                    return reduced[0];
                }

                return new SymbolicCostExpression(CostNodeKind.Max, alternatives: reduced.ToImmutableArray());
            }

            public static SymbolicCostExpression Multiply(SymbolicCostExpression left, SymbolicCostExpression right)
            {
                if (left.IsRecursiveUnknown || right.IsRecursiveUnknown)
                {
                    return RecursiveUnknown();
                }

                if (left.IsUnknown)
                {
                    return Unknown(left.UnknownReason);
                }

                if (right.IsUnknown)
                {
                    return Unknown(right.UnknownReason);
                }

                if (left.Kind == CostNodeKind.Max)
                {
                    return Max(left.Alternatives.Select(alternative => Multiply(alternative, right)));
                }

                if (right.Kind == CostNodeKind.Max)
                {
                    return Max(right.Alternatives.Select(alternative => Multiply(left, alternative)));
                }

                var factors = left.Factors;
                foreach (var pair in right.Factors)
                {
                    factors = factors.SetItem(
                        pair.Key,
                        factors.TryGetValue(pair.Key, out var exponent) ? exponent + pair.Value : pair.Value);
                }

                return new SymbolicCostExpression(CostNodeKind.Monomial, factors);
            }

            public SymbolicCostExpression Substitute(Func<string, SymbolicCostExpression?> resolver)
            {
                if (resolver == null)
                {
                    throw new ArgumentNullException(nameof(resolver));
                }

                if (Kind == CostNodeKind.Max)
                {
                    return Max(Alternatives.Select(alternative => alternative.Substitute(resolver)));
                }

                if (Kind != CostNodeKind.Monomial)
                {
                    return this;
                }

                var preservedFactors = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
                var substituted = Constant();
                foreach (var pair in Factors)
                {
                    var resolved = resolver(pair.Key);
                    if (resolved == null)
                    {
                        preservedFactors = preservedFactors.SetItem(pair.Key, pair.Value);
                        continue;
                    }

                    var accumulated = Constant();
                    for (var index = 0; index < pair.Value; index++)
                    {
                        accumulated = Multiply(accumulated, resolved);
                    }

                    substituted = Multiply(substituted, accumulated);
                }

                if (preservedFactors.Count != 0)
                {
                    substituted = Multiply(substituted, new SymbolicCostExpression(CostNodeKind.Monomial, preservedFactors));
                }

                return substituted;
            }

            public string ToBigOText(IMethodSymbol? contextMethod = null)
            {
                return "O(" + ToTermText(contextMethod) + ")";
            }

            public SymbolicComplexityKind ToPublicKind()
            {
                if (IsRecursiveUnknown)
                {
                    return SymbolicComplexityKind.RecursiveUnknown;
                }

                if (IsUnknown)
                {
                    return SymbolicComplexityKind.Unknown;
                }

                if (Kind == CostNodeKind.Max)
                {
                    return SymbolicComplexityKind.Max;
                }

                if (Factors.Count == 0)
                {
                    return SymbolicComplexityKind.Constant;
                }

                if (Factors.Count == 1)
                {
                    var factor = Factors.Single();
                    return factor.Value switch
                    {
                        1 => SymbolicComplexityKind.Linear,
                        2 => SymbolicComplexityKind.Quadratic,
                        _ => SymbolicComplexityKind.Product,
                    };
                }

                return SymbolicComplexityKind.Product;
            }

            private string ToTermText(IMethodSymbol? contextMethod)
            {
                if (IsRecursiveUnknown)
                {
                    return "RecursiveUnknown";
                }

                if (IsUnknown)
                {
                    return "Unknown";
                }

                if (Kind == CostNodeKind.Max)
                {
                    return "max(" + string.Join(", ", Alternatives.Select(alternative => alternative.ToTermText(contextMethod))) + ")";
                }

                if (Factors.Count == 0)
                {
                    return "1";
                }

                return string.Join(
                    " * ",
                    Factors.Select(pair => pair.Value == 1
                        ? RenderVariable(pair.Key, contextMethod)
                        : RenderVariable(pair.Key, contextMethod) + "^" + pair.Value.ToString(CultureInfo.InvariantCulture)));
            }

            private static string RenderVariable(string key, IMethodSymbol? contextMethod)
            {
                if (key.StartsWith("$p", StringComparison.Ordinal))
                {
                    var colonIndex = key.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var ordinalText = key.Substring(2, colonIndex - 2);
                        var suffix = key.Substring(colonIndex + 1);
                        if (contextMethod != null &&
                            int.TryParse(ordinalText, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
                            ordinal >= 0 &&
                            ordinal < contextMethod.Parameters.Length)
                        {
                            var parameterName = contextMethod.Parameters[ordinal].Name;
                            return string.Equals(suffix, "length", StringComparison.Ordinal)
                                ? parameterName + ".Length"
                                : parameterName;
                        }

                        return string.Equals(suffix, "length", StringComparison.Ordinal)
                            ? "p" + ordinalText + ".Length"
                            : "p" + ordinalText;
                    }
                }

                if (string.Equals(key, "$this.length", StringComparison.Ordinal))
                {
                    return "this.Length";
                }

                if (string.Equals(key, "$this", StringComparison.Ordinal))
                {
                    return "this";
                }

                return key.StartsWith("name:", StringComparison.Ordinal)
                    ? key.Substring("name:".Length)
                    : key;
            }

            private static bool Dominates(SymbolicCostExpression left, SymbolicCostExpression right)
            {
                if (left.Kind != CostNodeKind.Monomial || right.Kind != CostNodeKind.Monomial)
                {
                    return false;
                }

                if (left.Factors.Count == 0)
                {
                    return right.Factors.Count == 0;
                }

                if (right.Factors.Count == 0)
                {
                    return true;
                }

                foreach (var pair in right.Factors)
                {
                    if (!left.Factors.TryGetValue(pair.Key, out var leftExponent) || leftExponent < pair.Value)
                    {
                        return false;
                    }
                }

                return true;
            }

            private enum CostNodeKind
            {
                Monomial,
                Max,
                Unknown,
                RecursiveUnknown,
            }
        }
    }
}
