namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy
{
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var suppress = false;
        foreach (var symbol in EnumerateScopes(method))
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
        _ = ValidateScope(
            symbol, session, reportDiagnostic, cancellationToken);
        foreach (var attribute in symbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Attributes.IsRejectedControlAttribute(attribute) ||
                IsGeneratedAttribute(attribute, session, cancellationToken) ||
                !session.TryMarkRejectedControlAttributeReported(attribute, symbol))
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

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                suppressing.Value
                    ? "[SharpProofSuppress]"
                    : "[SharpProofTrusted]",
                string.IsNullOrEmpty(reason) ? "<empty>" : reason,
                "expected a non-empty reason",
                attribute.GetLocation()));
        }
    }

    internal static IEnumerable<ISymbol> EnumerateScopes(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        if (seen.Add(method))
        {
            yield return method;
        }

        if (method.AssociatedSymbol is { } associated &&
            seen.Add(associated))
        {
            yield return associated;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            if (seen.Add(type))
            {
                yield return type;
            }
        }

        if (method.ContainingType is { } containingType)
        {
            foreach (var interfaceType in containingType.AllInterfaces)
            {
                foreach (var member in interfaceType.GetMembers())
                {
                    if (member is not (IMethodSymbol or IPropertySymbol or IEventSymbol) ||
                        !IsImplementedBy(method, method.AssociatedSymbol, containingType, member))
                    {
                        continue;
                    }

                    if (seen.Add(interfaceType))
                    {
                        yield return interfaceType;
                    }

                    if (seen.Add(member))
                    {
                        yield return member;
                    }
                }
            }
        }

        if (method.ContainingAssembly is { } assembly &&
            seen.Add(assembly))
        {
            yield return assembly;
        }
    }

    private static bool IsImplementedBy(
        IMethodSymbol method,
        ISymbol? associated,
        INamedTypeSymbol containingType,
        ISymbol interfaceMember)
    {
        var implementation = containingType.FindImplementationForInterfaceMember(
            interfaceMember);
        if (implementation == null)
        {
            return false;
        }

        if (implementation is IMethodSymbol implementationMethod)
        {
            for (var candidate = method;
                 candidate != null;
                 candidate = candidate.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationMethod,
                        candidate))
                {
                    return true;
                }
            }
        }

        if (associated is IPropertySymbol property &&
            implementation is IPropertySymbol implementationProperty)
        {
            for (var candidate = property;
                 candidate != null;
                 candidate = candidate.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationProperty,
                        candidate))
                {
                    return true;
                }
            }
        }

        if (associated is IEventSymbol @event &&
            implementation is IEventSymbol implementationEvent)
        {
            for (var candidate = @event;
                 candidate != null;
                 candidate = candidate.OverriddenEvent)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        implementationEvent,
                        candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool HasTrustedAttribute(
        IMethodSymbol method,
        ContractSelectionInventory inventory)
    {
        return EnumerateScopes(method)
            .SelectMany(static symbol => symbol.GetAttributes())
            .Any(attribute => ContractSelectionInventory.Is(
                attribute, inventory.Trusted));
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
        var suppress = false;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var attribute in symbol.GetAttributes())
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
            if (symbol is not IMethodSymbol &&
                IsGeneratedAttribute(attribute, session, cancellationToken))
            {
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
        if (!session.TryMarkAttributeValidated(attribute, symbol))
        {
            return;
        }

        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            symbol.Locations.FirstOrDefault(static candidate => candidate.IsInSource) ??
            Location.None;
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

    private static bool IsGeneratedAttribute(
        AttributeData attribute,
        AnalyzerSession session,
        CancellationToken cancellationToken)
    {
        var tree = attribute.ApplicationSyntaxReference?.SyntaxTree;
        return tree != null &&
            AnalyzerGeneratedCodePolicy.IsGenerated(
                tree,
                session.Compilation,
                cancellationToken);
    }
}
