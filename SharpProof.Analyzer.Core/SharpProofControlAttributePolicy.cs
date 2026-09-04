namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy
{
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var suppress = false;
        foreach (var symbol in CompilerMethodScopes.Enumerate(method))
        {
            suppress |= ValidateScope(
                symbol, session, reportDiagnostic, cancellationToken);
        }
        return suppress;
    }

    internal static void ValidateDeclaredScope(
        ISymbol symbol, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var attributes = symbol.GetAttributes();
        _ = ValidateScope(
            symbol,
            attributes,
            session,
            reportDiagnostic,
            cancellationToken);
        foreach (var attribute in attributes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Attributes.IsRejectedControlAttribute(attribute) ||
                !session.TryMarkRejectedControlAttributeReported(attribute))
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference?
                .GetSyntax(cancellationToken).GetLocation() ??
                symbol.Locations.FirstOrDefault(static candidate =>
                    candidate.IsInSource) ??
                Location.None;
            ReportRejectedContractApi(
                symbol.Name,
                location,
                reportDiagnostic);
        }
    }

    internal static void ReportRejectedContractApi(
        string ownerName,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        reportDiagnostic(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            location,
            ownerName,
            "ContractApiIdentityRejected"));
    }

    internal static void ValidateNestedCallableDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var attributeLists = declaration switch
        {
            LocalFunctionStatementSyntax localFunction =>
                localFunction.AttributeLists,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda =>
                parenthesizedLambda.AttributeLists,
            SimpleLambdaExpressionSyntax simpleLambda =>
                simpleLambda.AttributeLists,
            _ => default
        };
        foreach (var attribute in attributeLists.SelectMany(
                     static list => list.Attributes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var constructor = semanticModel.GetSymbolInfo(
                attribute,
                cancellationToken).Symbol as IMethodSymbol;
            var suppressing = constructor == null
                ? null
                : IsSuppressing(
                    constructor.ContainingType,
                    session.Attributes);
            if (!suppressing.HasValue)
            {
                continue;
            }

            var argument = attribute.ArgumentList?.Arguments.Count == 1
                ? semanticModel.GetConstantValue(
                    attribute.ArgumentList.Arguments[0].Expression,
                    cancellationToken)
                : default;
            var reason = argument.HasValue && argument.Value is string value
                ? value
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(reason) ||
                !session.TryMarkAttributeValidated(
                    attribute.SyntaxTree,
                    attribute.Span))
            {
                continue;
            }

            ReportInvalidReasonDiagnostic(
                suppressing.Value,
                reason,
                attribute.GetLocation(),
                reportDiagnostic);
        }
    }

    private static bool TryGetReason(AttributeData attribute, out string reason)
    {
        reason = attribute.ConstructorArguments.Length == 1 &&
                 attribute.ConstructorArguments[0].Value is string value
            ? value
            : string.Empty;
        return !string.IsNullOrWhiteSpace(reason);
    }

    private static bool ValidateScope(
        ISymbol symbol, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        return ValidateScope(
            symbol,
            symbol.GetAttributes(),
            session,
            reportDiagnostic,
            cancellationToken);
    }

    private static bool ValidateScope(
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var suppress = false;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var attribute in attributes)
        {
            var suppressing = IsSuppressing(attribute, session.Attributes);
            if (!suppressing.HasValue)
            {
                continue;
            }

            if (TryGetReason(attribute, out var reason))
            {
                suppress |= suppressing.Value;
                continue;
            }
            ReportInvalidReason(
                symbol, attribute, suppressing.Value, reason, session,
                reportDiagnostic, cancellationToken);
        }
        return suppress;
    }

    private static void ReportInvalidReason(
        ISymbol symbol, AttributeData attribute, bool suppressing,
        string reason, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        if (!session.TryMarkAttributeValidated(attribute))
        {
            return;
        }

        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            symbol.Locations.FirstOrDefault(static candidate => candidate.IsInSource) ??
            Location.None;
        ReportInvalidReasonDiagnostic(
            suppressing, reason, location, reportDiagnostic);
    }

    private static void ReportInvalidReasonDiagnostic(
        bool suppressing,
        string reason,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
            suppressing ? "[SharpProofSuppress]" : "[SharpProofTrusted]",
            string.IsNullOrEmpty(reason) ? "<empty>" : reason,
            "expected a non-empty reason",
            location));
    }

    private static bool? IsSuppressing(
        AttributeData attribute,
        ContractSelectionInventory inventory)
    {
        return ContractSelectionInventory.Is(attribute, inventory.Suppress)
            ? true
            : ContractSelectionInventory.Is(attribute, inventory.Trusted)
                ? false
                : null;
    }

    private static bool? IsSuppressing(
        INamedTypeSymbol attributeType,
        ContractSelectionInventory inventory)
    {
        return SymbolEqualityComparer.Default.Equals(
                attributeType,
                inventory.Suppress)
            ? true
            : SymbolEqualityComparer.Default.Equals(
                attributeType,
                inventory.Trusted)
                ? false
                : null;
    }
}
