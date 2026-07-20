namespace SharpProof.Analyzer;

internal static class MethodExpectedComplexityAnalyzer
{
    internal static void AnalyzeSymbolForExpectedComplexity(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        var report = AnalyzerDiagnosticReporter.CreateBaselineReporter(context, baseline);

        if (Engine.PurityAnalysisEngine.IsMetadataSymbol(methodSymbol)) return;

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
            report(diagnostic);

            return;
        }

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;

        var outcome = context.State.GetComplexityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var error = outcome.Error!;
            var diagnostic = CreateUnknownDiagnostic(
                methodSymbol,
                declaredComplexity,
                attributeLocation,
                "complexity query failed: " + error.Message,
                context.CancellationToken,
                context.Node.SyntaxTree,
                SymbolicUnknownReasonTaxonomy.ForComplexityFailure(error.Code + ": " + error.Message));
            report(diagnostic);

            return;
        }

        var result = outcome.Value!;

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
                report(exceededDiagnostic);

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
                report(unknownDiagnostic);

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

        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(
                     source,
                     "ExpectedComplexityAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int intValue)
            {
                declaredComplexity = new DeclaredComplexity(default, "invalid");
                invalidContract = new InvalidContractArgument(
                    AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken),
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

        if (result.Complexity.IsConservative)
            return ComplexityVerificationClassification.Unknown(
                "inferred complexity '" + result.Complexity.Text + "' contains conservative alternatives");

        if (ComplexityContractFacts.TryMap(result.Complexity.Kind, out var actualClass))
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
    private static ComplexityOrder Order(ComplexityGrowthClass actual, ComplexityGrowthClass declared)
    {
        if (actual == declared) return ComplexityOrder.Within;

        // O(1) is within every bound.
        if (actual == ComplexityGrowthClass.Constant) return ComplexityOrder.Within;

        // Constant is the strict bottom bound: every known nonconstant class exceeds it,
        // including Product and Max, which are otherwise incomparable with the rank chain.
        if (declared == ComplexityGrowthClass.Constant) return ComplexityOrder.Exceeds;

        if (TryGetChainRank(actual, out var actualRank) &&
            TryGetChainRank(declared, out var declaredRank))
            return actualRank <= declaredRank ? ComplexityOrder.Within : ComplexityOrder.Exceeds;

        return ComplexityOrder.Incomparable;
    }

    private static ComplexityGrowthClass MapDeclared(DeclaredComplexityKind kind) =>
        GetDeclaredComplexityDescriptor(kind).GrowthClass;

    private static (ComplexityGrowthClass GrowthClass, string Text) GetDeclaredComplexityDescriptor(
        DeclaredComplexityKind kind)
    {
        return kind switch
        {
            DeclaredComplexityKind.Constant => (ComplexityGrowthClass.Constant, "O(1)"),
            DeclaredComplexityKind.Logarithmic => (ComplexityGrowthClass.Logarithmic, "O(log n)"),
            DeclaredComplexityKind.Linear => (ComplexityGrowthClass.Linear, "O(n)"),
            DeclaredComplexityKind.Linearithmic => (ComplexityGrowthClass.Linearithmic, "O(n log n)"),
            DeclaredComplexityKind.Quadratic => (ComplexityGrowthClass.Quadratic, "O(n^2)"),
            DeclaredComplexityKind.Product => (ComplexityGrowthClass.Product, "O(n * m)"),
            DeclaredComplexityKind.Max => (ComplexityGrowthClass.Max, "O(max(n, m))"),
            // Undefined declared values are rejected upstream. Keep any stray value isolated.
            _ => (ComplexityGrowthClass.Max, kind.ToString())
        };
    }

    private static bool TryGetChainRank(ComplexityGrowthClass complexityClass, out int rank)
    {
        switch (complexityClass)
        {
            case ComplexityGrowthClass.Constant:
                rank = 0;
                return true;
            case ComplexityGrowthClass.Logarithmic:
                rank = 1;
                return true;
            case ComplexityGrowthClass.Linear:
                rank = 2;
                return true;
            case ComplexityGrowthClass.Linearithmic:
                rank = 3;
                return true;
            case ComplexityGrowthClass.Quadratic:
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
        var properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add("sharpproof.complexity.actual", result.Complexity.Text),
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "exceeded:" + declaredComplexity.Text + ":" + result.Complexity.Text,
            location,
            declaredComplexity.Text,
            "exceeded");

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityExceededRule"),
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
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.ExpectedComplexityProperty, declaredComplexity.Text)
            .Add("sharpproof.complexity.unknown_reason", reason);
        var effectiveUnknownReason = unknownReasonInfo ?? SymbolicUnknownReasonTaxonomy.ForComplexityFailure(reason);
        properties = UnknownReasonDiagnosticProperties.Add(properties, effectiveUnknownReason);
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "unknown:" + declaredComplexity.Text + ":" + reason,
            location,
            declaredComplexity.Text,
            "unknown",
            effectiveUnknownReason.Code);

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityCouldNotBeVerifiedRule"),
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
        public string Text => TextOverride ?? GetDeclaredComplexityDescriptor(Kind).Text;
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

        public static ComplexityVerificationClassification Unknown(string reason) =>
            new ComplexityVerificationClassification(ComplexityVerificationKind.Unknown, reason);
    }

    private enum ComplexityVerificationKind
    {
        Verified,
        Exceeded,
        Unknown
    }

    private sealed record InvalidContractArgument(string Argument, string Reason);
}
