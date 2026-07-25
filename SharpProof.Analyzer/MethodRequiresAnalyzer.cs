namespace SharpProof.Analyzer;
internal static class MethodRequiresAnalyzer {
    internal static void AnalyzeSymbolForRequires(
        MethodBodyAnalysisContext context,
        SmtAnalysisService smtAnalysis) {
        var methodSymbol = context.MethodSymbol;
        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;
        var contracts =
            RequiresContractHelpers.CollectContracts(methodSymbol, context.CancellationToken);
        contracts = ContractConditionHelpers.ReportAndFilterInvalid(
            contracts, RequiresContractHelpers.AttributeDisplayName, context);
        foreach (var contract in contracts) {
            if (!ContractConditionHelpers.TryParse(contract.Condition, out _, out var conditionExpression)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, "condition parse failure", CreateUnsupportedDiagnostic);
                continue;
            }
            if (RequiresContractHelpers.ContainsUnsupportedResultReference(
                    contract.Condition,
                    contract.SourceMethod)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract,
                    "result placeholder is not supported in [Requires] conditions", CreateUnsupportedDiagnostic);
                continue;
            }
            if (!RequiresContractHelpers.TryRewriteForMethod(
                    contract.Condition,
                    contract.SourceMethod,
                    methodSymbol,
                    out var implementationCondition) ||
                !ContractConditionHelpers.TryParse(
                    implementationCondition,
                    out var implementationStatement,
                    out var implementationExpression) ||
                !TryValidateConditionBinding(
                    context,
                    implementationStatement,
                    implementationExpression)) {
                ContractConditionHelpers.ReportUnsupported(
                    context,
                    methodSymbol,
                    contract,
                    "condition binding failure",
                    CreateUnsupportedDiagnostic);
            }
        }
        AnalyzeCallSitesForRequires(context, smtAnalysis);
    }
    private static bool TryValidateConditionBinding(
        MethodBodyAnalysisContext context,
        IfStatementSyntax conditionStatement,
        ExpressionSyntax conditionExpression) {
        var completion = MethodCompletionAnalysis.Collect(context).FirstOrDefault();
        if (completion.QueryNode == null) return true;
        var position = completion.QueryNode.SpanStart;
        if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                context.SemanticModel,
                position,
                conditionStatement,
                out var speculativeModel))
            return false;
        if (speculativeModel.GetTypeInfo(
                conditionExpression,
                context.CancellationToken).ConvertedType is not {
                    SpecialType: SpecialType.System_Boolean
                })
            return false;
        foreach (var node in conditionExpression.DescendantNodesAndSelf()) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (node is IdentifierNameSyntax identifier &&
                !CSharpSyntaxFacts.IsMemberOrQualifiedNameRightSide(identifier) &&
                speculativeModel.GetSymbolInfo(
                    identifier,
                    context.CancellationToken).Symbol == null)
                return false;
            if (node is MemberAccessExpressionSyntax or InvocationExpressionSyntax &&
                speculativeModel.GetSymbolInfo(
                    node,
                    context.CancellationToken).Symbol == null)
                return false;
        }
        return true;
    }
    private static void AnalyzeCallSitesForRequires(
        MethodBodyAnalysisContext context,
        SmtAnalysisService smtAnalysis) {
        foreach (var callSite in context.Snapshot.VisibleOperations.SelectMany(static operation => CreateCallSites(operation))) {
            var contracts = RequiresContractHelpers.ValidContracts(callSite.Method, context.CancellationToken);
            if (contracts.Length == 0) continue;
            var location = callSite.Syntax.GetLocation();
            var seenConditions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in contracts) {
                if (!RequiresContractHelpers.TryRewriteForArguments(
                        contract.Condition,
                        contract.SourceMethod,
                        callSite.Method,
                        callSite.Arguments,
                        callSite.Receiver,
                        out var rewrittenCondition)) {
                    ContractConditionHelpers.ReportUnsupported(
                        context, callSite.Method, contract, "condition rewrite failure", CreateUnsupportedDiagnostic,
                        location, AdditionalLocations(contract.Location));
                    continue;
                }
                if (!seenConditions.Add(rewrittenCondition)) continue;
                var proof = context.State.ProveAtNode(
                    callSite.Syntax,
                    rewrittenCondition,
                    smtAnalysis,
                    includeCurrentStatementCompletionFacts: false,
                    context.CancellationToken);
                if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                    proof.TruthValue == SymbolicTruthValue.Unreachable)
                    continue;
                if (proof.TruthValue == SymbolicTruthValue.ProvenFalse) {
                    context.ReportDiagnostic(CreateNotProvenDiagnostic(callSite.Method, contract.Condition, location, contract.Location));
                    continue;
                }
                ContractConditionHelpers.ReportUnsupported(
                    context, callSite.Method, contract, ContractDiagnosticSupport.FormatUnknownReason(proof, "Requires"),
                    CreateUnsupportedDiagnostic, location, AdditionalLocations(contract.Location));
            }
        }
    }
    private static ImmutableArray<RequiresCallSite> CreateCallSites(IOperation operation) {
        var builder = ImmutableArray.CreateBuilder<RequiresCallSite>();
        switch (operation) {
            case IInvocationOperation invocation:
                builder.Add(new RequiresCallSite(
                    invocation.TargetMethod,
                    CreateArgumentMap(invocation.TargetMethod, invocation.Arguments),
                    GetExplicitReceiver(invocation.Instance),
                    invocation.Syntax));
                break;
            case IObjectCreationOperation { Constructor: { } constructor } objectCreation:
                builder.Add(new RequiresCallSite(
                    constructor,
                    CreateArgumentMap(constructor, objectCreation.Arguments),
                    null,
                    objectCreation.Syntax));
                break;
            case IPropertyReferenceOperation propertyReference when !IsMutationTarget(propertyReference):
                AddPropertyAccessor(builder, propertyReference, propertyReference.Property.GetMethod, null);
                break;
            case ISimpleAssignmentOperation simpleAssignment
                when simpleAssignment.Target is IPropertyReferenceOperation propertyTarget:
                AddPropertyAccessor(
                    builder,
                    propertyTarget,
                    propertyTarget.Property.SetMethod,
                    simpleAssignment.Value.Syntax as ExpressionSyntax,
                    simpleAssignment.Syntax);
                break;
            case ICoalesceAssignmentOperation coalesceAssignment
                when coalesceAssignment.Target is IPropertyReferenceOperation propertyTarget:
                AddPropertyAccessor(
                    builder,
                    propertyTarget,
                    propertyTarget.Property.GetMethod,
                    null,
                    coalesceAssignment.Syntax);
                AddPropertyAccessor(
                    builder,
                    propertyTarget,
                    propertyTarget.Property.SetMethod,
                    coalesceAssignment.Value.Syntax as ExpressionSyntax,
                    coalesceAssignment.Syntax);
                break;
            case ICompoundAssignmentOperation compoundAssignment
                when compoundAssignment.Target is IPropertyReferenceOperation propertyTarget:
                AddPropertyAccessor(builder, propertyTarget, propertyTarget.Property.GetMethod, null, compoundAssignment.Syntax);
                AddOperator(builder, compoundAssignment.OperatorMethod, compoundAssignment.Syntax, propertyTarget,
                    compoundAssignment.Value);
                AddPropertyAccessor(
                    builder,
                    propertyTarget,
                    propertyTarget.Property.SetMethod,
                    CreateCompoundSetterValue(compoundAssignment),
                    compoundAssignment.Syntax);
                break;
            case IIncrementOrDecrementOperation incrementOrDecrement
                when incrementOrDecrement.Target is IPropertyReferenceOperation propertyTarget:
                AddPropertyAccessor(builder, propertyTarget, propertyTarget.Property.GetMethod, null, incrementOrDecrement.Syntax);
                AddOperator(builder, incrementOrDecrement.OperatorMethod, incrementOrDecrement.Syntax, propertyTarget);
                AddPropertyAccessor(
                    builder,
                    propertyTarget,
                    propertyTarget.Property.SetMethod,
                    CreateIncrementSetterValue(incrementOrDecrement),
                    incrementOrDecrement.Syntax);
                break;
            case IBinaryOperation { OperatorMethod: { } operatorMethod } binary:
                AddOperator(builder, operatorMethod, binary.Syntax, binary.LeftOperand, binary.RightOperand);
                break;
            case IUnaryOperation { OperatorMethod: { } operatorMethod } unary:
                AddOperator(builder, operatorMethod, unary.Syntax, unary.Operand);
                break;
            case IConversionOperation { OperatorMethod: { } operatorMethod } conversion:
                AddOperator(builder, operatorMethod, conversion.Syntax, conversion.Operand);
                break;
        }
        return builder.ToImmutable();
    }
    private static ExpressionSyntax? CreateCompoundSetterValue(ICompoundAssignmentOperation operation) {
        if (operation.Syntax is not AssignmentExpressionSyntax assignment ||
            operation.Target.Syntax is not ExpressionSyntax target ||
            operation.Value.Syntax is not ExpressionSyntax value ||
            !CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind))
            return null;
        return SyntaxFactory.BinaryExpression(
            binaryKind,
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)target.WithoutTrivia()),
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)value.WithoutTrivia()));
    }
    private static ExpressionSyntax? CreateIncrementSetterValue(IIncrementOrDecrementOperation operation) {
        if (operation.Target.Syntax is not ExpressionSyntax target ||
            operation.Syntax is not ExpressionSyntax updateExpression ||
            !CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(updateExpression, out _, out var delta))
            return null;
        return SyntaxFactory.BinaryExpression(
            delta < 0 ? SyntaxKind.SubtractExpression : SyntaxKind.AddExpression,
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)target.WithoutTrivia()),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1)));
    }
    private static bool IsMutationTarget(IPropertyReferenceOperation propertyReference) => propertyReference.Parent switch {
        IAssignmentOperation assignment => ReferenceEquals(assignment.Target, propertyReference),
        IIncrementOrDecrementOperation incrementOrDecrement =>
            ReferenceEquals(incrementOrDecrement.Target, propertyReference),
        _ => false
    };
    private static void AddPropertyAccessor(
        ImmutableArray<RequiresCallSite>.Builder builder,
        IPropertyReferenceOperation propertyReference,
        IMethodSymbol? accessor,
        ExpressionSyntax? setterValue,
        SyntaxNode? syntax = null) {
        if (accessor == null) return;
        var arguments = CreateArgumentMap(accessor, propertyReference.Arguments);
        if (setterValue != null && accessor.MethodKind is MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove) {
            var valueParameter = accessor.Parameters.LastOrDefault();
            if (valueParameter != null)
                arguments = arguments.SetItem(valueParameter.Name, (ExpressionSyntax)setterValue.WithoutTrivia());
        }
        builder.Add(new RequiresCallSite(
            accessor,
            arguments,
            GetExplicitReceiver(propertyReference.Instance),
            syntax ?? propertyReference.Syntax));
    }
    private static void AddOperator(
        ImmutableArray<RequiresCallSite>.Builder builder,
        IMethodSymbol? operatorMethod,
        SyntaxNode syntax,
        params IOperation[] operands) {
        if (operatorMethod == null) return;
        var arguments = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        for (var index = 0; index < operands.Length && index < operatorMethod.Parameters.Length; index++)
            if (operands[index].Syntax is ExpressionSyntax expression)
                arguments[operatorMethod.Parameters[index].Name] = (ExpressionSyntax)expression.WithoutTrivia();
        builder.Add(new RequiresCallSite(operatorMethod, arguments.ToImmutable(), null, syntax));
    }
    private static ImmutableDictionary<string, ExpressionSyntax> CreateArgumentMap(
        IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments) {
        var result = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in method.Parameters) {
            var matching = arguments
                .Where(argument => argument.Parameter?.Ordinal == parameter.Ordinal)
                .ToImmutableArray();
            if (parameter.IsParams &&
                matching.Any(static argument =>
                    argument.ArgumentKind == ArgumentKind.ParamArray)) {
                var expressions = matching
                    .SelectMany(static argument =>
                        argument.Value is IArrayCreationOperation {
                            Initializer: { } initializer
                        }
                            ? initializer.ElementValues
                            : [argument.Value])
                    .Select(static value => value.Syntax)
                    .OfType<ExpressionSyntax>()
                    .Select(static expression =>
                        (ExpressionSyntax)expression.WithoutTrivia())
                    .ToArray();
                var initializer = SyntaxFactory.InitializerExpression(
                    SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList(expressions));
                ExpressionSyntax paramsArray = parameter.Type is IArrayTypeSymbol arrayType
                    ? (ExpressionSyntax)SyntaxFactory.ArrayCreationExpression(
                        SyntaxFactory.ArrayType(
                            SyntaxFactory.ParseTypeName(
                                arrayType.ElementType.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat)),
                            SyntaxFactory.SingletonList(
                                SyntaxFactory.ArrayRankSpecifier(
                                    SyntaxFactory.SingletonSeparatedList<
                                        ExpressionSyntax>(
                                        SyntaxFactory.OmittedArraySizeExpression())))),
                        initializer)
                    : SyntaxFactory.ImplicitArrayCreationExpression(initializer);
                result[parameter.Name] =
                    (ExpressionSyntax)paramsArray.NormalizeWhitespace();
                continue;
            }
            var argument = matching.FirstOrDefault();
            if (argument == null) {
                if (TryCreateDefaultValueExpression(
                        parameter,
                        out var missingDefault))
                    result[parameter.Name] = missingDefault;
                continue;
            }
            if (argument.ArgumentKind == ArgumentKind.DefaultValue) {
                if (TryCreateDefaultValueExpression(
                        parameter,
                        out var implicitDefault))
                    result[parameter.Name] = implicitDefault;
                continue;
            }
            if (argument.Value.Syntax is ExpressionSyntax expression)
                result[parameter.Name] =
                    (ExpressionSyntax)expression.WithoutTrivia();
        }
        return result.ToImmutable();
    }
    private static bool TryCreateDefaultValueExpression(
        IParameterSymbol parameter,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out ExpressionSyntax? expression) {
        expression = null;
        if (!parameter.HasExplicitDefaultValue) return false;
        var text = parameter.ExplicitDefaultValue == null
            ? "null"
            : Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(
                parameter.ExplicitDefaultValue,
                quoteStrings: true,
                useHexadecimalNumbers: false);
        if (parameter.Type.TypeKind == TypeKind.Enum)
            text = "(" +
                   parameter.Type.ToDisplayString(
                       SymbolDisplayFormat.FullyQualifiedFormat) +
                   ")" +
                   text;
        expression = SyntaxFactory.ParseExpression(text);
        return !expression.ContainsDiagnostics;
    }
    private static ExpressionSyntax? GetExplicitReceiver(IOperation? instance) =>
        instance is { IsImplicit: false, Syntax: ExpressionSyntax expression }
            ? (ExpressionSyntax)expression.WithoutTrivia()
            : null;
    private static Diagnostic CreateNotProvenDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location location,
        Location? contractLocation) {
        var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("RequiresNotProvenRule"),
            location,
            AdditionalLocations(contractLocation),
            callee,
            condition);
    }
    internal static Diagnostic CreateUnsupportedDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location? location,
        string reason,
        IEnumerable<Location>? additionalLocations) {
        var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("RequiresUnsupportedRule"),
            location,
            additionalLocations,
            callee,
            condition,
            reason);
    }
    private static IEnumerable<Location>? AdditionalLocations(Location? location) =>
        location == null ? null : [location];
    readonly record struct RequiresCallSite(
        IMethodSymbol Method,
        ImmutableDictionary<string, ExpressionSyntax> Arguments,
        ExpressionSyntax? Receiver,
        SyntaxNode Syntax);
}
