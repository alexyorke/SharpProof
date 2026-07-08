using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer
{
    internal static class MethodExpectedComplexityAnalyzer
    {
        private static readonly SymbolicQueryService QueryService = new SymbolicQueryService();

        internal static void AnalyzeSymbolForExpectedComplexity(
            SyntaxNodeAnalysisContext context,
            Configuration.DiagnosticBaseline baseline)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            if (!TryGetExpectedComplexity(
                    methodSymbol,
                    context.SemanticModel.Compilation,
                    context.CancellationToken,
                    out var declaredComplexity,
                    out var attributeLocation,
                    out var invalidContractReason))
            {
                return;
            }

            if (invalidContractReason != null)
            {
                if (!baseline.IsSuppressed(SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId, methodSymbol, context.Node.SyntaxTree))
                {
                    context.ReportDiagnostic(CreateUnknownDiagnostic(
                        methodSymbol,
                        declaredComplexity,
                        attributeLocation,
                        invalidContractReason,
                        context.CancellationToken));
                }

                return;
            }

            SymbolicComplexityResult result;
            try
            {
                result = QueryService.QueryComplexity(
                    new SymbolicComplexityRequest(
                        SymbolicSourceInput.FromNode(context.Node, context.SemanticModel),
                        SymbolicQueryTarget.Node()),
                    context.CancellationToken);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                if (!baseline.IsSuppressed(SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId, methodSymbol, context.Node.SyntaxTree))
                {
                    context.ReportDiagnostic(CreateUnknownDiagnostic(
                        methodSymbol,
                        declaredComplexity,
                        attributeLocation,
                        "complexity query failed: " + ex.Message,
                        context.CancellationToken));
                }

                return;
            }

            var classification = Classify(result, declaredComplexity);
            switch (classification.Kind)
            {
                case ComplexityVerificationKind.Verified:
                    return;

                case ComplexityVerificationKind.Exceeded:
                    if (!baseline.IsSuppressed(SharpProofDiagnostics.ComplexityExceededId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateExceededDiagnostic(
                            methodSymbol,
                            declaredComplexity,
                            result,
                            attributeLocation,
                            context.CancellationToken));
                    }

                    return;

                default:
                    if (!baseline.IsSuppressed(SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId, methodSymbol, context.Node.SyntaxTree))
                    {
                        context.ReportDiagnostic(CreateUnknownDiagnostic(
                            methodSymbol,
                            declaredComplexity,
                            attributeLocation,
                            classification.Reason,
                            context.CancellationToken));
                    }

                    return;
            }
        }

        private static bool TryGetExpectedComplexity(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            out DeclaredComplexity declaredComplexity,
            out Location? attributeLocation,
            out string? invalidContractReason)
        {
            declaredComplexity = default;
            attributeLocation = null;
            invalidContractReason = null;

            var attributeSymbol =
                compilation.GetTypeByMetadataName("SharpProof.Attributes.ExpectedComplexityAttribute") ??
                compilation.GetTypeByMetadataName("ExpectedComplexityAttribute");

            foreach (var attribute in methodSymbol.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!AnalyzerSyntaxHelpers.MatchesAttribute(attribute, attributeSymbol, "ExpectedComplexityAttribute"))
                {
                    continue;
                }

                attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not int intValue)
                {
                    declaredComplexity = new DeclaredComplexity(default, "invalid");
                    invalidContractReason = "invalid expected complexity argument";
                    return true;
                }

                if (!Enum.IsDefined(typeof(DeclaredComplexityKind), intValue))
                {
                    declaredComplexity = new DeclaredComplexity(
                        (DeclaredComplexityKind)intValue,
                        intValue.ToString());
                    invalidContractReason = "invalid expected complexity value '" + intValue + "'";
                    return true;
                }

                declaredComplexity = new DeclaredComplexity((DeclaredComplexityKind)intValue);
                return true;
            }

            return false;
        }

        private static ComplexityVerificationClassification Classify(
            SymbolicComplexityResult result,
            DeclaredComplexity declaredComplexity)
        {
            if (result.Complexity.IsUnknown || result.Complexity.IsRecursiveUnknown)
            {
                var reason = result.UnknownReasons.Count > 0
                    ? result.UnknownReasons[0].ToString()
                    : "complexity unknown";
                return ComplexityVerificationClassification.Unknown(reason);
            }

            if (TryCompare(result.Complexity.Kind, declaredComplexity.Kind, out var comparison))
            {
                return comparison <= 0
                    ? ComplexityVerificationClassification.Verified
                    : ComplexityVerificationClassification.Exceeded;
            }

            return ComplexityVerificationClassification.Unknown(
                "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" + declaredComplexity.Text + "'");
        }

        private static bool TryCompare(
            SymbolicComplexityKind actual,
            DeclaredComplexityKind declared,
            out int comparison)
        {
            comparison = 0;
            if (!TryGetRank(actual, out var actualRank))
            {
                return false;
            }

            if (!TryGetRank(declared, out var declaredRank))
            {
                return false;
            }

            comparison = actualRank.CompareTo(declaredRank);
            return true;
        }

        private static bool TryGetRank(SymbolicComplexityKind kind, out int rank)
        {
            switch (kind)
            {
                case SymbolicComplexityKind.Constant:
                    rank = 0;
                    return true;
                case SymbolicComplexityKind.Linear:
                    rank = 1;
                    return true;
                case SymbolicComplexityKind.Quadratic:
                    rank = 2;
                    return true;
                default:
                    rank = -1;
                    return false;
            }
        }

        private static bool TryGetRank(DeclaredComplexityKind kind, out int rank)
        {
            switch (kind)
            {
                case DeclaredComplexityKind.Constant:
                    rank = 0;
                    return true;
                case DeclaredComplexityKind.Linear:
                    rank = 1;
                    return true;
                case DeclaredComplexityKind.Quadratic:
                    rank = 2;
                    return true;
                default:
                    rank = -1;
                    return false;
            }
        }

        private static Diagnostic CreateExceededDiagnostic(
            IMethodSymbol methodSymbol,
            DeclaredComplexity declaredComplexity,
            SymbolicComplexityResult result,
            Location? attributeLocation,
            CancellationToken cancellationToken)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add(SharpProofDiagnostics.ActualComplexityProperty, result.Complexity.Text);

            return Diagnostic.Create(
                SharpProofDiagnostics.ComplexityExceededRule,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken),
                attributeLocation == null ? null : new[] { attributeLocation },
                properties,
                methodSymbol.Name,
                declaredComplexity.Text,
                result.Complexity.Text);
        }

        private static Diagnostic CreateUnknownDiagnostic(
            IMethodSymbol methodSymbol,
            DeclaredComplexity declaredComplexity,
            Location? attributeLocation,
            string reason,
            CancellationToken cancellationToken)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add(SharpProofDiagnostics.ComplexityUnknownReasonProperty, reason);

            return Diagnostic.Create(
                SharpProofDiagnostics.ComplexityCouldNotBeVerifiedRule,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken),
                attributeLocation == null ? null : new[] { attributeLocation },
                properties,
                methodSymbol.Name,
                declaredComplexity.Text,
                reason);
        }

        private readonly record struct DeclaredComplexity(
            DeclaredComplexityKind Kind,
            string? TextOverride = null)
        {
            public string Text =>
                TextOverride ??
                Kind switch
                {
                    DeclaredComplexityKind.Constant => "O(1)",
                    DeclaredComplexityKind.Linear => "O(n)",
                    DeclaredComplexityKind.Quadratic => "O(n^2)",
                    _ => Kind.ToString(),
                };
        }

        private enum DeclaredComplexityKind
        {
            Constant = 0,
            Linear = 1,
            Quadratic = 2,
        }

        private readonly record struct ComplexityVerificationClassification(
            ComplexityVerificationKind Kind,
            string Reason)
        {
            public static readonly ComplexityVerificationClassification Verified =
                new ComplexityVerificationClassification(ComplexityVerificationKind.Verified, string.Empty);

            public static readonly ComplexityVerificationClassification Exceeded =
                new ComplexityVerificationClassification(ComplexityVerificationKind.Exceeded, string.Empty);

            public static ComplexityVerificationClassification Unknown(string reason) =>
                new ComplexityVerificationClassification(ComplexityVerificationKind.Unknown, reason);
        }

        private enum ComplexityVerificationKind
        {
            Verified,
            Exceeded,
            Unknown,
        }
    }
}
