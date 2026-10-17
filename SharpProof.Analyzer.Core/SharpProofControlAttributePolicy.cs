namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy
{
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
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
        _ = ValidateDeclaredScopeAndShouldSuppress(symbol, session, reportDiagnostic, cancellationToken);
    }

    internal static bool ValidateDeclaredScopeAndShouldSuppress(
        ISymbol symbol, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        var attributes = symbol.GetAttributes();
        var suppressed = ValidateScope(
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
                    candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.IsInSource)) ??
                Location.None;
            ReportRejectedContractApi(
                symbol.Name,
                location,
                reportDiagnostic);
        }
        return suppressed;
    }

    internal static void ReportRejectedContractApi(
        string ownerName,
        Location location,
        Action<Diagnostic> reportDiagnostic)
    {
        if (ownerName is null)
        {
            throw new System.ArgumentNullException(nameof(ownerName));
        }
        location = ArgumentNullGuard.NotNull(location, nameof(location));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
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
        if (declaration is null)
        {
            throw new System.ArgumentNullException(nameof(declaration));
        }
        semanticModel = ArgumentNullGuard.NotNull(semanticModel, nameof(semanticModel));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
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
                     static list => list is null ? throw new System.ArgumentNullException(nameof(list)) : (list.Attributes)))
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
        if (attribute is null)
        {
            throw new System.ArgumentNullException(nameof(attribute));
        }
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

        if (attributes.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(attributes));
        }
        if (reportDiagnostic is null)
        {
            throw new System.ArgumentNullException(nameof(reportDiagnostic));
        }
        if (session is null)
        {
            throw new System.ArgumentNullException(nameof(session));
        }
        if (symbol is null)
        {
            throw new System.ArgumentNullException(nameof(symbol));
        }
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
        if (reason is null)
        {
            throw new System.ArgumentNullException(nameof(reason));
        }
        if (reportDiagnostic is null)
        {
            throw new System.ArgumentNullException(nameof(reportDiagnostic));
        }
        if (symbol is null)
        {
            throw new System.ArgumentNullException(nameof(symbol));
        }
        if (!session.TryMarkAttributeValidated(attribute))
        {
            return;
        }

        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            symbol.Locations.FirstOrDefault(static candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.IsInSource)) ??
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
        if (location is null)
        {
            throw new System.ArgumentNullException(nameof(location));
        }
        if (reason is null)
        {
            throw new System.ArgumentNullException(nameof(reason));
        }
        if (reportDiagnostic is null)
        {
            throw new System.ArgumentNullException(nameof(reportDiagnostic));
        }
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
        if (attribute is null)
        {
            throw new System.ArgumentNullException(nameof(attribute));
        }
        if (inventory is null)
        {
            throw new System.ArgumentNullException(nameof(inventory));
        }
        return ContractSelectionInventory.GetControlSelection(
            attribute.AttributeClass,
            inventory.Suppress,
            inventory.Trusted);
    }

    private static bool? IsSuppressing(
        INamedTypeSymbol attributeType,
        ContractSelectionInventory inventory)
    {
        if (attributeType is null)
        {
            throw new System.ArgumentNullException(nameof(attributeType));
        }
        if (inventory is null)
        {
            throw new System.ArgumentNullException(nameof(inventory));
        }
        return ContractSelectionInventory.GetControlSelection(
            attributeType,
            inventory.Suppress,
            inventory.Trusted);
    }
}
