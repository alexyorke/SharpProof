namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule {
    private static bool IsConstructionWithEscapingParameters(
        IObjectCreationOperation objectCreationOperation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (objectCreationOperation.Type is not INamedTypeSymbol namedType ||
            objectCreationOperation.Constructor == null)
            return false;

        foreach (var argument in objectCreationOperation.Arguments) {
            cancellationToken.ThrowIfCancellationRequested();
            var parameter = argument.Parameter;
            if (parameter == null) continue;

            if (namedType.IsRecord && HasMatchingRecordProperty(namedType, parameter)) return true;

            if (RuleAnalysisHelper.ConstructorStoresParameterMatching(
                    objectCreationOperation.Constructor,
                    parameter,
                    semanticModel,
                    cancellationToken,
                    target =>
                        (target is IFieldReferenceOperation fieldReference &&
                         IsInstanceMemberOfConstructedType(fieldReference.Field,
                             objectCreationOperation.Constructor.ContainingType) &&
                         RuleAnalysisHelper.IsThisOrImplicitInstance(fieldReference.Instance)) ||
                        (target is IPropertyReferenceOperation propertyReference &&
                         IsInstanceMemberOfConstructedType(propertyReference.Property,
                             objectCreationOperation.Constructor.ContainingType) &&
                         RuleAnalysisHelper.IsThisOrImplicitInstance(propertyReference.Instance))))
                return true;
        }

        return false;
    }

    private static bool IsInstanceMemberOfConstructedType(ISymbol member, INamedTypeSymbol constructedType) => member is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } &&
               SymbolEq.AreEqual(member.ContainingType.OriginalDefinition,
                   constructedType.OriginalDefinition);

    private static bool HasMatchingRecordProperty(INamedTypeSymbol recordType, IParameterSymbol parameter) {
        foreach (var member in recordType.GetMembers())
            if (member is IPropertySymbol property &&
                string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                SymbolEq.AreEqual(property.Type, parameter.Type))
                return true;

        return false;
    }
}
