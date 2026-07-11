using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class MethodExpectedComplexityAnalyzer
{
    internal static void AnalyzeSymbolForExpectedComplexity(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return;

        if (!TryGetExpectedComplexity(
                methodSymbol,
                attributePolicy,
                context.CancellationToken,
                out var declaredComplexity,
                out var attributeLocation,
                out var invalidContract))
            return;

        if (invalidContract != null)
        {
            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                "[ExpectedComplexity]",
                invalidContract.Argument,
                invalidContract.Reason,
                attributeLocation ??
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken),
                methodSymbol,
                context.Node.SyntaxTree);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

            return;
        }

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;

        SymbolicComplexityResult result;
        try
        {
            result = context.State.GetComplexityResult(context.CancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
        {
            var diagnostic = CreateUnknownDiagnostic(
                methodSymbol,
                declaredComplexity,
                attributeLocation,
                "complexity query failed: " + ex.Message,
                context.CancellationToken,
                context.Node.SyntaxTree,
                SymbolicUnknownReasonTaxonomy.ForComplexityFailure(ex.Message));
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

            return;
        }

        var classification = Classify(result, declaredComplexity);
        switch (classification.Kind)
        {
            case ComplexityVerificationKind.Verified:
                return;

            case ComplexityVerificationKind.Exceeded:
                var exceededDiagnostic = CreateExceededDiagnostic(
                    methodSymbol,
                    declaredComplexity,
                    result,
                    attributeLocation,
                    context.CancellationToken,
                    context.Node.SyntaxTree);
                if (!baseline.IsSuppressed(exceededDiagnostic)) context.ReportDiagnostic(exceededDiagnostic);

                return;

            default:
                var unknownDiagnostic = CreateUnknownDiagnostic(
                    methodSymbol,
                    declaredComplexity,
                    attributeLocation,
                    classification.Reason,
                    context.CancellationToken,
                    context.Node.SyntaxTree,
                    result.UnknownReasonDetails.FirstOrDefault());
                if (!baseline.IsSuppressed(unknownDiagnostic)) context.ReportDiagnostic(unknownDiagnostic);

                return;
        }
    }

    private static bool TryGetExpectedComplexity(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        out DeclaredComplexity declaredComplexity,
        out Location? attributeLocation,
        out InvalidContractArgument? invalidContract)
    {
        declaredComplexity = default;
        attributeLocation = null;
        invalidContract = null;

        foreach (var attribute in attributePolicy.GetAcceptedAttributes(
                     methodSymbol,
                     "ExpectedComplexityAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int intValue)
            {
                declaredComplexity = new DeclaredComplexity(default, "invalid");
                invalidContract = new InvalidContractArgument(
                    GetAttributeArgumentText(attribute, cancellationToken),
                    "expected a ComplexityKind enum value");
                return true;
            }

            if (!Enum.IsDefined(typeof(DeclaredComplexityKind), intValue))
            {
                declaredComplexity = new DeclaredComplexity(
                    (DeclaredComplexityKind)intValue,
                    intValue.ToString());
                invalidContract = new InvalidContractArgument(
                    intValue.ToString(CultureInfo.InvariantCulture),
                    "undefined ComplexityKind value");
                return true;
            }

            declaredComplexity = new DeclaredComplexity((DeclaredComplexityKind)intValue);
            return true;
        }

        return false;
    }

    private static string GetAttributeArgumentText(AttributeData attribute, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";

        return "<missing>";
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

        if (TryMapActual(result.Complexity.Kind, out var actualClass))
            switch (Order(actualClass, MapDeclared(declaredComplexity.Kind)))
            {
                case ComplexityOrder.Within:
                    return ComplexityVerificationClassification.Verified;
                case ComplexityOrder.Exceeds:
                    return ComplexityVerificationClassification.Exceeded;
            }

        return ComplexityVerificationClassification.Unknown(
            "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" +
            declaredComplexity.Text + "'");
    }

    // Sound partial order over complexity growth classes. Constant, Logarithmic, Linear,
    // Linearithmic, and Quadratic form a total chain, so they order by rank. Product (O(n*m)) and
    // Max (O(max(n, m))) involve independent size parameters, so they only compare to themselves
    // and to Constant (the bottom element); every other pairing stays conservatively incomparable
    // (reported as SP0022) rather than being coerced into a chain position it cannot justify.
    private static ComplexityOrder Order(ComplexityClass actual, ComplexityClass declared)
    {
        if (actual == declared) return ComplexityOrder.Within;

        // O(1) is within every bound.
        if (actual == ComplexityClass.Constant) return ComplexityOrder.Within;

        if (TryGetChainRank(actual, out var actualRank) &&
            TryGetChainRank(declared, out var declaredRank))
            return actualRank <= declaredRank ? ComplexityOrder.Within : ComplexityOrder.Exceeds;

        return ComplexityOrder.Incomparable;
    }

    private static bool TryMapActual(SymbolicComplexityKind kind, out ComplexityClass complexityClass)
    {
        switch (kind)
        {
            case SymbolicComplexityKind.Constant:
                complexityClass = ComplexityClass.Constant;
                return true;
            case SymbolicComplexityKind.Linear:
                complexityClass = ComplexityClass.Linear;
                return true;
            case SymbolicComplexityKind.Quadratic:
                complexityClass = ComplexityClass.Quadratic;
                return true;
            case SymbolicComplexityKind.Product:
                complexityClass = ComplexityClass.Product;
                return true;
            case SymbolicComplexityKind.Max:
                complexityClass = ComplexityClass.Max;
                return true;
            default:
                complexityClass = default;
                return false;
        }
    }

    private static ComplexityClass MapDeclared(DeclaredComplexityKind kind)
    {
        return kind switch
        {
            DeclaredComplexityKind.Constant => ComplexityClass.Constant,
            DeclaredComplexityKind.Logarithmic => ComplexityClass.Logarithmic,
            DeclaredComplexityKind.Linear => ComplexityClass.Linear,
            DeclaredComplexityKind.Linearithmic => ComplexityClass.Linearithmic,
            DeclaredComplexityKind.Quadratic => ComplexityClass.Quadratic,
            DeclaredComplexityKind.Product => ComplexityClass.Product,
            DeclaredComplexityKind.Max => ComplexityClass.Max,
            // Undefined declared values are rejected upstream as invalid contracts; treat any
            // stray value as an isolated class so it stays conservatively incomparable.
            _ => ComplexityClass.Max
        };
    }

    private static bool TryGetChainRank(ComplexityClass complexityClass, out int rank)
    {
        switch (complexityClass)
        {
            case ComplexityClass.Constant:
                rank = 0;
                return true;
            case ComplexityClass.Logarithmic:
                rank = 1;
                return true;
            case ComplexityClass.Linear:
                rank = 2;
                return true;
            case ComplexityClass.Linearithmic:
                rank = 3;
                return true;
            case ComplexityClass.Quadratic:
                rank = 4;
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
        CancellationToken cancellationToken,
        SyntaxTree syntaxTree)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add(SharpProofDiagnostics.ActualComplexityProperty, result.Complexity.Text),
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "exceeded:" + declaredComplexity.Text + ":" + result.Complexity.Text);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            declaredComplexity.Text,
            "exceeded");

        return Diagnostic.Create(
            SharpProofDiagnostics.ComplexityExceededRule,
            location,
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
        CancellationToken cancellationToken,
        SyntaxTree syntaxTree,
        SymbolicUnknownReasonInfo? unknownReasonInfo)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add(SharpProofDiagnostics.ComplexityUnknownReasonProperty, reason),
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "unknown:" + declaredComplexity.Text + ":" + reason);
        properties = UnknownReasonDiagnosticProperties.Add(
            properties,
            unknownReasonInfo ?? SymbolicUnknownReasonTaxonomy.ForComplexityFailure(reason));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            declaredComplexity.Text,
            "unknown",
            (unknownReasonInfo ?? SymbolicUnknownReasonTaxonomy.ForComplexityFailure(reason)).Code);

        return Diagnostic.Create(
            SharpProofDiagnostics.ComplexityCouldNotBeVerifiedRule,
            location,
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
                DeclaredComplexityKind.Logarithmic => "O(log n)",
                DeclaredComplexityKind.Linear => "O(n)",
                DeclaredComplexityKind.Linearithmic => "O(n log n)",
                DeclaredComplexityKind.Quadratic => "O(n^2)",
                DeclaredComplexityKind.Product => "O(n * m)",
                DeclaredComplexityKind.Max => "O(max(n, m))",
                _ => Kind.ToString()
            };
    }

    // Mirrors the integer values of SharpProof.Attributes.ComplexityKind.
    private enum DeclaredComplexityKind
    {
        Constant = 0,
        Linear = 1,
        Quadratic = 2,
        Logarithmic = 3,
        Linearithmic = 4,
        Product = 5,
        Max = 6
    }

    // Unified growth classes shared by inferred (Symbolic) and declared bounds.
    private enum ComplexityClass
    {
        Constant,
        Logarithmic,
        Linear,
        Linearithmic,
        Quadratic,
        Product,
        Max
    }

    private enum ComplexityOrder
    {
        Within,
        Exceeds,
        Incomparable
    }

    private readonly record struct ComplexityVerificationClassification(
        ComplexityVerificationKind Kind,
        string Reason)
    {
        public static readonly ComplexityVerificationClassification Verified =
            new(ComplexityVerificationKind.Verified, string.Empty);

        public static readonly ComplexityVerificationClassification Exceeded =
            new(ComplexityVerificationKind.Exceeded, string.Empty);

        public static ComplexityVerificationClassification Unknown(string reason)
        {
            return new ComplexityVerificationClassification(ComplexityVerificationKind.Unknown, reason);
        }
    }

    private enum ComplexityVerificationKind
    {
        Verified,
        Exceeded,
        Unknown
    }

    private sealed record InvalidContractArgument(string Argument, string Reason);
}
