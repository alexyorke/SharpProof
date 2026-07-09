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

        internal static bool TryGetSingleReturnedValueFromNestedCallable(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            out IOperation returnedOperation,
            out SyntaxNode returnedExpressionSyntax,
            out SemanticModel returnedSemanticModel,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            returnedOperation = null!;
            returnedExpressionSyntax = null!;
            returnedSemanticModel = semanticModel;

            if (methodSymbol == null ||
                !CanExtractSingleReturnedValue(methodSymbol))
            {
                return false;
            }

            var callableSyntax = methodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .FirstOrDefault();
            if (callableSyntax == null ||
                !TryGetSingleReturnedExpressionSyntax(callableSyntax, out returnedExpressionSyntax))
            {
                return false;
            }

            returnedSemanticModel = semanticModel.Compilation.GetSemanticModel(callableSyntax.SyntaxTree);
            var extractedOperation = SkipImplicitConversions(returnedSemanticModel.GetOperation(returnedExpressionSyntax, cancellationToken)!);
            if (extractedOperation == null)
            {
                return false;
            }

            returnedOperation = extractedOperation;
            return true;
        }

        private static bool CanExtractSingleReturnedValue(IMethodSymbol methodSymbol)
        {
            return methodSymbol.MethodKind == MethodKind.LocalFunction ||
                methodSymbol.MethodKind == MethodKind.AnonymousFunction ||
                methodSymbol.MethodKind == MethodKind.Ordinary ||
                methodSymbol.MethodKind == MethodKind.StaticConstructor ||
                methodSymbol.MethodKind == MethodKind.Constructor;
        }

        internal static bool TryGetSingleReturnedValueFromInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            out IOperation returnedOperation,
            out SyntaxNode returnedExpressionSyntax,
            out SemanticModel returnedSemanticModel,
            CancellationToken cancellationToken,
            PurityAnalysisState? currentState = null)
        {
            if (TryGetSingleReturnedValueFromNestedCallable(
                    invocationOperation.TargetMethod,
                    semanticModel,
                    out returnedOperation,
                    out returnedExpressionSyntax,
                    out returnedSemanticModel,
                    cancellationToken))
            {
                return true;
            }

            if (invocationOperation.TargetMethod.Name == "Invoke" &&
                invocationOperation.TargetMethod.ContainingType?.TypeKind == TypeKind.Delegate &&
                invocationOperation.Instance != null)
            {
                var potentialTargets = ResolvePotentialTargets(
                    invocationOperation.Instance,
                    currentState ?? PurityAnalysisState.Pure,
                    cancellationToken,
                    semanticModel);
                if (potentialTargets is { IsUnresolved: false } resolvedTargets &&
                    resolvedTargets.MethodSymbols.Count == 1)
                {
                    return TryGetSingleReturnedValueFromNestedCallable(
                        resolvedTargets.MethodSymbols.Single(),
                        semanticModel,
                        out returnedOperation,
                        out returnedExpressionSyntax,
                        out returnedSemanticModel,
                        cancellationToken);
                }
            }

            returnedOperation = null!;
            returnedExpressionSyntax = null!;
            returnedSemanticModel = semanticModel;
            return false;
        }

        private static bool TryGetSingleReturnedExpressionSyntax(
            SyntaxNode callableSyntax,
            out SyntaxNode returnedExpressionSyntax)
        {
            switch (callableSyntax)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody?.Expression != null:
                    returnedExpressionSyntax = localFunctionStatementSyntax.ExpressionBody.Expression;
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(localFunctionStatementSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody?.Expression != null:
                    returnedExpressionSyntax = methodDeclarationSyntax.ExpressionBody.Expression;
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(methodDeclarationSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(simpleLambdaExpressionSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(parenthesizedLambdaExpressionSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                    when anonymousMethodExpressionSyntax.Block != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(anonymousMethodExpressionSyntax.Block, out returnedExpressionSyntax);
                default:
                    returnedExpressionSyntax = null!;
                    return false;
            }
        }

        private static bool TryGetSingleReturnedExpressionSyntaxFromBody(
            SyntaxNode bodySyntax,
            out SyntaxNode returnedExpressionSyntax)
        {
            if (bodySyntax is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expressionSyntax)
            {
                returnedExpressionSyntax = expressionSyntax;
                return true;
            }

            if (bodySyntax is not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax blockSyntax)
            {
                returnedExpressionSyntax = null!;
                return false;
            }

            var directReturns = blockSyntax
                .DescendantNodes(static node =>
                    node is not Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax &&
                    node is not Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousFunctionExpressionSyntax)
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax>()
                .Where(returnStatement => returnStatement.Expression != null)
                .ToArray();
            if (directReturns.Length != 1)
            {
                returnedExpressionSyntax = null!;
                return false;
            }

            returnedExpressionSyntax = directReturns[0].Expression!;
            return true;
        }
    }
}
