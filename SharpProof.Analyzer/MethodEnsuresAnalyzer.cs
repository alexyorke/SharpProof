using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
            DiagnosticBaseline baseline)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var ensuresAttributeSymbol =
                ResolveAttributeSymbol(context.SemanticModel.Compilation, "SharpProof.Attributes.EnsuresAttribute", "EnsuresAttribute")
                ?? GetAppliedAttributeSymbol(methodSymbol, "EnsuresAttribute");
            var contracts = CollectContracts(methodSymbol, ensuresAttributeSymbol);
            if (contracts.Length == 0)
            {
                return;
            }

            if (!SupportsReturnValuePostconditions(methodSymbol, context.Node, out var unsupportedReason))
            {
                if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                {
                    foreach (var contract in contracts)
                    {
                        context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            unsupportedReason,
                            additionalLocations: null));
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
                    if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "condition parse failure",
                            additionalLocations: null));
                    }

                    continue;
                }

                if (!TryCreateSpeculativeConditionModel(
                        context.SemanticModel,
                        GetSpeculativePosition(returnSites[0]),
                        conditionStatement,
                        out var speculativeModel))
                {
                    if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "condition binding failure",
                            additionalLocations: null));
                    }

                    continue;
                }

                if (ReferencesUserLocal(conditionExpression, speculativeModel))
                {
                    if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "local variables are not supported in [Ensures] conditions",
                            additionalLocations: null));
                    }

                    continue;
                }

                foreach (var returnSite in returnSites)
                {
                    if (!purityService.SmtAnalysis.Options.IsEnabled)
                    {
                        if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                        {
                            context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                                methodSymbol,
                                contract.Condition,
                                returnSite.Location,
                                "SMT is disabled for [Ensures] verification",
                                additionalLocations: contract.Location == null ? null : new[] { contract.Location }));
                        }

                        continue;
                    }

                    if (!TryRewriteConditionForReturnSite(contract.Condition, returnSite.Expression, out var rewrittenCondition))
                    {
                        if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                        {
                            context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                                methodSymbol,
                                contract.Condition,
                                contract.Location,
                                "result placeholder rewrite failed",
                                additionalLocations: new[] { returnSite.Location }));
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
                        if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresNotProvenId, methodSymbol, context.Node.SyntaxTree))
                        {
                            context.ReportDiagnostic(CreateNotProvenDiagnostic(
                                methodSymbol,
                                contract.Condition,
                                returnSite,
                                contract.Location,
                                proof));
                        }

                        continue;
                    }

                    if (!baseline.IsSuppressed(SharpProofDiagnostics.EnsuresUnsupportedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            returnSite.Location,
                            FormatUnknownReason(proof),
                            additionalLocations: contract.Location == null ? null : new[] { contract.Location }));
                    }
                }
            }
        }

        private static ImmutableArray<EnsuresContract> CollectContracts(
            IMethodSymbol methodSymbol,
            INamedTypeSymbol? ensuresAttributeSymbol)
        {
            var builder = ImmutableArray.CreateBuilder<EnsuresContract>();
            foreach (var attribute in methodSymbol.GetAttributes())
            {
                if (!MatchesAttribute(attribute, ensuresAttributeSymbol, "EnsuresAttribute"))
                {
                    continue;
                }

                var condition = attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
                builder.Add(new EnsuresContract(condition ?? string.Empty, location));
            }

            return builder.ToImmutable();
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
            var rootOperation = GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken);
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
            SemanticModel speculativeModel)
        {
            foreach (var identifier in conditionExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal))
                {
                    continue;
                }

                var symbol = speculativeModel.GetSymbolInfo(identifier).Symbol;
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
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, proof.Proof.Status.ToString())
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, proof.Reason);

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
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                .Add(SharpProofDiagnostics.EnsuresUnknownReasonProperty, reason)
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, reason);

            return Diagnostic.Create(
                SharpProofDiagnostics.EnsuresUnsupportedRule,
                location,
                additionalLocations,
                properties,
                condition,
                methodSymbol.Name,
                reason);
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

        private static bool MatchesAttribute(
            AttributeData attribute,
            INamedTypeSymbol? expectedSymbol,
            string attributeTypeName)
        {
            var attributeClass = attribute.AttributeClass;
            return attributeClass != null &&
                ((expectedSymbol != null &&
                  SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, expectedSymbol)) ||
                 string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal));
        }

        private static INamedTypeSymbol? ResolveAttributeSymbol(Compilation compilation, string qualifiedMetadataName, string fallbackMetadataName)
        {
            return compilation.GetTypeByMetadataName(qualifiedMetadataName)
                ?? compilation.GetTypeByMetadataName(fallbackMetadataName)
                ?? FindTypeByName(compilation.Assembly.GlobalNamespace, fallbackMetadataName);
        }

        private static INamedTypeSymbol? FindTypeByName(INamespaceSymbol namespaceSymbol, string typeName)
        {
            var directMatch = namespaceSymbol.GetTypeMembers(typeName).FirstOrDefault();
            if (directMatch != null)
            {
                return directMatch;
            }

            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                var nestedMatch = FindTypeByName(nestedNamespace, typeName);
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }

        private static INamedTypeSymbol? GetAppliedAttributeSymbol(IMethodSymbol methodSymbol, string attributeTypeName)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                var attributeClass = attributeData.AttributeClass;
                if (attributeClass != null && string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal))
                {
                    return attributeClass;
                }
            }

            return null;
        }

        private static IOperation? GetMethodBodyRootOperation(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return methodNode switch
            {
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null =>
                    semanticModel.GetOperation(methodDeclaration.Body, cancellationToken),
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.Body != null =>
                    semanticModel.GetOperation(constructorDeclaration.Body, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(constructorDeclaration.ExpressionBody.Expression, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.Body != null =>
                    semanticModel.GetOperation(operatorDeclaration.Body, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(operatorDeclaration.ExpressionBody.Expression, cancellationToken),
                ConversionOperatorDeclarationSyntax conversionOperatorDeclaration when conversionOperatorDeclaration.Body != null =>
                    semanticModel.GetOperation(conversionOperatorDeclaration.Body, cancellationToken),
                ConversionOperatorDeclarationSyntax conversionOperatorDeclaration when conversionOperatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(conversionOperatorDeclaration.ExpressionBody.Expression, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.Body != null =>
                    semanticModel.GetOperation(accessorDeclaration.Body, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(accessorDeclaration.ExpressionBody.Expression, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.Body != null =>
                    semanticModel.GetOperation(localFunction.Body, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null =>
                    semanticModel.GetOperation(localFunction.ExpressionBody.Expression, cancellationToken),
                _ => semanticModel.GetOperation(methodNode, cancellationToken)
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

        private readonly record struct EnsuresContract(string Condition, Location? Location);

        private readonly record struct ReturnSite(
            ExpressionSyntax Expression,
            Location Location,
            Location QueryLocation,
            string DisplayText);
    }
}
