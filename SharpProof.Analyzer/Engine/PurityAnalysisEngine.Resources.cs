using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

        private static PurityAnalysisState AddCompletedStraightLineUsingDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            IReturnOperation returnOperation,
            CancellationToken cancellationToken)
        {
            var nextState = currentState;
            foreach (var usingOperation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (usingOperation.Syntax.Span.End > returnOperation.Syntax.SpanStart ||
                    !IsStraightLineUsingStatement(usingOperation.Syntax))
                {
                    continue;
                }

                nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, nextState);
            }

            return nextState;
        }

        private static PurityAnalysisState AddScopeEndResourceDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            CancellationToken cancellationToken)
        {
            var nextState = currentState;
            foreach (var usingOperation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(usingOperation.Syntax))
                {
                    continue;
                }

                nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, nextState);
            }

            foreach (var usingDeclaration in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingDeclarationOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(usingDeclaration.Syntax))
                {
                    continue;
                }

                nextState = AddUsingDeclarationDisposeFacts(nextState, usingDeclaration);
            }

            return nextState;
        }

        private static PurityAnalysisState AddStraightLineResourceActionFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var nextState = currentState;
            foreach (var declarationGroup in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IVariableDeclarationGroupOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(declarationGroup.Syntax))
                {
                    continue;
                }

                foreach (var declaration in declarationGroup.Declarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var declarator in declaration.Declarators)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (declarator.Initializer?.Value is { } initializer)
                        {
                            nextState = AddAssignedAliasFact(
                                nextState,
                                declarator.Symbol,
                                initializer,
                                nextState);
                            nextState = AddOwnedDisposableLocalFacts(
                                nextState,
                                declarator.Symbol,
                                initializer,
                                semanticModel.Compilation);
                        }
                    }
                }
            }

            foreach (var deconstructionAssignment in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IDeconstructionAssignmentOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(deconstructionAssignment.Syntax))
                {
                    continue;
                }

                nextState = AddDeconstructedResourceAcquisitionFacts(
                    nextState,
                    deconstructionAssignment,
                    semanticModel,
                    cancellationToken);
            }

            foreach (var assignmentSyntax in methodBodyOperation.Syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(assignmentSyntax))
                {
                    continue;
                }

                nextState = AddDeconstructedResourceAcquisitionFacts(
                    nextState,
                    assignmentSyntax,
                    semanticModel,
                    cancellationToken);
            }

            foreach (var invocation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IInvocationOperation>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsStraightLineUsingStatement(invocation.Syntax))
                {
                    continue;
                }

                nextState = AddDisposeInvocationFacts(nextState, invocation, nextState);
            }

            return nextState;
        }

        private static PurityAnalysisState AddDeconstructedResourceAcquisitionFacts(
            PurityAnalysisState nextState,
            AssignmentExpressionSyntax assignmentSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!IsDeconstructionAssignmentSyntax(assignmentSyntax.Left))
            {
                return nextState;
            }

            foreach (var assignment in EnumerateDeconstructionSyntaxAssignments(
                         assignmentSyntax.Left,
                         assignmentSyntax.Right,
                         semanticModel,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var valueOperation = semanticModel.GetOperation(assignment.Value, cancellationToken);
                if (valueOperation == null)
                {
                    continue;
                }

                nextState = AddAssignedAliasFact(
                    nextState,
                    assignment.Local,
                    valueOperation,
                    nextState);
                nextState = AddOwnedDisposableLocalFacts(
                    nextState,
                    assignment.Local,
                    valueOperation,
                    semanticModel.Compilation);
            }

            return nextState;
        }

        private static bool IsDeconstructionAssignmentSyntax(ExpressionSyntax target)
        {
            target = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target);
            return target is TupleExpressionSyntax ||
                target is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax };
        }

        private static IEnumerable<DeconstructionSyntaxAssignmentElement> EnumerateDeconstructionSyntaxAssignments(
            ExpressionSyntax target,
            ExpressionSyntax value,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            target = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target);
            value = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(value);
            if (target is DeclarationExpressionSyntax declarationExpression)
            {
                foreach (var assignment in EnumerateDeconstructionDesignationAssignments(
                             declarationExpression.Designation,
                             value,
                             semanticModel,
                             cancellationToken))
                {
                    yield return assignment;
                }

                yield break;
            }

            if (target is TupleExpressionSyntax targetTuple &&
                value is TupleExpressionSyntax valueTuple)
            {
                var count = Math.Min(targetTuple.Arguments.Count, valueTuple.Arguments.Count);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionSyntaxAssignments(
                                 targetTuple.Arguments[i].Expression,
                                 valueTuple.Arguments[i].Expression,
                                 semanticModel,
                                 cancellationToken))
                    {
                        yield return nested;
                    }
                }

                yield break;
            }

            if (target is IdentifierNameSyntax identifierName &&
                semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol localSymbol)
            {
                yield return new DeconstructionSyntaxAssignmentElement(localSymbol, value);
            }
        }

        private static IEnumerable<DeconstructionSyntaxAssignmentElement> EnumerateDeconstructionDesignationAssignments(
            VariableDesignationSyntax designation,
            ExpressionSyntax value,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            value = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(value);
            if (designation is SingleVariableDesignationSyntax singleVariable &&
                semanticModel.GetDeclaredSymbol(singleVariable, cancellationToken) is ILocalSymbol localSymbol)
            {
                yield return new DeconstructionSyntaxAssignmentElement(localSymbol, value);
                yield break;
            }

            if (designation is ParenthesizedVariableDesignationSyntax parenthesized &&
                value is TupleExpressionSyntax tuple)
            {
                var count = Math.Min(parenthesized.Variables.Count, tuple.Arguments.Count);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionDesignationAssignments(
                                 parenthesized.Variables[i],
                                 tuple.Arguments[i].Expression,
                                 semanticModel,
                                 cancellationToken))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private readonly struct DeconstructionSyntaxAssignmentElement
        {
            public DeconstructionSyntaxAssignmentElement(ILocalSymbol local, ExpressionSyntax value)
            {
                Local = local;
                Value = value;
            }

            public ILocalSymbol Local { get; }

            public ExpressionSyntax Value { get; }
        }

        private static PurityAnalysisState AddDeconstructedResourceAcquisitionFacts(
            PurityAnalysisState nextState,
            IDeconstructionAssignmentOperation deconstructionAssignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in EnumerateDeconstructionAssignments(
                         deconstructionAssignment.Target,
                         deconstructionAssignment.Value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryResolveDeconstructionTargetSymbol(
                        assignment.Target,
                        nextState,
                        semanticModel,
                        cancellationToken) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                nextState = AddAssignedAliasFact(
                    nextState,
                    localSymbol,
                    assignment.Value,
                    nextState);
                nextState = AddOwnedDisposableLocalFacts(
                    nextState,
                    localSymbol,
                    assignment.Value,
                    semanticModel.Compilation);
            }

            return nextState;
        }

        private static PurityAnalysisState AddFinallyResourceDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var nextState = currentState;
            foreach (var tryStatement in methodBodyOperation.Syntax.DescendantNodes().OfType<TryStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tryStatement.Finally?.Block is not { } finallyBlock)
                {
                    continue;
                }

                foreach (var invocation in finallyBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                        memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                        semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } resourceSymbol ||
                        !FinallyBlockReleasesResource(finallyBlock, resourceSymbol, semanticModel, cancellationToken))
                    {
                        continue;
                    }

                    var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
                    nextState = AddResourceDisposedFacts(
                        nextState,
                        term,
                        resourceSymbol,
                        invocation,
                        "analyzer.resource.finally.dispose",
                        "evidence.resource.finally.dispose");
                }
            }

            return nextState;
        }

        private static bool IsStraightLineUsingStatement(SyntaxNode usingSyntax)
        {
            foreach (var ancestor in usingSyntax.Ancestors())
            {
                if (ancestor is MethodDeclarationSyntax ||
                    ancestor is ConstructorDeclarationSyntax ||
                    ancestor is OperatorDeclarationSyntax ||
                    ancestor is ConversionOperatorDeclarationSyntax ||
                    ancestor is AccessorDeclarationSyntax ||
                    ancestor is LocalFunctionStatementSyntax)
                {
                    return true;
                }

                if (ancestor is IfStatementSyntax ||
                    ancestor is ElseClauseSyntax ||
                    ancestor is SwitchStatementSyntax ||
                    ancestor is SwitchSectionSyntax ||
                    ancestor is WhileStatementSyntax ||
                    ancestor is DoStatementSyntax ||
                    ancestor is ForStatementSyntax ||
                    ancestor is ForEachStatementSyntax ||
                    ancestor is ForEachVariableStatementSyntax ||
                    ancestor is TryStatementSyntax ||
                    ancestor is CatchClauseSyntax)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
