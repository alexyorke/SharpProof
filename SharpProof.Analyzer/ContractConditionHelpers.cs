namespace SharpProof.Analyzer;

internal static class ContractConditionHelpers {
    internal static ImmutableArray<TContract> Collect<TContract>(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        string attributeTypeName,
        Func<ContractAttributeCondition, TContract> createContract,
        CancellationToken cancellationToken) {
        var builder = ImmutableArray.CreateBuilder<TContract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(source, attributeTypeName)) {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string
                : null;
            var argument = AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken);
            var key = condition ?? "<invalid>:" + argument;
            if (!seen.Add(key)) continue;

            builder.Add(createContract(new ContractAttributeCondition(
                condition ?? string.Empty,
                attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                argument,
                GetInvalidReason(attribute, condition),
                source)));
        }

        return builder.ToImmutable();
    }

    internal static bool TryParse(
        string conditionText,
        out IfStatementSyntax conditionStatement,
        out ExpressionSyntax conditionExpression) {
        var statement = SyntaxFactory.ParseStatement("if (" + conditionText + ") { }");
        if (statement.ContainsDiagnostics || statement is not IfStatementSyntax ifStatement) {
            conditionStatement = null!;
            conditionExpression = null!;
            return false;
        }

        conditionStatement = ifStatement;
        conditionExpression = ifStatement.Condition;
        return true;
    }

    internal static bool TryCreateSpeculativeModel(
        SemanticModel semanticModel,
        int position,
        IfStatementSyntax conditionStatement,
        out SemanticModel speculativeModel) {
        if (semanticModel.TryGetSpeculativeSemanticModel(position, conditionStatement, out var model) &&
            model != null) {
            speculativeModel = model;
            return true;
        }

        speculativeModel = null!;
        return false;
    }

    private static string? GetInvalidReason(AttributeData attribute, string? condition) {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string)
            return "expected a string condition";

        return string.IsNullOrWhiteSpace(condition)
            ? "condition must not be empty"
            : null;
    }
}

internal readonly record struct ContractAttributeCondition(
    string Condition,
    Location? Location,
    string Argument,
    string? InvalidReason,
    IMethodSymbol SourceMethod);
