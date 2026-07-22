namespace SharpProof.Symbolic;

internal static class SymbolicFactFactory {
    internal static bool MatchesVariableOrMemberName(string candidate, string variableName)
        => string.Equals(candidate, variableName, StringComparison.Ordinal) ||
               candidate.StartsWith(variableName + ".", StringComparison.Ordinal) ||
               candidate.StartsWith(variableName + "[", StringComparison.Ordinal);

    internal static string GetSmtVariableName(ISymbol symbol) {
        var sourceLocation = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (sourceLocation != null)
            return symbol.Name + "#" +
                   sourceLocation.SourceSpan.Start.ToString(CultureInfo.InvariantCulture);

        var containingIdentity = symbol.ContainingSymbol == null
            ? string.Empty
            : DocumentationCommentId.CreateDeclarationId(symbol.ContainingSymbol.OriginalDefinition) ??
              symbol.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var ordinal = symbol switch {
            IParameterSymbol parameter => parameter.Ordinal.ToString(CultureInfo.InvariantCulture),
            ITypeParameterSymbol typeParameter => typeParameter.Ordinal.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
        return symbol.Name + "#metadata:" + containingIdentity + ":" + symbol.Kind + ":" + ordinal;
    }
    internal static bool TryCreateReferenceBuiltInLengthFormula(SmtFormula receiverFormula, out SmtFormula formula) {
        if (receiverFormula.Kind != SmtValueKind.Reference) {
            formula = null!;
            return false;
        }
        formula = new SmtVariable(GetReferenceFormulaName(receiverFormula) + ".Length", SmtValueKind.Int);
        return true;
    }
    internal static bool TryCreateReferenceArrayDimensionLengthFormula(SmtFormula receiverFormula, int dimension, out SmtFormula formula) {
        if (receiverFormula.Kind != SmtValueKind.Reference ||
            dimension < 0) {
            formula = null!;
            return false;
        }
        formula = new SmtVariable(
            GetReferenceFormulaName(receiverFormula) + ".GetLength(" +
            dimension.ToString(CultureInfo.InvariantCulture) + ")",
            SmtValueKind.Int);
        return true;
    }
    internal static bool TryCreateReferenceStringContentFormula(SmtFormula receiverFormula, out SmtFormula formula) {
        if (receiverFormula.Kind != SmtValueKind.Reference) {
            formula = null!;
            return false;
        }
        formula = new SmtVariable(GetReferenceFormulaName(receiverFormula) + ".String", SmtValueKind.String);
        return true;
    }
    internal static ITypeSymbol? GetTrackedSymbolType(ISymbol symbol) => symbol switch {
        ILocalSymbol localSymbol => localSymbol.Type,
        IParameterSymbol parameterSymbol => parameterSymbol.Type,
        _ => null
    };
    internal static bool TryGetDirectLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol) {
        var candidate = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
        if (candidate is ILocalSymbol or IParameterSymbol) {
            symbol = candidate;
            return true;
        }
        symbol = null!;
        return false;
    }
    internal static bool TryGetValueKind(
        ITypeSymbol type,
        Func<ITypeSymbol, bool> isIntegralType,
        Func<ITypeSymbol, bool> isReferenceLikeType,
        out SmtValueKind kind) {
        if (type.SpecialType == SpecialType.System_Boolean) {
            kind = SmtValueKind.Bool;
            return true;
        }
        if (isIntegralType(type)) {
            kind = SmtValueKind.Int;
            return true;
        }
        if (isReferenceLikeType(type)) {
            kind = SmtValueKind.Reference;
            return true;
        }
        kind = default;
        return false;
    }
    internal static bool IsSupportedSmtIntegralOrEnumType(ITypeSymbol? typeSymbol) {
        if (typeSymbol == null) return false;

        if (SymbolicTypeFacts.IsBuiltInIntegralType(typeSymbol))
            return true;

        return typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
               IsSupportedSmtIntegralOrEnumType(underlyingType);
    }
    internal static string GetReferenceFormulaName(SmtFormula receiverFormula) => receiverFormula is SmtVariable variable
            ? variable.Name
            : receiverFormula.ToString() ?? string.Empty;
}
