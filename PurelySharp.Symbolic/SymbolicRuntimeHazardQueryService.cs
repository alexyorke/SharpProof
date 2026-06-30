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
            var hazards = EnumerateCandidates(root, semanticModel, cancellationToken)
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
            var (status, reason) = ClassifyTrigger(
                analysis,
                candidate.TriggerCondition,
                smtAnalysis);
            var lineColumn = GetLineAndColumn(syntaxTree, candidate.Site.SpanStart, cancellationToken);
            var sourceSpan = GetNodeSourceSpan(syntaxTree, candidate.Site.Span, cancellationToken);

            return new SymbolicRuntimeHazard(
                syntaxTree.FilePath,
                candidate.Kind,
                status,
                reason,
                candidate.ExceptionType,
                candidate.Category,
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
                candidate.TriggerCondition.ToString() ?? string.Empty,
                analysis.MergedInvariantText,
                analysis.Facts,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
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
            CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.DescendantNodesAndSelf(descendIntoTrivia: false))
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

                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    if (TryCreateNullableValueCandidate(memberAccess, semanticModel, cancellationToken, out var nullableCandidate))
                    {
                        yield return nullableCandidate;
                    }

                    if (TryCreateNullDereferenceCandidate(memberAccess, memberAccess.Expression, semanticModel, cancellationToken, out var memberNullCandidate))
                    {
                        yield return memberNullCandidate;
                    }

                    break;
                case ElementAccessExpressionSyntax elementAccess:
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
                    if (TryCreateArrayTypeMismatchCandidate(assignment, semanticModel, cancellationToken, out var arrayTypeMismatchCandidate))
                    {
                        yield return arrayTypeMismatchCandidate;
                    }

                    break;
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is not MemberAccessExpressionSyntax &&
                        TryCreateNullDereferenceCandidate(invocation, invocation.Expression, semanticModel, cancellationToken, out var invocationNullCandidate))
                    {
                        yield return invocationNullCandidate;
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

        private static bool TryCreateNullDereferenceCandidate(
            SyntaxNode site,
            ExpressionSyntax receiver,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RuntimeHazardCandidate candidate)
        {
            candidate = default;
            var receiverType = GetExpressionType(receiver, semanticModel, cancellationToken);
            if (!IsReferenceType(receiverType) ||
                !TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger))
            {
                return false;
            }

            candidate = new RuntimeHazardCandidate(
                site,
                SymbolicRuntimeHazardKind.NullDereference,
                trigger,
                "System.NullReferenceException",
                "definite_null_dereference");
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
            if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken) ||
                !CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            var isRange = elementAccess.ArgumentList.Arguments.Count == 1 &&
                IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken);

            candidate = new RuntimeHazardCandidate(
                elementAccess,
                isRange ? SymbolicRuntimeHazardKind.ArgumentOutOfRange : SymbolicRuntimeHazardKind.IndexOutOfRange,
                new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula),
                isRange ? "System.ArgumentOutOfRangeException" : "System.IndexOutOfRangeException",
                isRange ? "definite_range_out_of_range" : "definite_index_out_of_range");
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
            trigger = CreateIntegralOutOfRangeFormula(resultFormula, minValue, maxValue);
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
                IsReferenceType(GetExpressionType(defaultExpression, semanticModel, cancellationToken)))
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
    }

    public enum SymbolicRuntimeHazardStatus
    {
        Proven,
        Unreachable,
        Unknown,
        Unsupported,
    }
}
