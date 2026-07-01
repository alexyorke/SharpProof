using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    public sealed class SymbolicRuntimeHazardQueryService
    {
        private readonly SymbolicInvariantService _invariantService;

        public SymbolicRuntimeHazardQueryService()
            : this(new SymbolicInvariantService())
        {
        }

        public SymbolicRuntimeHazardQueryService(SymbolicInvariantService invariantService)
        {
            _invariantService = invariantService ?? throw new ArgumentNullException(nameof(invariantService));
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazards(
            string filePath,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazards(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsLine(
            string filePath,
            int line,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazardsLine(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                line,
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryFileRuntimeHazardsSpan(
            string filePath,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Source file does not exist.", filePath);
            }

            return QuerySourceRuntimeHazardsSpan(
                File.ReadAllText(filePath),
                Path.GetFullPath(filePath),
                spanStart,
                spanEnd,
                smtAnalysis,
                references,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazards(
            string sourceText,
            string filePath,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = CreateCompilation(sourceText, filePath, references, cancellationToken);
            return QuerySyntaxTreeRuntimeHazards(
                syntaxTree,
                compilation,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsLine(
            string sourceText,
            string filePath,
            int line,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = CreateCompilation(sourceText, filePath, references, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsLine(
                syntaxTree,
                compilation,
                line,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySourceRuntimeHazardsSpan(
            string sourceText,
            string filePath,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            IEnumerable<MetadataReference>? references = null,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var (syntaxTree, compilation) = CreateCompilation(sourceText, filePath, references, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsSpan(
                syntaxTree,
                compilation,
                spanStart,
                spanEnd,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazards(
            SyntaxTree syntaxTree,
            Compilation compilation,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                scope: null,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsLine(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int line,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var lineSpan = GetLineSpan(syntaxTree, line, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                lineSpan,
                line,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsSpan(
            SyntaxTree syntaxTree,
            Compilation compilation,
            int spanStart,
            int spanEnd,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null)
        {
            var sourceSpan = GetSourceSpan(syntaxTree, spanStart, spanEnd, cancellationToken);
            return QuerySyntaxTreeRuntimeHazardsCore(
                syntaxTree,
                compilation,
                sourceSpan,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options);
        }

        public SymbolicRuntimeHazardQueryResult QueryNodeRuntimeHazards(
            SyntaxNode node,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken = default,
            SymbolicRuntimeHazardQueryOptions? options = null,
            bool includeNestedCallables = false)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            return QueryRuntimeHazardsCore(
                node.SyntaxTree,
                semanticModel,
                node,
                scope: node.Span,
                requestedLine: null,
                smtAnalysis,
                cancellationToken,
                options,
                includeNestedCallables);
        }

        private SymbolicRuntimeHazardQueryResult QuerySyntaxTreeRuntimeHazardsCore(
            SyntaxTree syntaxTree,
            Compilation compilation,
            TextSpan? scope,
            int? requestedLine,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            SymbolicRuntimeHazardQueryOptions? options)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            options ??= SymbolicRuntimeHazardQueryOptions.Default;
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);
            return QueryRuntimeHazardsCore(
                syntaxTree,
                semanticModel,
                root,
                scope,
                requestedLine,
                smtAnalysis,
                cancellationToken,
                options,
                includeNestedCallables: true);
        }

        private SymbolicRuntimeHazardQueryResult QueryRuntimeHazardsCore(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            SyntaxNode root,
            TextSpan? scope,
            int? requestedLine,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            SymbolicRuntimeHazardQueryOptions? options,
            bool includeNestedCallables)
        {
            if (syntaxTree == null)
            {
                throw new ArgumentNullException(nameof(syntaxTree));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (smtAnalysis == null)
            {
                throw new ArgumentNullException(nameof(smtAnalysis));
            }

            options ??= SymbolicRuntimeHazardQueryOptions.Default;
            var hazards = EnumerateCandidates(root, semanticModel, cancellationToken, includeNestedCallables)
                .Where(candidate => scope == null || candidate.Site.Span.IntersectsWith(scope.Value))
                .Where(candidate => options.Includes(candidate.Kind))
                .Select(candidate => ClassifyCandidate(
                    syntaxTree,
                    semanticModel,
                    candidate,
                    smtAnalysis,
                    cancellationToken))
                .Where(hazard => options.IncludeUnprovenCandidates || hazard.Status == SymbolicRuntimeHazardStatus.Proven)
                .OrderBy(static hazard => hazard.SpanStart)
                .ThenBy(static hazard => hazard.Kind.ToString(), StringComparer.Ordinal)
                .ToArray();

            var sourceText = syntaxTree.GetText(cancellationToken);
            return new SymbolicRuntimeHazardQueryResult(
                syntaxTree.FilePath,
                sourceText.Lines.Count,
                scope?.Start,
                scope?.End,
                requestedLine,
                hazards,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        private SymbolicRuntimeHazard ClassifyCandidate(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            RuntimeHazardCandidate candidate,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken)
        {
            var analysis = _invariantService.AnalyzeAt(
                candidate.Site,
                semanticModel,
                smtAnalysis,
                cancellationToken);
            var triggerCondition = candidate.TriggerCondition;
            var exceptionType = candidate.ExceptionType;
            var category = candidate.Category;
            if (TryRefineThrowNullCandidate(
                    candidate,
                    analysis,
                    semanticModel,
                    smtAnalysis,
                    cancellationToken,
                    out var throwNullTrigger))
            {
                triggerCondition = throwNullTrigger;
                exceptionType = "System.NullReferenceException";
                category = "definite_throw_null";
            }

            var (status, reason) = ClassifyTrigger(
                analysis,
                triggerCondition,
                smtAnalysis);
            var lineColumn = GetLineAndColumn(syntaxTree, candidate.Site.SpanStart, cancellationToken);
            var sourceSpan = GetNodeSourceSpan(syntaxTree, candidate.Site.Span, cancellationToken);

            return new SymbolicRuntimeHazard(
                syntaxTree.FilePath,
                candidate.Kind,
                status,
                reason,
                exceptionType,
                category,
                candidate.Site.Kind().ToString(),
                candidate.Site.ToString(),
                candidate.Site.SpanStart,
                candidate.Site.Span.End,
                lineColumn.Line,
                lineColumn.Column,
                sourceSpan.StartLine,
                sourceSpan.StartColumn,
                sourceSpan.EndLine,
                sourceSpan.EndColumn,
                triggerCondition.ToString() ?? string.Empty,
                analysis.MergedInvariantText,
                analysis.Facts,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        private static bool TryRefineThrowNullCandidate(
            RuntimeHazardCandidate candidate,
            SymbolicProgramPointAnalysis analysis,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (candidate.Kind != SymbolicRuntimeHazardKind.DirectThrow ||
                !TryGetThrowExpression(candidate.Site, out var thrownExpression) ||
                !TryTranslateNullCondition(thrownExpression, semanticModel, cancellationToken, out var nullTrigger))
            {
                return false;
            }

            trigger = nullTrigger;
            return nullTrigger is SmtBooleanConstant { Value: true } ||
                smtAnalysis.PathConditionsImply(analysis.PathConditions, nullTrigger);
        }

        private static bool TryGetThrowExpression(SyntaxNode throwNode, out ExpressionSyntax expression)
        {
            switch (throwNode)
            {
                case ThrowStatementSyntax { Expression: { } statementExpression }:
                    expression = statementExpression;
                    return true;
                case ThrowExpressionSyntax throwExpression:
                    expression = throwExpression.Expression;
                    return true;
                default:
                    expression = null!;
                    return false;
            }
        }

        private static (SymbolicRuntimeHazardStatus Status, string Reason) ClassifyTrigger(
            SymbolicProgramPointAnalysis analysis,
            SmtFormula triggerCondition,
            SmtAnalysisService smtAnalysis)
        {
            if (analysis.Reachability == SymbolicReachability.Unreachable)
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, analysis.ReachabilityReason);
            }

            if (analysis.Reachability == SymbolicReachability.Unknown)
            {
                return (SymbolicRuntimeHazardStatus.Unknown, analysis.ReachabilityReason);
            }

            if (!smtAnalysis.Options.IsEnabled)
            {
                return (SymbolicRuntimeHazardStatus.Unsupported, "smt_disabled");
            }

            if (triggerCondition is SmtBooleanConstant { Value: true })
            {
                return (SymbolicRuntimeHazardStatus.Proven, "trigger_always_true");
            }

            if (triggerCondition is SmtBooleanConstant { Value: false })
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, "trigger_always_false");
            }

            var proven = smtAnalysis.ClassifyImplication(analysis.PathConditions, triggerCondition);
            if (proven.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return (SymbolicRuntimeHazardStatus.Proven, proven.Reason);
            }

            var disproven = smtAnalysis.ClassifyImplication(
                analysis.PathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, triggerCondition));
            if (disproven.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return (SymbolicRuntimeHazardStatus.Unreachable, disproven.Reason);
            }

            return (SymbolicRuntimeHazardStatus.Unknown, proven.Reason);
        }

        private static IEnumerable<RuntimeHazardCandidate> EnumerateCandidates(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeNestedCallables)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoTrivia: false,
                         descendIntoChildren: candidate =>
                             includeNestedCallables ||
                             ReferenceEquals(candidate, root) ||
                             !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var candidate in EnumerateCandidatesForNode(node, semanticModel, cancellationToken))
                {
                    var key = candidate.Kind + ":" + candidate.Site.SpanStart + ":" + candidate.Site.Span.End;
                    if (seen.Add(key))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        private static IEnumerable<RuntimeHazardCandidate> EnumerateCandidatesForNode(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            switch (node)
            {
                case ThrowStatementSyntax throwStatement:
                    yield return CreateThrowCandidate(throwStatement, semanticModel, cancellationToken);
                    break;
                case ThrowExpressionSyntax throwExpression:
                    yield return CreateThrowCandidate(throwExpression, semanticModel, cancellationToken);
                    break;
                case BinaryExpressionSyntax binaryExpression:
                    if (TryCreateDivideByZeroCandidate(binaryExpression, semanticModel, cancellationToken, out var divideCandidate))
                    {
                        yield return divideCandidate;
                    }

                    if (TryCreateCheckedIntegralOverflowCandidate(binaryExpression, semanticModel, cancellationToken, out var binaryOverflowCandidate))
                    {
                        yield return binaryOverflowCandidate;
                    }

                    break;
                case PrefixUnaryExpressionSyntax prefixUnaryExpression:
                    if (TryCreateCheckedIntegralOverflowCandidate(prefixUnaryExpression, semanticModel, cancellationToken, out var unaryOverflowCandidate))
                    {
                        yield return unaryOverflowCandidate;
                    }

                    break;
                case PostfixUnaryExpressionSyntax postfixUnaryExpression:
                    if (TryCreateCheckedIntegralOverflowCandidate(postfixUnaryExpression, semanticModel, cancellationToken, out var postfixOverflowCandidate))
                    {
                        yield return postfixOverflowCandidate;
                    }

                    break;
                case CastExpressionSyntax castExpression:
                    if (TryCreateCheckedExplicitNumericConversionOverflowCandidate(castExpression, semanticModel, cancellationToken, out var conversionOverflowCandidate))
                    {
                        yield return conversionOverflowCandidate;
                    }

                    if (TryCreateUnboxNullCastCandidate(castExpression, semanticModel, cancellationToken, out var unboxNullCandidate))
                    {
                        yield return unboxNullCandidate;
                    }

                    if (TryCreateInvalidCastCandidate(castExpression, semanticModel, cancellationToken, out var invalidCastCandidate))
                    {
                        yield return invalidCastCandidate;
                    }

                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    if (TryCreateNullableValueCandidate(memberAccess, semanticModel, cancellationToken, out var nullableCandidate))
                    {
                        yield return nullableCandidate;
                    }

                    if (memberAccess.Parent is not InvocationExpressionSyntax { Expression: var invocationExpression } ||
                        !ReferenceEquals(invocationExpression, memberAccess))
                    {
                        if (TryCreateDynamicNullBindingCandidate(
                                memberAccess,
                                memberAccess.Expression,
                                "definite_dynamic_member_null_binding",
                                semanticModel,
                                cancellationToken,
                                out var memberDynamicCandidate))
                        {
                            yield return memberDynamicCandidate;
                        }
                    }

                    if (TryCreateNullDereferenceCandidate(memberAccess, memberAccess.Expression, semanticModel, cancellationToken, out var memberNullCandidate))
                    {
                        yield return memberNullCandidate;
                    }

                    break;
                case ElementAccessExpressionSyntax elementAccess:
                    if (TryCreateDynamicNullBindingCandidate(
                            elementAccess,
                            elementAccess.Expression,
                            "definite_dynamic_index_null_binding",
                            semanticModel,
                            cancellationToken,
                            out var elementDynamicCandidate))
                    {
                        yield return elementDynamicCandidate;
                    }

                    if (TryCreateNullDereferenceCandidate(elementAccess, elementAccess.Expression, semanticModel, cancellationToken, out var elementNullCandidate))
                    {
                        yield return elementNullCandidate;
                    }

                    if (TryCreateIndexOrRangeCandidate(elementAccess, semanticModel, cancellationToken, out var indexCandidate))
                    {
                        yield return indexCandidate;
                    }

                    break;
                case AssignmentExpressionSyntax assignment:
                    if (TryCreateCompoundAssignmentDivideByZeroCandidate(assignment, semanticModel, cancellationToken, out var compoundDivideCandidate))
                    {
                        yield return compoundDivideCandidate;
                    }

                    if (TryCreateDeconstructionNullReceiverCandidate(assignment, semanticModel, cancellationToken, out var deconstructionNullCandidate))
                    {
                        yield return deconstructionNullCandidate;
                    }

                    if (TryCreateArrayTypeMismatchCandidate(assignment, semanticModel, cancellationToken, out var arrayTypeMismatchCandidate))
                    {
                        yield return arrayTypeMismatchCandidate;
                    }

                    if (TryCreateCheckedIntegralCompoundAssignmentOverflowCandidate(assignment, semanticModel, cancellationToken, out var compoundOverflowCandidate))
                    {
                        yield return compoundOverflowCandidate;
                    }

                    break;
                case ArrayCreationExpressionSyntax arrayCreation:
                    if (TryCreateNegativeArrayLengthCandidate(arrayCreation, semanticModel, cancellationToken, out var negativeLengthCandidate))
                    {
                        yield return negativeLengthCandidate;
                    }

                    break;
                case StackAllocArrayCreationExpressionSyntax stackAllocCreation:
                    if (TryCreateNegativeStackAllocLengthCandidate(
                            stackAllocCreation,
                            semanticModel,
                            cancellationToken,
                            out var negativeStackAllocLengthCandidate))
                    {
                        yield return negativeStackAllocLengthCandidate;
                    }

                    break;
                case SwitchExpressionSyntax switchExpression:
                    if (TryCreateSwitchExpressionNoMatchCandidate(
                            switchExpression,
                            semanticModel,
                            cancellationToken,
                            out var switchNoMatchCandidate))
                    {
                        yield return switchNoMatchCandidate;
                    }

                    break;
                case ForEachStatementSyntax forEachStatement:
                    if (TryCreateNullDereferenceCandidate(forEachStatement, forEachStatement.Expression, semanticModel, cancellationToken, out var foreachNullCandidate))
                    {
                        yield return foreachNullCandidate;
                    }

                    break;
                case ForEachVariableStatementSyntax forEachVariableStatement:
                    if (TryCreateNullDereferenceCandidate(forEachVariableStatement, forEachVariableStatement.Expression, semanticModel, cancellationToken, out var foreachVariableNullCandidate))
                    {
                        yield return foreachVariableNullCandidate;
                    }

                    break;
                case LockStatementSyntax lockStatement:
                    if (TryCreateArgumentNullCandidate(
                            lockStatement,
                            lockStatement.Expression,
                            "definite_lock_null",
                            semanticModel,
                            cancellationToken,
                            out var lockNullCandidate))
                    {
                        yield return lockNullCandidate;
                    }

                    break;
                case InvocationExpressionSyntax invocation:
                    if (TryCreateDynamicInvocationNullBindingCandidate(invocation, semanticModel, cancellationToken, out var invocationDynamicCandidate))
                    {
                        yield return invocationDynamicCandidate;
                    }

                    if (TryCreateArrayGetValueIndexOutOfRangeCandidate(invocation, semanticModel, cancellationToken, out var arrayGetValueCandidate))
                    {
                        yield return arrayGetValueCandidate;
                    }

                    if (TryCreateSlicingArgumentOutOfRangeCandidate(invocation, semanticModel, cancellationToken, out var slicingCandidate))
                    {
                        yield return slicingCandidate;
                    }

                    if (invocation.Expression is not MemberAccessExpressionSyntax &&
                        TryCreateNullDereferenceCandidate(invocation, invocation.Expression, semanticModel, cancellationToken, out var invocationNullCandidate))
                    {
                        yield return invocationNullCandidate;
                    }

                    break;
                case AwaitExpressionSyntax awaitExpression:
                    if (TryCreateAwaitNullDereferenceCandidate(awaitExpression, semanticModel, cancellationToken, out var awaitNullCandidate))
                    {
                        yield return awaitNullCandidate;
                    }

                    break;
                case WithExpressionSyntax withExpression:
                    if (TryCreateNullDereferenceCandidate(
                            withExpression,
                            withExpression.Expression,
                            "definite_with_null",
                            semanticModel,
                            cancellationToken,
                            out var withNullCandidate))
                    {
                        yield return withNullCandidate;
                    }

                    break;
            }
        }

        private static RuntimeHazardCandidate CreateThrowCandidate(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var exceptionType = GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
            var isRethrow = throwNode is ThrowStatementSyntax { Expression: null };
            return new RuntimeHazardCandidate(
                throwNode,
                isRethrow ? SymbolicRuntimeHazardKind.Rethrow : SymbolicRuntimeHazardKind.DirectThrow,
                new SmtBooleanConstant(true),
                exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty) ??
                    (isRethrow ? "unknown" : "System.Exception"),
                isRethrow ? "rethrow" : "direct_throw");
        }

        private static bool TryCreateDivideByZeroCandidate(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!binaryExpression.IsKind(SyntaxKind.DivideExpression) &&
                !binaryExpression.IsKind(SyntaxKind.ModuloExpression))
            {
                return false;
            }

            var rightType = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken).ConvertedType;
            if (!IsThrowingDivideByZeroType(rightType) ||
                !TryTranslateZeroCondition(binaryExpression.Right, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                binaryExpression,
                SymbolicRuntimeHazardKind.DivideByZero,
                trigger,
                "System.DivideByZeroException",
                binaryExpression.IsKind(SyntaxKind.ModuloExpression) ? "definite_modulo_by_zero" : "definite_divide_by_zero");
            return true;
        }

        private static bool TryCreateCheckedIntegralOverflowCandidate(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetCheckedIntegralBinaryOperator(
                    binaryExpression,
                    semanticModel,
                    cancellationToken,
                    out var smtOperator,
                    out var minValue,
                    out var maxValue))
            {
                return false;
            }

            var trigger = TryCreateCheckedIntegralBinaryOverflowTrigger(
                binaryExpression,
                smtOperator,
                minValue,
                maxValue,
                semanticModel,
                cancellationToken,
                out var overflowTrigger)
                ? overflowTrigger
                : CreateUnknownTrigger(binaryExpression, "checked_integral_overflow");

            candidate = new RuntimeHazardCandidate(
                binaryExpression,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
                trigger,
                "System.OverflowException",
                "definite_checked_integral_overflow");
            return true;
        }

        private static bool TryCreateCheckedIntegralOverflowCandidate(
            PrefixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            if (TryCreateCheckedIntegralUnaryMinusOverflowCandidate(
                    unaryExpression,
                    semanticModel,
                    cancellationToken,
                    out candidate))
            {
                return true;
            }

            return TryCreateCheckedIntegralUpdateOverflowCandidate(
                unaryExpression,
                unaryExpression.Operand,
                semanticModel,
                cancellationToken,
                out candidate);
        }

        private static bool TryCreateCheckedIntegralUnaryMinusOverflowCandidate(
            PrefixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetCheckedIntegralUnaryOperator(
                    unaryExpression,
                    semanticModel,
                    cancellationToken,
                    out var minValue,
                    out var maxValue))
            {
                return false;
            }

            var trigger = TryCreateCheckedIntegralUnaryOverflowTrigger(
                unaryExpression,
                minValue,
                maxValue,
                semanticModel,
                cancellationToken,
                out var overflowTrigger)
                ? overflowTrigger
                : CreateUnknownTrigger(unaryExpression, "checked_integral_overflow");

            candidate = new RuntimeHazardCandidate(
                unaryExpression,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
                trigger,
                "System.OverflowException",
                "definite_checked_integral_overflow");
            return true;
        }

        private static bool TryCreateCheckedIntegralOverflowCandidate(
            PostfixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            return TryCreateCheckedIntegralUpdateOverflowCandidate(
                unaryExpression,
                unaryExpression.Operand,
                semanticModel,
                cancellationToken,
                out candidate);
        }

        private static bool TryCreateCheckedIntegralUpdateOverflowCandidate(
            ExpressionSyntax updateExpression,
            ExpressionSyntax operand,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetCheckedIntegralIncrementOrDecrementOperator(
                    updateExpression,
                    operand,
                    semanticModel,
                    cancellationToken,
                    out var smtOperator,
                    out var minValue,
                    out var maxValue))
            {
                return false;
            }

            var trigger = TryCreateCheckedIntegralUpdateOverflowTrigger(
                operand,
                smtOperator,
                minValue,
                maxValue,
                semanticModel,
                cancellationToken,
                out var overflowTrigger)
                ? overflowTrigger
                : CreateUnknownTrigger(updateExpression, "checked_integral_overflow");

            candidate = new RuntimeHazardCandidate(
                updateExpression,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
                trigger,
                "System.OverflowException",
                "definite_checked_integral_overflow");
            return true;
        }

        private static bool TryCreateCheckedIntegralCompoundAssignmentOverflowCandidate(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetCheckedIntegralCompoundAssignmentOperator(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var smtOperator,
                    out var minValue,
                    out var maxValue))
            {
                return false;
            }

            var trigger = TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
                assignment,
                smtOperator,
                minValue,
                maxValue,
                semanticModel,
                cancellationToken,
                out var overflowTrigger)
                ? overflowTrigger
                : CreateUnknownTrigger(assignment, "checked_integral_overflow");

            candidate = new RuntimeHazardCandidate(
                assignment,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
                trigger,
                "System.OverflowException",
                "definite_checked_integral_overflow");
            return true;
        }

        private static bool TryCreateCompoundAssignmentDivideByZeroCandidate(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!assignment.IsKind(SyntaxKind.DivideAssignmentExpression) &&
                !assignment.IsKind(SyntaxKind.ModuloAssignmentExpression))
            {
                return false;
            }

            var rightType = semanticModel.GetTypeInfo(assignment.Right, cancellationToken).ConvertedType;
            if (!IsThrowingDivideByZeroType(rightType) ||
                !TryTranslateZeroCondition(assignment.Right, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                assignment,
                SymbolicRuntimeHazardKind.DivideByZero,
                trigger,
                "System.DivideByZeroException",
                assignment.IsKind(SyntaxKind.ModuloAssignmentExpression) ? "definite_modulo_by_zero" : "definite_divide_by_zero");
            return true;
        }

        private static bool TryCreateDeconstructionNullReceiverCandidate(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not TupleExpressionSyntax and not DeclarationExpressionSyntax)
            {
                return false;
            }

            var deconstructionInfo = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeconstructionInfo(semanticModel, assignment);
            if (deconstructionInfo.Method is not IMethodSymbol { IsStatic: false })
            {
                return false;
            }

            var receiver = assignment.Right;
            var receiverType = GetExpressionType(receiver, semanticModel, cancellationToken);
            if (IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
                !IsReferenceType(receiverType) ||
                !TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                assignment,
                SymbolicRuntimeHazardKind.NullDereference,
                trigger,
                "System.NullReferenceException",
                "definite_deconstruction_null");
            return true;
        }

        private static bool TryCreateArrayTypeMismatchCandidate(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess ||
                !TryGetArrayElementStoreType(elementAccess, semanticModel, cancellationToken, out var arrayType) ||
                !IsReferenceType(arrayType.ElementType))
            {
                return false;
            }

            var mismatchTrigger = TryCreateArrayStoreMismatchFormula(
                assignment,
                elementAccess,
                semanticModel,
                cancellationToken,
                out var mismatchFormula)
                ? mismatchFormula
                : CreateUnknownTrigger(assignment, "array_type_mismatch");
            var inRangeTrigger = CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                elementAccess,
                semanticModel,
                cancellationToken,
                out var inRangeFormula)
                ? inRangeFormula
                : CreateUnknownTrigger(elementAccess, "array_store_in_range");

            candidate = new RuntimeHazardCandidate(
                assignment,
                SymbolicRuntimeHazardKind.ArrayTypeMismatch,
                Conjoin(mismatchTrigger, inRangeTrigger),
                "System.ArrayTypeMismatchException",
                "definite_array_type_mismatch");
            return true;
        }

        private static bool TryCreateCheckedExplicitNumericConversionOverflowCandidate(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetCheckedExplicitNumericConversionRange(
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var minValue,
                    out var maxValue))
            {
                return false;
            }

            var trigger = TryCreateCheckedExplicitNumericConversionOverflowTrigger(
                castExpression,
                minValue,
                maxValue,
                semanticModel,
                cancellationToken,
                out var overflowTrigger)
                ? overflowTrigger
                : CreateUnknownTrigger(castExpression, "checked_numeric_conversion_overflow");

            candidate = new RuntimeHazardCandidate(
                castExpression,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
                trigger,
                "System.OverflowException",
                "definite_checked_numeric_conversion_overflow");
            return true;
        }

        private static bool TryCreateUnboxNullCastCandidate(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
                conversionOperation.Conversion.IsUserDefined ||
                !IsUnboxingCastShape(castExpression, conversionOperation.Type, semanticModel, cancellationToken))
            {
                return false;
            }

            var trigger = TryTranslateNullCondition(castExpression.Expression, semanticModel, cancellationToken, out var nullTrigger)
                ? nullTrigger
                : CreateUnknownTrigger(castExpression, "unbox_null");

            candidate = new RuntimeHazardCandidate(
                castExpression,
                SymbolicRuntimeHazardKind.UnboxNull,
                trigger,
                "System.NullReferenceException",
                "definite_unbox_null");
            return true;
        }

        private static bool TryCreateInvalidCastCandidate(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
                conversionOperation.Conversion.IsUserDefined ||
                conversionOperation.Conversion.IsIdentity ||
                conversionOperation.Type is not { } targetType ||
                targetType.TypeKind == TypeKind.Dynamic)
            {
                return false;
            }

            SmtFormula mismatchTrigger;
            if (IsUnboxingCastShape(castExpression, targetType, semanticModel, cancellationToken))
            {
                if (TryTranslateNullCondition(castExpression.Expression, semanticModel, cancellationToken, out var nullTrigger) &&
                    nullTrigger is SmtBooleanConstant { Value: true })
                {
                    return false;
                }

                mismatchTrigger = TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType)
                    ? new SmtBooleanConstant(!CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType))
                    : CreateUnknownTrigger(castExpression, "invalid_unbox_cast");
            }
            else
            {
                var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
                if (!IsReferenceType(targetType) ||
                    !IsReferenceType(operandType))
                {
                    return false;
                }

                if (TryTranslateNullCondition(castExpression.Expression, semanticModel, cancellationToken, out var nullTrigger) &&
                    nullTrigger is SmtBooleanConstant { Value: true })
                {
                    return false;
                }

                mismatchTrigger = TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType)
                    ? new SmtBooleanConstant(!CanCastExactRuntimeTypeToReferenceType(
                        exactRuntimeType,
                        targetType,
                        semanticModel.Compilation))
                    : TryCreateRuntimeReferenceCastMismatchTrigger(
                        castExpression.Expression,
                        targetType,
                        semanticModel,
                        cancellationToken,
                        out var runtimeMismatchTrigger)
                        ? runtimeMismatchTrigger
                        : CreateUnknownTrigger(castExpression, "invalid_reference_cast");
            }

            if (mismatchTrigger is SmtBooleanConstant { Value: false })
            {
                return false;
            }

            var trigger = Conjoin(
                CreateNonNullTrigger(castExpression.Expression, castExpression, semanticModel, cancellationToken),
                mismatchTrigger);
            if (trigger is SmtBooleanConstant { Value: false })
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                castExpression,
                SymbolicRuntimeHazardKind.InvalidCast,
                trigger,
                "System.InvalidCastException",
                "definite_invalid_cast");
            return true;
        }

        private static bool TryCreateNullDereferenceCandidate(
            SyntaxNode site,
            ExpressionSyntax receiver,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            return TryCreateNullDereferenceCandidate(
                site,
                receiver,
                "definite_null_dereference",
                semanticModel,
                cancellationToken,
                out candidate);
        }

        private static bool TryCreateAwaitNullDereferenceCandidate(
            AwaitExpressionSyntax awaitExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            return TryCreateNullDereferenceCandidate(
                awaitExpression,
                awaitExpression.Expression,
                "definite_await_null",
                semanticModel,
                cancellationToken,
                out candidate);
        }

        private static bool TryCreateNullDereferenceCandidate(
            SyntaxNode site,
            ExpressionSyntax receiver,
            string category,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var receiverType = GetExpressionType(receiver, semanticModel, cancellationToken);
            if (IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
                !IsReferenceType(receiverType) ||
                !TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                site,
                SymbolicRuntimeHazardKind.NullDereference,
                trigger,
                "System.NullReferenceException",
                category);
            return true;
        }

        private static bool TryCreateArgumentNullCandidate(
            SyntaxNode site,
            ExpressionSyntax expression,
            string category,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var expressionType = GetExpressionType(expression, semanticModel, cancellationToken);
            if (IsDynamicExpression(expression, semanticModel, cancellationToken) ||
                !IsReferenceType(expressionType) ||
                !TryTranslateNullCondition(expression, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                site,
                SymbolicRuntimeHazardKind.ArgumentNull,
                trigger,
                "System.ArgumentNullException",
                category);
            return true;
        }

        private static bool TryCreateDynamicInvocationNullBindingCandidate(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var expression = UnwrapExpression(invocation.Expression);
            var receiver = expression is MemberAccessExpressionSyntax memberAccess
                ? memberAccess.Expression
                : invocation.Expression;

            return TryCreateDynamicNullBindingCandidate(
                invocation,
                receiver,
                "definite_dynamic_invocation_null_binding",
                semanticModel,
                cancellationToken,
                out candidate);
        }

        private static bool TryCreateDynamicNullBindingCandidate(
            SyntaxNode site,
            ExpressionSyntax receiver,
            string category,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
                !TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                site,
                SymbolicRuntimeHazardKind.DynamicNullBinding,
                trigger,
                "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException",
                category);
            return true;
        }

        private static bool TryCreateNullableValueCandidate(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!IsNullableValueAccess(memberAccess, semanticModel, cancellationToken) ||
                !CSharpConditionToFormula.TryTranslateNullableHasValue(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                memberAccess,
                SymbolicRuntimeHazardKind.NullableValueWithoutValue,
                new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula),
                "System.InvalidOperationException",
                "definite_nullable_value_without_value");
            return true;
        }

        private static bool TryCreateIndexOrRangeCandidate(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (!TryGetIndexOrRangeHazardMetadata(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var kind,
                    out var exceptionType,
                    out var category) ||
                !CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                elementAccess,
                kind,
                new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula),
                exceptionType,
                category);
            return true;
        }

        private static bool TryCreateSlicingArgumentOutOfRangeCandidate(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                !TryGetSlicingInvocationShape(
                    invocationOperation,
                    out var sourceExpression,
                    out var startExpression,
                    out var countExpression,
                    out var oneArgumentUpperBoundIsInclusive,
                    out var category) ||
                !CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLength) ||
                sourceLength is not { Kind: SmtValueKind.Int } ||
                !CSharpConditionToFormula.TryTranslateValue(
                    startExpression,
                    semanticModel,
                    cancellationToken,
                    out var start,
                    getSymbolVersion: null) ||
                start is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            SmtFormula inRange;
            var startNonNegative = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                start,
                new SmtIntegerConstant(0));
            if (countExpression == null)
            {
                var upperBound = new SmtBinaryFormula(
                    oneArgumentUpperBoundIsInclusive
                        ? SmtBinaryOperator.LessThanOrEqual
                        : SmtBinaryOperator.LessThan,
                    start,
                    sourceLength);
                inRange = Conjoin(startNonNegative, upperBound);
            }
            else
            {
                if (!CSharpConditionToFormula.TryTranslateValue(
                        countExpression,
                        semanticModel,
                        cancellationToken,
                        out var count,
                        getSymbolVersion: null) ||
                    count is not { Kind: SmtValueKind.Int })
                {
                    return false;
                }

                var countNonNegative = new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    count,
                    new SmtIntegerConstant(0));
                var end = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, start, count);
                var endWithinLength = new SmtBinaryFormula(
                    SmtBinaryOperator.LessThanOrEqual,
                    end,
                    sourceLength);
                inRange = Conjoin(startNonNegative, Conjoin(countNonNegative, endWithinLength));
            }

            candidate = new RuntimeHazardCandidate(
                invocation,
                SymbolicRuntimeHazardKind.ArgumentOutOfRange,
                new SmtUnaryFormula(SmtUnaryOperator.Not, inRange),
                "System.ArgumentOutOfRangeException",
                category);
            return true;
        }

        private static bool TryCreateArrayGetValueIndexOutOfRangeCandidate(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                !IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Instance.Type is not IArrayTypeSymbol arrayType ||
                invocationOperation.Arguments.Length != arrayType.Rank)
            {
                return false;
            }

            SmtFormula? inRange = null;
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!TryGetInvocationArgumentExpression(invocationOperation, dimension, out var indexExpression) ||
                    !CSharpConditionToFormula.TryTranslateValue(
                        indexExpression,
                        semanticModel,
                        cancellationToken,
                        out var indexFormula,
                        getSymbolVersion: null) ||
                    indexFormula is not { Kind: SmtValueKind.Int } ||
                    !TryTranslateArrayGetValueDimensionLength(
                        receiverExpression,
                        arrayType,
                        dimension,
                        semanticModel,
                        cancellationToken,
                        out var lengthFormula) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
                    return false;
                }

                var lowerBound = new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    indexFormula,
                    new SmtIntegerConstant(0));
                var upperBound = new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    indexFormula,
                    lengthFormula);
                var dimensionInRange = Conjoin(lowerBound, upperBound);
                inRange = inRange == null ? dimensionInRange : Conjoin(inRange, dimensionInRange);
            }

            if (inRange == null)
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                invocation,
                SymbolicRuntimeHazardKind.IndexOutOfRange,
                new SmtUnaryFormula(SmtUnaryOperator.Not, inRange),
                "System.IndexOutOfRangeException",
                "definite_array_get_value_index_out_of_range");
            return true;
        }

        private static bool TryTranslateArrayGetValueDimensionLength(
            ExpressionSyntax receiverExpression,
            IArrayTypeSymbol arrayType,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula)
        {
            if (arrayType.Rank == 1 &&
                dimension == 0 &&
                CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula))
            {
                return true;
            }

            return CSharpConditionToFormula.TryTranslateArrayDimensionLengthValue(
                receiverExpression,
                dimension,
                semanticModel,
                cancellationToken,
                out lengthFormula);
        }

        private static bool TryCreateNegativeArrayLengthCandidate(
            ArrayCreationExpressionSyntax arrayCreation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var trigger = default(SmtFormula);
            foreach (var lengthExpression in GetArrayLengthExpressions(arrayCreation))
            {
                if (!TryTranslateNegativeCondition(lengthExpression, semanticModel, cancellationToken, out var negativeLength))
                {
                    continue;
                }

                trigger = trigger == null
                    ? negativeLength
                    : new SmtBinaryFormula(SmtBinaryOperator.Or, trigger, negativeLength);
            }

            if (trigger == null)
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                arrayCreation,
                SymbolicRuntimeHazardKind.NegativeArrayLength,
                trigger,
                "System.OverflowException",
                "definite_negative_array_length");
            return true;
        }

        private static bool TryCreateNegativeStackAllocLengthCandidate(
            StackAllocArrayCreationExpressionSyntax stackAllocCreation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var trigger = default(SmtFormula);
            foreach (var lengthExpression in GetStackAllocLengthExpressions(stackAllocCreation))
            {
                if (!TryTranslateNegativeCondition(lengthExpression, semanticModel, cancellationToken, out var negativeLength))
                {
                    continue;
                }

                trigger = trigger == null
                    ? negativeLength
                    : new SmtBinaryFormula(SmtBinaryOperator.Or, trigger, negativeLength);
            }

            if (trigger == null)
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                stackAllocCreation,
                SymbolicRuntimeHazardKind.NegativeStackAllocLength,
                trigger,
                "System.OverflowException",
                "definite_negative_stackalloc_length");
            return true;
        }

        private static bool TryCreateSwitchExpressionNoMatchCandidate(
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            SmtFormula? anyArmSelected = null;
            foreach (var arm in switchExpression.Arms)
            {
                if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition))
                {
                    return false;
                }

                anyArmSelected = anyArmSelected == null
                    ? armCondition
                    : Disjoin(anyArmSelected, armCondition);
            }

            if (anyArmSelected == null)
            {
                return false;
            }

            SmtFormula trigger = new SmtUnaryFormula(SmtUnaryOperator.Not, anyArmSelected);
            if (trigger is SmtBooleanConstant { Value: false })
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                switchExpression,
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
                trigger,
                "System.Runtime.CompilerServices.SwitchExpressionException",
                "definite_switch_expression_no_match");
            return true;
        }

        private static bool TryCreateCheckedIntegralBinaryOverflowTrigger(
            BinaryExpressionSyntax binaryExpression,
            SmtIntegerBinaryOperator smtOperator,
            long minValue,
            long maxValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftFormula,
                    getSymbolVersion: null) ||
                leftFormula is not { Kind: SmtValueKind.Int } ||
                !CSharpConditionToFormula.TryTranslateValue(
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion: null) ||
                rightFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerBinaryTerm(smtOperator, leftFormula, rightFormula);
            trigger = smtOperator == SmtIntegerBinaryOperator.Divide
                ? Conjoin(
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, leftFormula, new SmtIntegerConstant(minValue)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, rightFormula, new SmtIntegerConstant(-1)))
                : CreateIntegralOutOfRangeFormula(resultFormula, minValue, maxValue);
            return true;
        }

        private static bool TryCreateCheckedIntegralUnaryOverflowTrigger(
            PrefixUnaryExpressionSyntax unaryExpression,
            long minValue,
            long maxValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    unaryExpression.Operand,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion: null) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operandFormula);
            trigger = CreateIntegralOutOfRangeFormula(resultFormula, minValue, maxValue);
            return true;
        }

        private static bool TryCreateCheckedIntegralUpdateOverflowTrigger(
            ExpressionSyntax operand,
            SmtIntegerBinaryOperator smtOperator,
            long minValue,
            long maxValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    operand,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion: null) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerBinaryTerm(smtOperator, operandFormula, new SmtIntegerConstant(1));
            trigger = CreateIntegralOutOfRangeFormula(resultFormula, minValue, maxValue);
            return true;
        }

        private static bool TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
            AssignmentExpressionSyntax assignment,
            SmtIntegerBinaryOperator smtOperator,
            long minValue,
            long maxValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    assignment.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftFormula,
                    getSymbolVersion: null) ||
                leftFormula is not { Kind: SmtValueKind.Int } ||
                !CSharpConditionToFormula.TryTranslateValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion: null) ||
                rightFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = new SmtIntegerBinaryTerm(smtOperator, leftFormula, rightFormula);
            trigger = smtOperator == SmtIntegerBinaryOperator.Divide
                ? Conjoin(
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, leftFormula, new SmtIntegerConstant(minValue)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, rightFormula, new SmtIntegerConstant(-1)))
                : CreateIntegralOutOfRangeFormula(resultFormula, minValue, maxValue);
            return true;
        }

        private static bool TryCreateCheckedExplicitNumericConversionOverflowTrigger(
            CastExpressionSyntax castExpression,
            long minValue,
            long maxValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            trigger = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    castExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion: null) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            trigger = CreateIntegralOutOfRangeFormula(operandFormula, minValue, maxValue);
            return true;
        }

        private static bool TryGetCheckedIntegralBinaryOperator(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtIntegerBinaryOperator smtOperator,
            out long minValue,
            out long maxValue)
        {
            smtOperator = default;
            minValue = default;
            maxValue = default;

            if (!TryGetCheckedIntegralRange(binaryExpression, semanticModel, cancellationToken, out minValue, out maxValue) ||
                semanticModel.GetOperation(binaryExpression, cancellationToken) is not IBinaryOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                })
            {
                return false;
            }

            switch (binaryExpression.Kind())
            {
                case SyntaxKind.AddExpression:
                    smtOperator = SmtIntegerBinaryOperator.Add;
                    return true;
                case SyntaxKind.SubtractExpression:
                    smtOperator = SmtIntegerBinaryOperator.Subtract;
                    return true;
                case SyntaxKind.MultiplyExpression:
                    smtOperator = SmtIntegerBinaryOperator.Multiply;
                    return true;
                case SyntaxKind.DivideExpression when minValue < 0:
                    smtOperator = SmtIntegerBinaryOperator.Divide;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetCheckedIntegralUnaryOperator(
            PrefixUnaryExpressionSyntax unaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            minValue = default;
            maxValue = default;
            return unaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) &&
                TryGetCheckedIntegralRange(unaryExpression, semanticModel, cancellationToken, out minValue, out maxValue) &&
                semanticModel.GetOperation(unaryExpression, cancellationToken) is IUnaryOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                };
        }

        private static bool TryGetCheckedIntegralIncrementOrDecrementOperator(
            ExpressionSyntax updateExpression,
            ExpressionSyntax operand,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtIntegerBinaryOperator smtOperator,
            out long minValue,
            out long maxValue)
        {
            smtOperator = default;
            minValue = default;
            maxValue = default;

            if (semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                } operation)
            {
                return false;
            }

            var operandType = operation.Target.Type ?? semanticModel.GetTypeInfo(operand, cancellationToken).Type;
            if (!TryGetBoundedIntegralRange(operandType, out minValue, out maxValue))
            {
                return false;
            }

            switch (updateExpression.Kind())
            {
                case SyntaxKind.PreIncrementExpression:
                case SyntaxKind.PostIncrementExpression:
                    smtOperator = SmtIntegerBinaryOperator.Add;
                    return true;
                case SyntaxKind.PreDecrementExpression:
                case SyntaxKind.PostDecrementExpression:
                    smtOperator = SmtIntegerBinaryOperator.Subtract;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetCheckedIntegralCompoundAssignmentOperator(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtIntegerBinaryOperator smtOperator,
            out long minValue,
            out long maxValue)
        {
            smtOperator = default;
            minValue = default;
            maxValue = default;

            if (semanticModel.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                } operation)
            {
                return false;
            }

            var targetType = operation.Target.Type ?? semanticModel.GetTypeInfo(assignment.Left, cancellationToken).Type;
            if (!TryGetBoundedIntegralRange(targetType, out minValue, out maxValue))
            {
                return false;
            }

            switch (assignment.Kind())
            {
                case SyntaxKind.AddAssignmentExpression:
                    smtOperator = SmtIntegerBinaryOperator.Add;
                    return true;
                case SyntaxKind.SubtractAssignmentExpression:
                    smtOperator = SmtIntegerBinaryOperator.Subtract;
                    return true;
                case SyntaxKind.MultiplyAssignmentExpression:
                    smtOperator = SmtIntegerBinaryOperator.Multiply;
                    return true;
                case SyntaxKind.DivideAssignmentExpression when minValue < 0:
                    smtOperator = SmtIntegerBinaryOperator.Divide;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetCheckedExplicitNumericConversionRange(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            minValue = default;
            maxValue = default;
            if (semanticModel.GetOperation(castExpression, cancellationToken) is not IConversionOperation
                {
                    IsChecked: true,
                    Conversion:
                    {
                        Exists: true,
                        IsIdentity: false,
                        IsImplicit: false,
                        IsNumeric: true,
                        IsUserDefined: false,
                        MethodSymbol: null
                    }
                } ||
                !TryGetCheckedNumericConversionRange(
                    GetNaturalExpressionType(castExpression, semanticModel, cancellationToken),
                    out minValue,
                    out maxValue))
            {
                return false;
            }

            if (TryGetCheckedNumericConversionRange(
                    GetNaturalExpressionType(castExpression.Expression, semanticModel, cancellationToken),
                    out var sourceMinValue,
                    out var sourceMaxValue) &&
                sourceMinValue >= minValue &&
                sourceMaxValue <= maxValue)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetCheckedIntegralRange(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long minValue,
            out long maxValue)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
        }

        private static bool TryGetCheckedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static bool TryGetBoundedIntegralRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Char:
                    minValue = char.MinValue;
                    maxValue = char.MaxValue;
                    return true;
                case SpecialType.System_SByte:
                    minValue = sbyte.MinValue;
                    maxValue = sbyte.MaxValue;
                    return true;
                case SpecialType.System_Byte:
                    minValue = byte.MinValue;
                    maxValue = byte.MaxValue;
                    return true;
                case SpecialType.System_Int16:
                    minValue = short.MinValue;
                    maxValue = short.MaxValue;
                    return true;
                case SpecialType.System_UInt16:
                    minValue = ushort.MinValue;
                    maxValue = ushort.MaxValue;
                    return true;
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static bool TryGetCheckedNumericConversionRange(
            ITypeSymbol? typeSymbol,
            out long minValue,
            out long maxValue)
        {
            switch (typeSymbol?.SpecialType)
            {
                case SpecialType.System_Char:
                    minValue = char.MinValue;
                    maxValue = char.MaxValue;
                    return true;
                case SpecialType.System_SByte:
                    minValue = sbyte.MinValue;
                    maxValue = sbyte.MaxValue;
                    return true;
                case SpecialType.System_Byte:
                    minValue = byte.MinValue;
                    maxValue = byte.MaxValue;
                    return true;
                case SpecialType.System_Int16:
                    minValue = short.MinValue;
                    maxValue = short.MaxValue;
                    return true;
                case SpecialType.System_UInt16:
                    minValue = ushort.MinValue;
                    maxValue = ushort.MaxValue;
                    return true;
                case SpecialType.System_Int32:
                    minValue = int.MinValue;
                    maxValue = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    minValue = uint.MinValue;
                    maxValue = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    minValue = long.MinValue;
                    maxValue = long.MaxValue;
                    return true;
                default:
                    minValue = default;
                    maxValue = default;
                    return false;
            }
        }

        private static SmtFormula CreateIntegralOutOfRangeFormula(SmtFormula resultFormula, long minValue, long maxValue)
        {
            var lowerOverflow = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                resultFormula,
                new SmtIntegerConstant(minValue));
            var upperOverflow = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThan,
                resultFormula,
                new SmtIntegerConstant(maxValue));
            return new SmtBinaryFormula(SmtBinaryOperator.Or, lowerOverflow, upperOverflow);
        }

        private static bool TryGetArrayElementStoreType(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IArrayTypeSymbol arrayType)
        {
            arrayType = null!;
            var argumentCount = elementAccess.ArgumentList.Arguments.Count;
            if (argumentCount == 0 ||
                GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is not IArrayTypeSymbol candidate ||
                candidate.Rank != argumentCount)
            {
                return false;
            }

            arrayType = candidate;
            return true;
        }

        private static bool TryCreateArrayStoreMismatchFormula(
            AssignmentExpressionSyntax assignment,
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            if (TryTranslateNullCondition(assignment.Right, semanticModel, cancellationToken, out var isNullFormula))
            {
                if (isNullFormula is SmtBooleanConstant { Value: true })
                {
                    formula = new SmtBooleanConstant(false);
                    return true;
                }
            }
            else
            {
                isNullFormula = null!;
            }

            if (!TryGetExactRuntimeType(
                    elementAccess.Expression,
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeArrayType) ||
                exactRuntimeArrayType is not IArrayTypeSymbol exactArrayType ||
                exactArrayType.Rank != elementAccess.ArgumentList.Arguments.Count ||
                !IsReferenceType(exactArrayType.ElementType) ||
                !TryGetExactRuntimeType(
                    assignment.Right,
                    assignment,
                    semanticModel,
                    cancellationToken,
                    out var exactAssignedType))
            {
                formula = isNullFormula == null
                    ? CreateUnknownTrigger(assignment, "array_type_mismatch")
                    : Conjoin(
                        new SmtUnaryFormula(SmtUnaryOperator.Not, isNullFormula),
                        CreateUnknownTrigger(assignment, "array_type_mismatch"));
                return true;
            }

            formula = new SmtBooleanConstant(!CanStoreExactRuntimeTypeInArrayElement(
                exactAssignedType,
                exactArrayType.ElementType,
                semanticModel.Compilation));
            return true;
        }

        private static bool TryGetExactRuntimeType(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ITypeSymbol exactType,
            int inlineDepth = 0)
        {
            exactType = null!;
            if (inlineDepth > 8)
            {
                return false;
            }

            expression = UnwrapExpression(expression);
            if (TryResolveCurrentSimpleValueExpression(
                    expression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var currentValueExpression))
            {
                return TryGetExactRuntimeType(
                    currentValueExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out exactType,
                    inlineDepth + 1);
            }

            var expressionType = GetNaturalExpressionType(expression, semanticModel, cancellationToken);
            if (expressionType != null && IsNonNullableValueType(expressionType))
            {
                exactType = expressionType;
                return true;
            }

            if (expressionType?.TypeKind == TypeKind.Dynamic)
            {
                return false;
            }

            if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
                ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax)
            {
                if (expressionType != null && !expressionType.IsAbstract)
                {
                    exactType = expressionType;
                    return true;
                }

                return false;
            }

            if (expression.IsKind(SyntaxKind.StringLiteralExpression) &&
                expressionType?.SpecialType == SpecialType.System_String)
            {
                exactType = expressionType;
                return true;
            }

            return false;
        }

        private static bool TryResolveCurrentSimpleValueExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMayMutateSymbol(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                            !ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken)
                                ? assignment.Right
                                : null;
                        continue;
                    }

                    if (StatementMayMutateSymbol(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(SyntaxNode node)
        {
            for (SyntaxNode? current = node; current != null; current = current.Parent)
            {
                if (current is StatementSyntax statement &&
                    current.Parent is BlockSyntax block)
                {
                    yield return (block, statement);
                }
            }
        }

        private static bool StatementMayMutateSymbol(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in statement.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var unary in statement.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     unary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                    ExpressionMatchesSymbol(unary.Operand, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var unary in statement.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     unary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                    ExpressionMatchesSymbol(unary.Operand, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var argument in statement.DescendantNodes().OfType<ArgumentSyntax>())
            {
                if (!argument.RefOrOutKeyword.IsKind(SyntaxKind.None) &&
                    ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var expression in node.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                if (ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionMatchesSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is { } expressionSymbol &&
                SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol);
        }

        private static ISymbol? GetLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            return symbol is ILocalSymbol or IParameterSymbol
                ? symbol
                : null;
        }

        private static ITypeSymbol? GetNaturalExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static bool CanStoreExactRuntimeTypeInArrayElement(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol elementType,
            Compilation compilation)
        {
            if (exactRuntimeType.TypeKind == TypeKind.Dynamic ||
                elementType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            var conversion = compilation.ClassifyCommonConversion(exactRuntimeType, elementType);
            return conversion.Exists &&
                (conversion.IsIdentity || conversion.IsImplicit);
        }

        private static bool CanUnboxExactRuntimeTypeToValueType(ITypeSymbol exactRuntimeType, ITypeSymbol targetType)
        {
            if (!IsNonNullableValueType(targetType))
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(exactRuntimeType, targetType);
        }

        private static bool CanCastExactRuntimeTypeToReferenceType(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol targetType,
            Compilation compilation)
        {
            if (targetType.TypeKind == TypeKind.Dynamic ||
                exactRuntimeType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            if (IsReferenceType(targetType) &&
                targetType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            var conversion = compilation.ClassifyCommonConversion(exactRuntimeType, targetType);
            return conversion.Exists &&
                (conversion.IsIdentity || conversion.IsImplicit);
        }

        private static SmtFormula Conjoin(SmtFormula left, SmtFormula right)
        {
            if (left is SmtBooleanConstant leftConstant)
            {
                return leftConstant.Value ? right : left;
            }

            if (right is SmtBooleanConstant rightConstant)
            {
                return rightConstant.Value ? left : right;
            }

            return new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
        }

        private static SmtFormula Disjoin(SmtFormula left, SmtFormula right)
        {
            if (left is SmtBooleanConstant leftConstant)
            {
                return leftConstant.Value ? left : right;
            }

            if (right is SmtBooleanConstant rightConstant)
            {
                return rightConstant.Value ? right : left;
            }

            return new SmtBinaryFormula(SmtBinaryOperator.Or, left, right);
        }

        private static SmtFormula CreateNonNullTrigger(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return TryTranslateNullCondition(expression, semanticModel, cancellationToken, out var nullTrigger)
                ? new SmtUnaryFormula(SmtUnaryOperator.Not, nullTrigger)
                : CreateUnknownTrigger(site, "cast_operand_not_null");
        }

        private static bool TryCreateRuntimeReferenceCastMismatchTrigger(
            ExpressionSyntax expression,
            ITypeSymbol targetType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    getSymbolVersion: null) ||
                value is not { Kind: SmtValueKind.Reference } ||
                !CSharpConditionToFormula.TryCreateRuntimeTypeTestFormula(value, targetType, out var runtimeTypeTest))
            {
                trigger = null!;
                return false;
            }

            trigger = new SmtUnaryFormula(SmtUnaryOperator.Not, runtimeTypeTest);
            return true;
        }

        private static SmtFormula CreateUnknownTrigger(SyntaxNode site, string name)
        {
            return new SmtVariable(
                name + "#" + site.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "_" + site.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SmtValueKind.Bool);
        }

        private static bool TryTranslateZeroCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true } constant)
            {
                if (IsIntegralOrDecimalZero(constant.Value))
                {
                    trigger = new SmtBooleanConstant(true);
                    return true;
                }

                if (constant.Value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal)
                {
                    trigger = new SmtBooleanConstant(false);
                    return true;
                }
            }

            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    getSymbolVersion: null) ||
                value is not { Kind: SmtValueKind.Int })
            {
                trigger = null!;
                return false;
            }

            trigger = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0));
            return true;
        }

        private static bool TryTranslateNegativeCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    getSymbolVersion: null) ||
                value is not { Kind: SmtValueKind.Int })
            {
                trigger = null!;
                return false;
            }

            trigger = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, new SmtIntegerConstant(0));
            return true;
        }

        private static bool TryTranslateNullCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula trigger)
        {
            expression = UnwrapExpression(expression);
            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                trigger = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is DefaultExpressionSyntax defaultExpression &&
                IsReferenceLikeType(GetExpressionType(defaultExpression, semanticModel, cancellationToken)))
            {
                trigger = new SmtBooleanConstant(true);
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    getSymbolVersion: null) ||
                value is not { Kind: SmtValueKind.Reference })
            {
                trigger = null!;
                return false;
            }

            trigger = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtNullConstant());
            return true;
        }

        private static bool IsBuiltInSequenceElementAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var argumentCount = elementAccess.ArgumentList.Arguments.Count;
            if (argumentCount == 0)
            {
                return false;
            }

            var receiverType = GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
            if (receiverType is IArrayTypeSymbol arrayType)
            {
                return arrayType.Rank == argumentCount;
            }

            return argumentCount == 1 &&
                (receiverType?.SpecialType == SpecialType.System_String ||
                 IsBuiltInSpanType(receiverType));
        }

        private static bool TryGetIndexOrRangeHazardMetadata(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicRuntimeHazardKind kind,
            out string exceptionType,
            out string category)
        {
            kind = default;
            exceptionType = string.Empty;
            category = string.Empty;

            if (IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken))
            {
                var isRange = elementAccess.ArgumentList.Arguments.Count == 1 &&
                    IsBuiltInRangeAccessArgument(
                        elementAccess.ArgumentList.Arguments[0].Expression,
                        semanticModel,
                        cancellationToken);
                if (isRange)
                {
                    kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
                    exceptionType = "System.ArgumentOutOfRangeException";
                    category = "definite_range_out_of_range";
                    return true;
                }

                kind = SymbolicRuntimeHazardKind.IndexOutOfRange;
                exceptionType = "System.IndexOutOfRangeException";
                category = "definite_index_out_of_range";
                return true;
            }

            if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken))
            {
                kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
                exceptionType = "System.ArgumentOutOfRangeException";
                category = "definite_count_index_out_of_range";
                return true;
            }

            return false;
        }

        private static bool TryGetSlicingInvocationShape(
            IInvocationOperation invocationOperation,
            out ExpressionSyntax sourceExpression,
            out ExpressionSyntax startExpression,
            out ExpressionSyntax? countExpression,
            out bool oneArgumentUpperBoundIsInclusive,
            out string category)
        {
            sourceExpression = null!;
            startExpression = null!;
            countExpression = null;
            oneArgumentUpperBoundIsInclusive = true;
            category = string.Empty;

            var method = invocationOperation.TargetMethod;
            if (TryGetMemoryExtensionsAsSpanSlicingShape(
                    invocationOperation,
                    method,
                    out sourceExpression,
                    out startExpression,
                    out countExpression))
            {
                category = "definite_memory_extensions_as_span_out_of_range";
                return true;
            }

            if (method.IsStatic ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax instanceExpression ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var firstArgument))
            {
                return false;
            }

            if (IsStringSubstringInvocation(method))
            {
                sourceExpression = instanceExpression;
                startExpression = firstArgument;
                oneArgumentUpperBoundIsInclusive = true;
                category = "definite_string_substring_out_of_range";
                return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
            }

            if (IsStringRemoveInvocation(method))
            {
                sourceExpression = instanceExpression;
                startExpression = firstArgument;
                category = "definite_string_remove_out_of_range";
                if (!TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression))
                {
                    return false;
                }

                oneArgumentUpperBoundIsInclusive = countExpression != null;
                return true;
            }

            if (IsBuiltInSpanOrMemorySliceInvocation(method))
            {
                sourceExpression = instanceExpression;
                startExpression = firstArgument;
                oneArgumentUpperBoundIsInclusive = true;
                category = "definite_slice_out_of_range";
                return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
            }

            return false;
        }

        private static bool TryGetMemoryExtensionsAsSpanSlicingShape(
            IInvocationOperation invocationOperation,
            IMethodSymbol method,
            out ExpressionSyntax sourceExpression,
            out ExpressionSyntax startExpression,
            out ExpressionSyntax? countExpression)
        {
            sourceExpression = null!;
            startExpression = null!;
            countExpression = null;

            if (!IsMemoryExtensionsAsSpanInvocation(method))
            {
                return false;
            }

            if (!TryGetMemoryExtensionsAsSpanSourceExpression(invocationOperation, out sourceExpression))
            {
                return false;
            }

            var intArguments = invocationOperation.Arguments
                .Where(static argument => argument.Parameter?.Type.SpecialType == SpecialType.System_Int32)
                .Select(static argument => argument.Value.Syntax)
                .OfType<ExpressionSyntax>()
                .ToArray();
            if (intArguments.Length is not (1 or 2))
            {
                return false;
            }

            startExpression = intArguments[0];
            countExpression = intArguments.Length == 2 ? intArguments[1] : null;
            return true;
        }

        private static bool TryGetMemoryExtensionsAsSpanSourceExpression(
            IInvocationOperation invocationOperation,
            out ExpressionSyntax sourceExpression)
        {
            if (invocationOperation.Instance?.Syntax is ExpressionSyntax instanceExpression &&
                IsMemoryExtensionsAsSpanSourceType(invocationOperation.Instance.Type))
            {
                sourceExpression = instanceExpression;
                return true;
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                if ((argument.Parameter?.Ordinal == 0 ||
                     IsMemoryExtensionsAsSpanSourceType(argument.Value.Type)) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression &&
                    IsMemoryExtensionsAsSpanSourceType(argument.Value.Type))
                {
                    sourceExpression = argumentExpression;
                    return true;
                }
            }

            sourceExpression = null!;
            return false;
        }

        private static bool TryGetOptionalSecondIntArgument(
            IInvocationOperation invocationOperation,
            IMethodSymbol method,
            out ExpressionSyntax? secondArgument)
        {
            secondArgument = null;
            if (method.Parameters.Length == 1)
            {
                return invocationOperation.Arguments.Length == 1 &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
            }

            if (method.Parameters.Length != 2 ||
                invocationOperation.Arguments.Length != 2 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
                method.Parameters[1].Type.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            return TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 1, out secondArgument);
        }

        private static bool TryGetInvocationArgumentExpression(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.Ordinal == parameterIndex &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            expression = null!;
            return false;
        }

        private static bool IsStringSubstringInvocation(IMethodSymbol method)
        {
            return method.Name == "Substring" &&
                method.ContainingType?.SpecialType == SpecialType.System_String &&
                method.ReturnType.SpecialType == SpecialType.System_String &&
                (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
                method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
        }

        private static bool IsStringRemoveInvocation(IMethodSymbol method)
        {
            return method.Name == "Remove" &&
                method.ContainingType?.SpecialType == SpecialType.System_String &&
                method.ReturnType.SpecialType == SpecialType.System_String &&
                (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
                method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
        }

        private static bool IsBuiltInSpanOrMemorySliceInvocation(IMethodSymbol method)
        {
            return method.Name == "Slice" &&
                (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
                method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) &&
                IsBuiltInSpanOrMemoryType(method.ContainingType) &&
                IsBuiltInSpanOrMemoryType(method.ReturnType);
        }

        private static bool IsMemoryExtensionsAsSpanInvocation(IMethodSymbol method)
        {
            return method.Name == "AsSpan" &&
                method.ContainingType?.OriginalDefinition.ToDisplayString() == "System.MemoryExtensions" &&
                IsBuiltInSpanType(method.ReturnType) &&
                method.Parameters.Count(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) is 1 or 2 &&
                method.Parameters.Any(static parameter => IsMemoryExtensionsAsSpanSourceType(parameter.Type));
        }

        private static bool IsMemoryExtensionsAsSpanSourceType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.SpecialType == SpecialType.System_String ||
                typeSymbol is IArrayTypeSymbol;
        }

        private static bool IsArrayGetValueInvocation(IMethodSymbol method)
        {
            return method.Name == "GetValue" &&
                !method.IsStatic &&
                method.ContainingType?.SpecialType == SpecialType.System_Array &&
                method.ReturnType.SpecialType == SpecialType.System_Object &&
                method.Parameters.Length > 0 &&
                method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
        }

        private static bool IsCountBackedIntIndexerElementAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var argumentType = GetExpressionType(
                elementAccess.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken);
            if (argumentType?.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var receiverType = GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
            return HasInstanceInt32Member(receiverType, "Count") &&
                HasInt32Indexer(receiverType);
        }

        private static bool HasInstanceInt32Member(ITypeSymbol? typeSymbol, string memberName)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            {
                if (HasDeclaredInstanceInt32Member(current, memberName))
                {
                    return true;
                }
            }

            foreach (var interfaceType in typeSymbol.AllInterfaces)
            {
                if (HasDeclaredInstanceInt32Member(interfaceType, memberName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDeclaredInstanceInt32Member(ITypeSymbol typeSymbol, string memberName)
        {
            foreach (var member in typeSymbol.GetMembers(memberName))
            {
                if (member.IsStatic)
                {
                    continue;
                }

                switch (member)
                {
                    case IPropertySymbol { Parameters.Length: 0, Type.SpecialType: SpecialType.System_Int32 }:
                    case IFieldSymbol { Type.SpecialType: SpecialType.System_Int32 }:
                        return true;
                }
            }

            return false;
        }

        private static bool HasInt32Indexer(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            {
                if (HasDeclaredInt32Indexer(current))
                {
                    return true;
                }
            }

            foreach (var interfaceType in typeSymbol.AllInterfaces)
            {
                if (HasDeclaredInt32Indexer(interfaceType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDeclaredInt32Indexer(ITypeSymbol typeSymbol)
        {
            foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsIndexer: true, IsStatic: false, Parameters.Length: 1 } &&
                    property.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBuiltInRangeAccessArgument(
            ExpressionSyntax argumentExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            argumentExpression = UnwrapExpression(argumentExpression);
            if (argumentExpression is RangeExpressionSyntax)
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
            return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
        }

        private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        private static bool IsBuiltInMemoryType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>";
        }

        private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
        {
            return IsBuiltInSpanType(typeSymbol) ||
                IsBuiltInMemoryType(typeSymbol);
        }

        private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Range",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        private static bool IsNullableValueAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return memberAccess.Name.Identifier.ValueText == "Value" &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol
                {
                    Name: "Value",
                    ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                };
        }

        private static bool IsUnboxingCastShape(
            CastExpressionSyntax castExpression,
            ITypeSymbol? targetType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            return IsNonNullableValueType(targetType) &&
                IsReferenceType(operandType);
        }

        private static bool TryGetConversionOperation(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IConversionOperation conversionOperation)
        {
            if (semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation operation)
            {
                conversionOperation = operation;
                return true;
            }

            conversionOperation = null!;
            return false;
        }

        private static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIntegralOrDecimalZero(object? value)
        {
            switch (value)
            {
                case byte byteValue:
                    return byteValue == 0;
                case sbyte sbyteValue:
                    return sbyteValue == 0;
                case short shortValue:
                    return shortValue == 0;
                case ushort ushortValue:
                    return ushortValue == 0;
                case int intValue:
                    return intValue == 0;
                case uint uintValue:
                    return uintValue == 0;
                case long longValue:
                    return longValue == 0L;
                case ulong ulongValue:
                    return ulongValue == 0UL;
                case decimal decimalValue:
                    return decimalValue == 0m;
                default:
                    return false;
            }
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol is ITypeParameterSymbol typeParameter)
            {
                return IsKnownReferenceTypeParameter(
                    typeParameter,
                    new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
            }

            return typeSymbol.IsReferenceType;
        }

        private static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.TypeKind == TypeKind.Dynamic ||
                IsReferenceType(typeSymbol);
        }

        private static bool IsDynamicExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.Type?.TypeKind == TypeKind.Dynamic ||
                typeInfo.ConvertedType?.TypeKind == TypeKind.Dynamic;
        }

        private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is { IsValueType: true, TypeKind: not TypeKind.TypeParameter } &&
                typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
        }

        private static bool IsKnownReferenceTypeParameter(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
            {
                return false;
            }

            if (typeParameter.HasReferenceTypeConstraint)
            {
                return true;
            }

            return typeParameter.ConstraintTypes.Any(constraint =>
                constraint.IsReferenceType ||
                constraint is ITypeParameterSymbol nestedTypeParameter &&
                IsKnownReferenceTypeParameter(nestedTypeParameter, visited));
        }

        private static ITypeSymbol? GetExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        private static IEnumerable<ExpressionSyntax> GetArrayLengthExpressions(ArrayCreationExpressionSyntax arrayCreation)
        {
            foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers)
            {
                foreach (var size in rankSpecifier.Sizes)
                {
                    if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    {
                        yield return size;
                    }
                }
            }
        }

        private static IEnumerable<ExpressionSyntax> GetStackAllocLengthExpressions(StackAllocArrayCreationExpressionSyntax stackAllocCreation)
        {
            if (stackAllocCreation.Type is not ArrayTypeSyntax arrayType)
            {
                yield break;
            }

            foreach (var rankSpecifier in arrayType.RankSpecifiers)
            {
                foreach (var size in rankSpecifier.Sizes)
                {
                    if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    {
                        yield return size;
                    }
                }
            }
        }

        private static ITypeSymbol? GetThrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax? exceptionExpression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax expression => expression.Expression,
                _ => null
            };

            if (exceptionExpression == null)
            {
                return GetRethrownExceptionType(throwNode, semanticModel, cancellationToken);
            }

            var typeInfo = semanticModel.GetTypeInfo(exceptionExpression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static ITypeSymbol? GetRethrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var catchClause in throwNode.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (!catchClause.Block.Span.Contains(throwNode.SpanStart) ||
                    catchClause.Declaration == null)
                {
                    continue;
                }

                return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            }

            return null;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case CastExpressionSyntax castExpression:
                        expression = castExpression.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax postfixUnary
                        when postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = postfixUnary.Operand;
                        continue;
                    default:
                        return expression;
                }
            }
        }

        private static (SyntaxTree SyntaxTree, Compilation Compilation) CreateCompilation(
            string sourceText,
            string filePath,
            IEnumerable<MetadataReference>? references,
            CancellationToken cancellationToken)
        {
            if (sourceText == null)
            {
                throw new ArgumentNullException(nameof(sourceText));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = "PurelySharp.Symbolic.RuntimeHazards.cs";
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                new CSharpParseOptions(LanguageVersion.Preview),
                filePath,
                cancellationToken: cancellationToken);
            var referenceArray = references?.ToImmutableArray() ?? SymbolicSourceQueryService.GetTrustedPlatformReferences();
            var compilation = CSharpCompilation.Create(
                "PurelySharp.Symbolic.RuntimeHazards",
                new[] { syntaxTree },
                referenceArray,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return (syntaxTree, compilation);
        }

        private static TextSpan GetLineSpan(SyntaxTree syntaxTree, int line, CancellationToken cancellationToken)
        {
            if (line < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line must be 1 or greater.");
            }

            var text = syntaxTree.GetText(cancellationToken);
            if (line > text.Lines.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "--line exceeds the file line count.");
            }

            return text.Lines[line - 1].Span;
        }

        private static TextSpan GetSourceSpan(
            SyntaxTree syntaxTree,
            int spanStart,
            int spanEnd,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            if (spanStart < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spanStart), "--span-start must be zero or greater.");
            }

            if (spanEnd < spanStart)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end cannot be less than --span-start.");
            }

            if (spanEnd > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(spanEnd), "--span-end exceeds the source text length.");
            }

            return TextSpan.FromBounds(spanStart, spanEnd);
        }

        private static LineColumn GetLineAndColumn(
            SyntaxTree syntaxTree,
            int position,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            var line = text.Lines.GetLineFromPosition(position);
            return new LineColumn(line.LineNumber + 1, position - line.Start + 1);
        }

        private static NodeSourceSpan GetNodeSourceSpan(
            SyntaxTree syntaxTree,
            TextSpan span,
            CancellationToken cancellationToken)
        {
            var text = syntaxTree.GetText(cancellationToken);
            var startLine = text.Lines.GetLineFromPosition(span.Start);
            var endLine = text.Lines.GetLineFromPosition(span.End);
            return new NodeSourceSpan(
                startLine.LineNumber + 1,
                span.Start - startLine.Start + 1,
                endLine.LineNumber + 1,
                span.End - endLine.Start + 1);
        }

        private readonly struct RuntimeHazardCandidate
        {
            public RuntimeHazardCandidate(
                SyntaxNode site,
                SymbolicRuntimeHazardKind kind,
                SmtFormula triggerCondition,
                string exceptionType,
                string category)
            {
                Site = site;
                Kind = kind;
                TriggerCondition = triggerCondition;
                ExceptionType = exceptionType;
                Category = category;
            }

            public SyntaxNode Site { get; }

            public SymbolicRuntimeHazardKind Kind { get; }

            public SmtFormula TriggerCondition { get; }

            public string ExceptionType { get; }

            public string Category { get; }
        }

        private readonly struct LineColumn
        {
            public LineColumn(int line, int column)
            {
                Line = line;
                Column = column;
            }

            public int Line { get; }

            public int Column { get; }
        }

        private readonly struct NodeSourceSpan
        {
            public NodeSourceSpan(
                int startLine,
                int startColumn,
                int endLine,
                int endColumn)
            {
                StartLine = startLine;
                StartColumn = startColumn;
                EndLine = endLine;
                EndColumn = endColumn;
            }

            public int StartLine { get; }

            public int StartColumn { get; }

            public int EndLine { get; }

            public int EndColumn { get; }
        }
    }

    public sealed class SymbolicRuntimeHazardQueryOptions
    {
        public static readonly SymbolicRuntimeHazardQueryOptions Default = new();

        public SymbolicRuntimeHazardQueryOptions(
            bool includeUnprovenCandidates = false,
            IEnumerable<SymbolicRuntimeHazardKind>? kinds = null)
        {
            IncludeUnprovenCandidates = includeUnprovenCandidates;
            Kinds = kinds?.ToImmutableHashSet() ?? ImmutableHashSet<SymbolicRuntimeHazardKind>.Empty;
        }

        public bool IncludeUnprovenCandidates { get; }

        public ImmutableHashSet<SymbolicRuntimeHazardKind> Kinds { get; }

        public bool Includes(SymbolicRuntimeHazardKind kind)
        {
            return Kinds.Count == 0 || Kinds.Contains(kind);
        }
    }

    public sealed class SymbolicRuntimeHazardQueryResult
    {
        public SymbolicRuntimeHazardQueryResult(
            string filePath,
            int lineCount,
            int? scopeStart,
            int? scopeEnd,
            int? line,
            IReadOnlyList<SymbolicRuntimeHazard> hazards,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            FilePath = filePath;
            LineCount = lineCount;
            ScopeStart = scopeStart;
            ScopeEnd = scopeEnd;
            Line = line;
            Hazards = hazards ?? throw new ArgumentNullException(nameof(hazards));
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public int LineCount { get; }

        public int? ScopeStart { get; }

        public int? ScopeEnd { get; }

        public int? Line { get; }

        public IReadOnlyList<SymbolicRuntimeHazard> Hazards { get; }

        public int HazardCount => Hazards.Count;

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    }

    public sealed class SymbolicRuntimeHazard
    {
        public SymbolicRuntimeHazard(
            string filePath,
            SymbolicRuntimeHazardKind kind,
            SymbolicRuntimeHazardStatus status,
            string statusReason,
            string exceptionType,
            string category,
            string nodeKind,
            string operationText,
            int spanStart,
            int spanEnd,
            int line,
            int column,
            int nodeStartLine,
            int nodeStartColumn,
            int nodeEndLine,
            int nodeEndColumn,
            string triggerCondition,
            string mergedInvariantText,
            IReadOnlyList<string> pathConditions,
            SymbolicReachability reachability,
            string reachabilityReason,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            FilePath = filePath;
            Kind = kind;
            Status = status;
            StatusReason = statusReason;
            ExceptionType = exceptionType;
            Category = category;
            NodeKind = nodeKind;
            OperationText = operationText;
            SpanStart = spanStart;
            SpanEnd = spanEnd;
            SpanLength = spanEnd - spanStart;
            Line = line;
            Column = column;
            NodeStartLine = nodeStartLine;
            NodeStartColumn = nodeStartColumn;
            NodeEndLine = nodeEndLine;
            NodeEndColumn = nodeEndColumn;
            TriggerCondition = triggerCondition;
            MergedInvariantText = mergedInvariantText;
            PathConditions = pathConditions ?? throw new ArgumentNullException(nameof(pathConditions));
            PathConditionCount = pathConditions.Count;
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public string FilePath { get; }

        public SymbolicRuntimeHazardKind Kind { get; }

        public SymbolicRuntimeHazardStatus Status { get; }

        public string StatusReason { get; }

        public string ExceptionType { get; }

        public string Category { get; }

        public string NodeKind { get; }

        public string OperationText { get; }

        public int SpanStart { get; }

        public int SpanEnd { get; }

        public int SpanLength { get; }

        public int Line { get; }

        public int Column { get; }

        public int NodeStartLine { get; }

        public int NodeStartColumn { get; }

        public int NodeEndLine { get; }

        public int NodeEndColumn { get; }

        public string TriggerCondition { get; }

        public string MergedInvariantText { get; }

        public IReadOnlyList<string> PathConditions { get; }

        public int PathConditionCount { get; }

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    }

    public enum SymbolicRuntimeHazardKind
    {
        DirectThrow,
        Rethrow,
        DivideByZero,
        NullDereference,
        NullableValueWithoutValue,
        IndexOutOfRange,
        ArgumentOutOfRange,
        CheckedIntegralOverflow,
        ArrayTypeMismatch,
        UnboxNull,
        InvalidCast,
        DynamicNullBinding,
        SwitchExpressionNoMatch,
        NegativeArrayLength,
        NegativeStackAllocLength,
        ArgumentNull,
    }

    public enum SymbolicRuntimeHazardStatus
    {
        Proven,
        Unreachable,
        Unknown,
        Unsupported,
    }
}
