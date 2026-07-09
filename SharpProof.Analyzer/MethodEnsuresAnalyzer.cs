using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer
{
    internal static class MethodEnsuresAnalyzer
    {
        internal static void AnalyzeSymbolForEnsures(
            SyntaxNodeAnalysisContext context,
            CompilationPurityService purityService,
            DiagnosticBaseline baseline,
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var contracts = CollectContracts(methodSymbol, attributePolicy, context.CancellationToken);
            if (contracts.Length == 0)
            {
                return;
            }

            contracts = ReportAndFilterInvalidContracts(
                contracts,
                context,
                methodSymbol,
                baseline);
            if (contracts.Length == 0)
            {
                return;
            }

            if (!SupportsReturnValuePostconditions(methodSymbol, context.Node, out var unsupportedReason))
            {
                foreach (var contract in contracts)
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        unsupportedReason,
                        additionalLocations: null);
                    if (!baseline.IsSuppressed(diagnostic))
                    {
                        context.ReportDiagnostic(diagnostic);
                    }
                }

                return;
            }

            var returnSites = CollectReturnSites(context.Node, context.SemanticModel, context.CancellationToken);
            if (returnSites.Length == 0)
            {
                return;
            }

            var queryService = new SymbolicQueryService();
            var source = SymbolicSourceInput.FromSyntaxTree(context.Node.SyntaxTree, context.SemanticModel.Compilation);
            var options = new SymbolicQueryOptions(smtAnalysis: purityService.SmtAnalysis);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var contract in contracts)
            {
                if (!TryParseCondition(contract.Condition, out var conditionStatement, out var conditionExpression))
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "condition parse failure",
                        additionalLocations: null);
                    if (!baseline.IsSuppressed(diagnostic))
                    {
                        context.ReportDiagnostic(diagnostic);
                    }

                    continue;
                }

                if (!TryCreateSpeculativeConditionModel(
                        context.SemanticModel,
                        GetSpeculativePosition(returnSites[0]),
                        conditionStatement,
                        out var speculativeModel))
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "condition binding failure",
                        additionalLocations: null);
                    if (!baseline.IsSuppressed(diagnostic))
                    {
                        context.ReportDiagnostic(diagnostic);
                    }

                    continue;
                }

                if (ReferencesUserLocal(conditionExpression, speculativeModel, context.CancellationToken))
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "local variables are not supported in [Ensures] conditions",
                        additionalLocations: null);
                    if (!baseline.IsSuppressed(diagnostic))
                    {
                        context.ReportDiagnostic(diagnostic);
                    }

                    continue;
                }

                foreach (var returnSite in returnSites)
                {
                    if (!purityService.SmtAnalysis.Options.IsEnabled)
                    {
                        var diagnostic = CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            returnSite.Location,
                            "SMT is disabled for [Ensures] verification",
                            additionalLocations: contract.Location == null ? null : new[] { contract.Location });
                        if (!baseline.IsSuppressed(diagnostic))
                        {
                            context.ReportDiagnostic(diagnostic);
                        }

                        continue;
                    }

                    if (!TryRewriteConditionForReturnSite(contract.Condition, returnSite.Expression, out var rewrittenCondition))
                    {
                        var diagnostic = CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "result placeholder rewrite failed",
                            additionalLocations: new[] { returnSite.Location });
                        if (!baseline.IsSuppressed(diagnostic))
                        {
                            context.ReportDiagnostic(diagnostic);
                        }

                        continue;
                    }

                    var lineSpan = returnSite.QueryLocation.GetLineSpan();
                    var line = lineSpan.StartLinePosition.Line + 1;
                    var column = lineSpan.StartLinePosition.Character + 1;
                    var proof = queryService.Prove(
                        new SymbolicConditionProofRequest(
                            source,
                            SymbolicQueryTarget.Point(line, column),
                            rewrittenCondition,
                            options),
                        context.CancellationToken);

                    if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                        proof.TruthValue == SymbolicTruthValue.Unreachable)
                    {
                        continue;
                    }

                    var key = string.Join(
                        ":",
                        contract.Condition,
                        returnSite.Expression.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        proof.TruthValue.ToString(),
                        proof.Proof.UnknownReason.ToString(),
                        proof.Reason);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    if (proof.TruthValue == SymbolicTruthValue.ProvenFalse)
                    {
                        var diagnostic = CreateNotProvenDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            returnSite,
                            contract.Location,
                            proof);
                        if (!baseline.IsSuppressed(diagnostic))
                        {
                            context.ReportDiagnostic(diagnostic);
                        }

                        continue;
                    }

                    var unsupportedDiagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        returnSite.Location,
                        FormatUnknownReason(proof),
                        additionalLocations: contract.Location == null ? null : new[] { contract.Location });
                    if (!baseline.IsSuppressed(unsupportedDiagnostic))
                    {
                        context.ReportDiagnostic(unsupportedDiagnostic);
                    }
                }
            }
        }

        private static ImmutableArray<EnsuresContract> CollectContracts(
            IMethodSymbol methodSymbol,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<EnsuresContract>();
            foreach (var attribute in methodSymbol.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!attributePolicy.IsAccepted(attribute, "EnsuresAttribute"))
                {
                    continue;
                }

                var condition = attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                var invalidReason = GetInvalidContractReason(attribute, condition);
                builder.Add(new EnsuresContract(
                    condition ?? string.Empty,
                    location,
                    GetAttributeArgumentText(attribute, cancellationToken),
                    invalidReason));
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<EnsuresContract> ReportAndFilterInvalidContracts(
            ImmutableArray<EnsuresContract> contracts,
            SyntaxNodeAnalysisContext context,
            IMethodSymbol methodSymbol,
            DiagnosticBaseline baseline)
        {
            var validContracts = ImmutableArray.CreateBuilder<EnsuresContract>(contracts.Length);
            foreach (var contract in contracts)
            {
                if (contract.InvalidReason == null)
                {
                    validContracts.Add(contract);
                    continue;
                }

                var diagnostic = InvalidContractArgumentDiagnostics.Create(
                    "[Ensures]",
                    contract.Argument,
                    contract.InvalidReason,
                    contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                    methodSymbol,
                    context.Node.SyntaxTree);
                if (!baseline.IsSuppressed(diagnostic))
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }

            return validContracts.ToImmutable();
        }

        private static string? GetInvalidContractReason(AttributeData attribute, string? condition)
        {
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string)
            {
                return "expected a string condition";
            }

            return string.IsNullOrWhiteSpace(condition)
                ? "condition must not be empty"
                : null;
        }

        private static string GetAttributeArgumentText(AttributeData attribute, CancellationToken cancellationToken)
        {
            if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            {
                return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";
            }

            return "<missing>";
        }

        private static bool SupportsReturnValuePostconditions(
            IMethodSymbol methodSymbol,
            SyntaxNode methodNode,
            out string reason)
        {
            if (methodSymbol.MethodKind == MethodKind.Constructor ||
                methodSymbol.MethodKind == MethodKind.StaticConstructor)
            {
                reason = "constructors are not supported by [Ensures] yet";
                return false;
            }

            if (methodSymbol.ReturnsVoid)
            {
                reason = "void-returning members are not supported by [Ensures]";
                return false;
            }

            if (methodNode is AccessorDeclarationSyntax accessor &&
                (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                 accessor.IsKind(SyntaxKind.InitAccessorDeclaration) ||
                 accessor.IsKind(SyntaxKind.AddAccessorDeclaration) ||
                 accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration)))
            {
                reason = "non-returning accessors are not supported by [Ensures]";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static ImmutableArray<ReturnSite> CollectReturnSites(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var rootOperation = MethodBodyOperationResolver.GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken, includeConversionOperators: true);
            if (rootOperation == null)
            {
                return ImmutableArray<ReturnSite>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<ReturnSite>();
            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                if (operation is IReturnOperation { ReturnedValue: { } returnedValue })
                {
                    if (IsCompilerMarkedUnreachable(operation.Syntax, semanticModel, cancellationToken))
                    {
                        continue;
                    }

                    if (returnedValue.Syntax is ExpressionSyntax returnedExpression)
                    {
                        builder.Add(new ReturnSite(
                            returnedExpression,
                            returnedExpression.GetLocation(),
                            operation.Syntax.GetLocation(),
                            returnedExpression.ToString()));
                    }
                }
            }

            if (builder.Count != 0)
            {
                return builder.ToImmutable();
            }

            if (TryGetExpressionBody(methodNode, out var expressionBody))
            {
                builder.Add(new ReturnSite(
                    expressionBody,
                    expressionBody.GetLocation(),
                    expressionBody.GetLocation(),
                    expressionBody.ToString()));
            }

            return builder.ToImmutable();
        }

        private static bool IsCompilerMarkedUnreachable(
            SyntaxNode syntax,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return semanticModel.GetDiagnostics(syntax.Span, cancellationToken)
                .Any(diagnostic => diagnostic.Id == "CS0162");
        }

        private static bool TryParseCondition(
            string conditionText,
            out IfStatementSyntax conditionStatement,
            out ExpressionSyntax conditionExpression)
        {
            var statement = SyntaxFactory.ParseStatement("if (" + conditionText + ") { }");
            if (statement.ContainsDiagnostics || statement is not IfStatementSyntax ifStatement)
            {
                conditionStatement = null!;
                conditionExpression = null!;
                return false;
            }

            conditionStatement = ifStatement;
            conditionExpression = ifStatement.Condition;
            return true;
        }

        private static bool TryCreateSpeculativeConditionModel(
            SemanticModel semanticModel,
            int position,
            IfStatementSyntax ifStatement,
            out SemanticModel speculativeModel)
        {
            if (semanticModel.TryGetSpeculativeSemanticModel(position, ifStatement, out var model) &&
                model != null)
            {
                speculativeModel = model;
                return true;
            }

            speculativeModel = null!;
            return false;
        }

        private static int GetSpeculativePosition(ReturnSite returnSite)
        {
            return returnSite.Expression.SpanStart;
        }

        private static bool ReferencesUserLocal(
            ExpressionSyntax conditionExpression,
            SemanticModel speculativeModel,
            CancellationToken cancellationToken)
        {
            foreach (var identifier in conditionExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal))
                {
                    continue;
                }

                var symbol = speculativeModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol is ILocalSymbol)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryRewriteConditionForReturnSite(
            string conditionText,
            ExpressionSyntax returnExpression,
            out string rewrittenCondition)
        {
            rewrittenCondition = conditionText;
            if (!TryParseCondition(conditionText, out _, out var conditionExpression))
            {
                return false;
            }

            var rewriter = new ResultPlaceholderRewriter((ExpressionSyntax)returnExpression.WithoutTrivia());
            var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
            rewrittenCondition = rewritten.ToFullString();
            return true;
        }

        private static Diagnostic CreateNotProvenDiagnostic(
            IMethodSymbol methodSymbol,
            string condition,
            ReturnSite returnSite,
            Location? contractLocation,
            SymbolicConditionProofResult proof)
        {
            var properties = AddBaselineProperties(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                    .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, proof.Proof.Status.ToString())
                    .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, proof.Reason),
                methodSymbol,
                "EnsuresReturnSite",
                condition,
                "not_proven:" +
                    condition +
                    "@" +
                    returnSite.Expression.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" +
                    returnSite.Expression.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "|" +
                    proof.Proof.Status.ToString() +
                    "|" +
                    proof.Reason);
            properties = ExplainDiagnosticProperties.Add(
                properties,
                returnSite.Location,
                condition,
                proof.Proof.Status.ToString(),
                FormatUnknownReason(proof),
                impliedConditionText: condition);

            return Diagnostic.Create(
                SharpProofDiagnostics.EnsuresNotProvenRule,
                returnSite.Location,
                contractLocation == null ? null : new[] { contractLocation },
                properties,
                returnSite.DisplayText,
                methodSymbol.Name,
                condition);
        }

        private static Diagnostic CreateUnsupportedDiagnostic(
            IMethodSymbol methodSymbol,
            string condition,
            Location? location,
            string reason,
            IEnumerable<Location>? additionalLocations)
        {
            var properties = AddBaselineProperties(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                    .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                    .Add(SharpProofDiagnostics.EnsuresUnknownReasonProperty, reason)
                    .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, reason),
                methodSymbol,
                "EnsuresUnsupported",
                condition,
                "unsupported:" + condition + "@" + FormatLocationKey(location) + "|" + reason);
            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                condition,
                SymbolicProofStatus.Unknown.ToString(),
                reason,
                impliedConditionText: condition);

            return Diagnostic.Create(
                SharpProofDiagnostics.EnsuresUnsupportedRule,
                location,
                additionalLocations,
                properties,
                condition,
                methodSymbol.Name,
                reason);
        }

        private static ImmutableDictionary<string, string?> AddBaselineProperties(
            ImmutableDictionary<string, string?> properties,
            IMethodSymbol methodSymbol,
            string operationKind,
            string contractText,
            string evidenceKey)
        {
            var syntaxTree = methodSymbol.Locations.FirstOrDefault(location => location.SourceTree != null)?.SourceTree;
            return syntaxTree == null
                ? properties
                : BaselineDiagnosticProperties.Add(
                    properties,
                    methodSymbol,
                    syntaxTree,
                    operationKind,
                    contractText,
                    evidenceKey);
        }

        private static string FormatLocationKey(Location? location)
        {
            return location == null
                ? "none"
                : location.SourceSpan.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  ":" +
                  location.SourceSpan.End.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatUnknownReason(SymbolicConditionProofResult proof)
        {
            if (proof.Proof.UnknownReason != SymbolicUnknownReason.None &&
                proof.Proof.UnknownReason != SymbolicUnknownReason.Unknown)
            {
                return proof.Proof.UnknownReason.ToString();
            }

            return proof.Reason switch
            {
                "condition_parse_failure" => "condition parse failure",
                "condition_binding_failure" => "condition binding failure",
                "condition_not_supported" => "condition is not supported by the current bounded proof engine",
                "smt_required" => "SMT is required for [Ensures] verification",
                _ when string.IsNullOrWhiteSpace(proof.Reason) => "unknown",
                _ => proof.Reason.Replace('_', ' '),
            };
        }

        private static bool TryGetExpressionBody(SyntaxNode methodNode, out ExpressionSyntax expression)
        {
            expression = methodNode switch
            {
                MethodDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
                OperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
                ConversionOperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
                AccessorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
                LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
                _ => null!,
            };
            return expression != null;
        }

        private sealed class ResultPlaceholderRewriter : CSharpSyntaxRewriter
        {
            private readonly ExpressionSyntax _replacement;

            public ResultPlaceholderRewriter(ExpressionSyntax replacement)
            {
                _replacement = SyntaxFactory.ParenthesizedExpression(replacement);
            }

            public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (!string.Equals(node.Identifier.ValueText, "result", StringComparison.Ordinal))
                {
                    return base.VisitIdentifierName(node);
                }

                if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                    ReferenceEquals(memberAccess.Name, node))
                {
                    return base.VisitIdentifierName(node);
                }

                if (node.Parent is QualifiedNameSyntax qualifiedName &&
                    ReferenceEquals(qualifiedName.Right, node))
                {
                    return base.VisitIdentifierName(node);
                }

                return _replacement.WithTriviaFrom(node);
            }
        }

        private readonly record struct EnsuresContract(
            string Condition,
            Location? Location,
            string Argument,
            string? InvalidReason);

        private readonly record struct ReturnSite(
            ExpressionSyntax Expression,
            Location Location,
            Location QueryLocation,
            string DisplayText);
    }
}
