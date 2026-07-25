namespace SharpProof.Analyzer;
internal static class ContractConditionHelpers {
    internal static ImmutableArray<ContractAttributeCondition> Collect(
        IMethodSymbol methodSymbol,
        string attributeTypeName,
        CancellationToken cancellationToken) {
        var builder = ImmutableArray.CreateBuilder<ContractAttributeCondition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken)) {
            foreach (var attribute in SharpProofAttributeIdentityPolicy.GetAcceptedAttributes(source, attributeTypeName)) {
                cancellationToken.ThrowIfCancellationRequested();
                var condition = attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
                var argument = AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken);
                var key = GetDeduplicationKey(
                    condition,
                    argument,
                    source,
                    methodSymbol);
                if (!seen.Add(key)) continue;
                builder.Add(new ContractAttributeCondition(
                    condition ?? string.Empty,
                    attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation(),
                    argument,
                    GetInvalidReason(attribute, condition),
                    source));
            }
        }
        return builder.ToImmutable();
    }
    private static string GetDeduplicationKey(
        string? condition,
        string argument,
        IMethodSymbol source,
        IMethodSymbol target) {
        if (condition != null &&
            RequiresContractHelpers.TryRewriteForMethod(
                condition,
                source,
                target,
                out var rewritten))
            return rewritten;
        return (condition ?? "<invalid>:" + argument) + "|" +
               RoslynStructuralMethodIdentity.GetCanonicalKey(source);
    }
    internal static ImmutableArray<ContractAttributeCondition> ReportAndFilterInvalid(
        ImmutableArray<ContractAttributeCondition> contracts,
        string attributeDisplayName,
        MethodBodyAnalysisContext context) {
        var validContracts = ImmutableArray.CreateBuilder<ContractAttributeCondition>(contracts.Length);
        foreach (var contract in contracts) {
            if (contract.InvalidReason == null) {
                validContracts.Add(contract);
                continue;
            }
            context.ReportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                attributeDisplayName,
                contract.Argument,
                contract.InvalidReason,
                contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)));
        }
        return validContracts.ToImmutable();
    }
    internal static void ReportUnsupported(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ContractAttributeCondition contract,
        string reason,
        Func<IMethodSymbol, string, Location?, string, IEnumerable<Location>?, Diagnostic> createDiagnostic,
        Location? location = null,
        IEnumerable<Location>? additionalLocations = null) => context.ReportDiagnostic(createDiagnostic(
            methodSymbol,
            contract.Condition,
            location ?? contract.Location,
            reason,
            additionalLocations));
    internal static bool TryParse(
        string conditionText,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IfStatementSyntax? conditionStatement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ExpressionSyntax? conditionExpression) {
        var statement = SyntaxFactory.ParseStatement("if (" + conditionText + ") { }");
        if (statement.ContainsDiagnostics || statement is not IfStatementSyntax ifStatement) {
            conditionStatement = null;
            conditionExpression = null;
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
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SemanticModel? speculativeModel) {
        if (semanticModel.TryGetSpeculativeSemanticModel(position, conditionStatement, out var model) &&
            model != null) {
            speculativeModel = model;
            return true;
        }
        speculativeModel = null;
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
