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
    internal sealed partial class SymbolicRuntimeHazardQueryService
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

                    if (TryCreateNullableValueCastCandidate(castExpression, semanticModel, cancellationToken, out var nullableCastCandidate))
                    {
                        yield return nullableCastCandidate;
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
        internal SymbolicRuntimeHazardQueryResult(
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
        internal SymbolicRuntimeHazard(
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
