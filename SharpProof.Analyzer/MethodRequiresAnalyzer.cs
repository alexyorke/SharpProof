using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using static SharpProof.Analyzer.AnalyzerDiagnosticReporter;

namespace SharpProof.Analyzer;

internal static class MethodRequiresAnalyzer
{
    internal static void AnalyzeSymbolForRequires(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return;

        var contracts =
            RequiresContractHelpers.CollectContracts(methodSymbol, attributePolicy, context.CancellationToken);
        if (contracts.Length == 0) return;

        foreach (var contract in contracts)
        {
            if (contract.InvalidReason != null)
            {
                var invalidDiagnostic = InvalidContractArgumentDiagnostics.Create(
                    RequiresContractHelpers.AttributeDisplayName,
                    contract.Argument,
                    contract.InvalidReason,
                    contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                    methodSymbol,
                    context.Node.SyntaxTree);
                ReportIfNotSuppressed(context, baseline, invalidDiagnostic);
                continue;
            }

            if (!RequiresContractHelpers.TryParseCondition(contract.Condition, out _, out var conditionExpression))
            {
                ReportIfNotSuppressed(
                    context,
                    baseline,
                    CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "condition parse failure",
                        null));
                continue;
            }

            if (RequiresContractHelpers.ContainsResultReference(conditionExpression))
                ReportIfNotSuppressed(
                    context,
                    baseline,
                    CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "result placeholder is not supported in [Requires] conditions",
                        null));
        }
    }

    internal static void AnalyzeCallSiteForRequires(
        OperationAnalysisContext context,
        CompilationPurityService purityService,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        foreach (var callSite in CreateCallSites(context.Operation))
        {
            var contracts = RequiresContractHelpers.ValidContracts(
                callSite.Method,
                attributePolicy,
                context.CancellationToken);
            if (contracts.Length == 0) continue;

            var queryService = new SymbolicQueryService();
            var source = SymbolicSourceInput.FromSyntaxTree(callSite.Syntax.SyntaxTree, context.Compilation);
            var options = new SymbolicQueryOptions(smtAnalysis: purityService.SmtAnalysis)
                .WithAnalysisLimits(purityService.AnalysisLimits);
            var location = callSite.Syntax.GetLocation();
            var lineSpan = location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;
            var seen = ImmutableHashSet.CreateBuilder<string>();

            foreach (var contract in contracts)
            {
                if (!RequiresContractHelpers.TryRewriteForArguments(
                        contract.Condition,
                        contract.SourceMethod,
                        callSite.Method,
                        callSite.Arguments,
                        out var rewrittenCondition))
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            callSite.Method,
                            contract.Condition,
                            location,
                            "condition rewrite failure",
                            AdditionalLocations(contract.Location)));
                    continue;
                }

                if (!purityService.SmtAnalysis.Options.IsEnabled)
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            callSite.Method,
                            contract.Condition,
                            location,
                            "SMT is disabled for [Requires] verification",
                            AdditionalLocations(contract.Location)));
                    continue;
                }

                var proofOutcome = queryService.TryProve(
                    new SymbolicConditionProofRequest(
                        source,
                        SymbolicQueryTarget.Point(line, column),
                        rewrittenCondition,
                        options),
                    context.CancellationToken);
                var proof = AnalyzerSymbolicQueryBoundary.ResolveProof(
                    proofOutcome,
                    rewrittenCondition,
                    context.CancellationToken);

                if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                    proof.TruthValue == SymbolicTruthValue.Unreachable)
                    continue;

                var key = callSite.Method.ToDisplayString() + ":" + contract.Condition + ":" + line + ":" + column +
                          ":" + proof.TruthValue + ":" + proof.Reason;
                if (!seen.Add(key)) continue;

                if (proof.TruthValue == SymbolicTruthValue.ProvenFalse)
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateNotProvenDiagnostic(callSite.Method, contract.Condition, location, contract.Location,
                            proof));
                    continue;
                }

                ReportIfNotSuppressed(
                    context,
                    baseline,
                    CreateUnsupportedDiagnostic(
                        callSite.Method,
                        contract.Condition,
                        location,
                        ContractDiagnosticSupport.FormatUnknownReason(proof, "Requires"),
                        AdditionalLocations(contract.Location),
                        proof.AnalysisTruncation));
            }
        }
    }

    private static ImmutableArray<RequiresCallSite> CreateCallSites(IOperation operation)
    {
        var builder = ImmutableArray.CreateBuilder<RequiresCallSite>();
        switch (operation)
        {
            case IInvocationOperation invocation:
                builder.Add(new RequiresCallSite(
                    invocation.TargetMethod,
                    CreateArgumentMap(invocation.TargetMethod, invocation.Arguments),
                    invocation.Syntax));
                break;

            case IObjectCreationOperation { Constructor: { } constructor } objectCreation:
                builder.Add(new RequiresCallSite(
                    constructor,
                    CreateArgumentMap(constructor, objectCreation.Arguments),
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

            case ICompoundAssignmentOperation compoundAssignment
                when compoundAssignment.Target is IPropertyReferenceOperation propertyTarget:
                AddPropertyAccessor(builder, propertyTarget, propertyTarget.Property.GetMethod, null,
                    compoundAssignment.Syntax);
                AddOperator(
                    builder,
                    compoundAssignment.OperatorMethod,
                    compoundAssignment.Syntax,
                    propertyTarget,
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
                AddPropertyAccessor(builder, propertyTarget, propertyTarget.Property.GetMethod, null,
                    incrementOrDecrement.Syntax);
                AddOperator(
                    builder,
                    incrementOrDecrement.OperatorMethod,
                    incrementOrDecrement.Syntax,
                    propertyTarget);
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

    private static ExpressionSyntax? CreateCompoundSetterValue(ICompoundAssignmentOperation operation)
    {
        if (operation.Syntax is not AssignmentExpressionSyntax assignment ||
            operation.Target.Syntax is not ExpressionSyntax target ||
            operation.Value.Syntax is not ExpressionSyntax value ||
            !TryGetCompoundBinaryKind(assignment.Kind(), out var binaryKind))
            return null;

        return SyntaxFactory.BinaryExpression(
            binaryKind,
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)target.WithoutTrivia()),
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)value.WithoutTrivia()));
    }

    private static ExpressionSyntax? CreateIncrementSetterValue(IIncrementOrDecrementOperation operation)
    {
        if (operation.Target.Syntax is not ExpressionSyntax target) return null;

        var isDecrement = operation.Syntax.IsKind(SyntaxKind.PreDecrementExpression) ||
                          operation.Syntax.IsKind(SyntaxKind.PostDecrementExpression);
        return SyntaxFactory.BinaryExpression(
            isDecrement ? SyntaxKind.SubtractExpression : SyntaxKind.AddExpression,
            SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)target.WithoutTrivia()),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1)));
    }

    private static bool TryGetCompoundBinaryKind(SyntaxKind assignmentKind, out SyntaxKind binaryKind)
    {
        binaryKind = assignmentKind switch
        {
            SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
            SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
            SyntaxKind.ModuloAssignmentExpression => SyntaxKind.ModuloExpression,
            SyntaxKind.AndAssignmentExpression => SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.OrAssignmentExpression => SyntaxKind.BitwiseOrExpression,
            SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.RightShiftExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression => SyntaxKind.UnsignedRightShiftExpression,
            SyntaxKind.CoalesceAssignmentExpression => SyntaxKind.CoalesceExpression,
            _ => SyntaxKind.None
        };
        return binaryKind != SyntaxKind.None;
    }

    private static bool IsMutationTarget(IPropertyReferenceOperation propertyReference)
    {
        return propertyReference.Parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, propertyReference),
            IIncrementOrDecrementOperation incrementOrDecrement =>
                ReferenceEquals(incrementOrDecrement.Target, propertyReference),
            _ => false
        };
    }

    private static void AddPropertyAccessor(
        ImmutableArray<RequiresCallSite>.Builder builder,
        IPropertyReferenceOperation propertyReference,
        IMethodSymbol? accessor,
        ExpressionSyntax? setterValue,
        SyntaxNode? syntax = null)
    {
        if (accessor == null) return;

        var arguments = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var argument in propertyReference.Arguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 || ordinal >= accessor.Parameters.Length ||
                argument.Value.Syntax is not ExpressionSyntax expression)
                continue;

            arguments[accessor.Parameters[ordinal].Name] = (ExpressionSyntax)expression.WithoutTrivia();
        }

        if (setterValue != null && accessor.MethodKind is MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove)
        {
            var valueParameter = accessor.Parameters.LastOrDefault();
            if (valueParameter != null)
                arguments[valueParameter.Name] = (ExpressionSyntax)setterValue.WithoutTrivia();
        }

        builder.Add(new RequiresCallSite(accessor, arguments.ToImmutable(), syntax ?? propertyReference.Syntax));
    }

    private static void AddOperator(
        ImmutableArray<RequiresCallSite>.Builder builder,
        IMethodSymbol? operatorMethod,
        SyntaxNode syntax,
        params IOperation[] operands)
    {
        if (operatorMethod == null) return;

        var arguments = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        for (var index = 0; index < operands.Length && index < operatorMethod.Parameters.Length; index++)
            if (operands[index].Syntax is ExpressionSyntax expression)
                arguments[operatorMethod.Parameters[index].Name] = (ExpressionSyntax)expression.WithoutTrivia();

        builder.Add(new RequiresCallSite(operatorMethod, arguments.ToImmutable(), syntax));
    }

    private static ImmutableDictionary<string, ExpressionSyntax> CreateArgumentMap(
        IMethodSymbol method,
        ImmutableArray<IArgumentOperation> arguments)
    {
        var result = ImmutableDictionary.CreateBuilder<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 || ordinal >= method.Parameters.Length ||
                argument.Value.Syntax is not ExpressionSyntax expression)
                continue;

            result[method.Parameters[ordinal].Name] = (ExpressionSyntax)expression.WithoutTrivia();
        }

        return result.ToImmutable();
    }

    private static Diagnostic CreateNotProvenDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location location,
        Location? contractLocation,
        SymbolicConditionProofResult proof)
    {
        var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var properties = ContractDiagnosticSupport.AddBaselineProperties(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.RequiresConditionProperty, condition)
                .Add(SharpProofDiagnostics.RequiresProofStatusProperty, proof.Proof.Status.ToString())
                .Add(SharpProofDiagnostics.RequiresFailureReasonProperty, proof.Reason)
                .Add(SharpProofDiagnostics.RequiresCalleeProperty, callee),
            methodSymbol,
            "RequiresCallSite",
            condition,
            RequiresContractHelpers.CreateEvidenceKey("not_proven", condition, location, proof.Reason));
        properties = AnalysisTruncationDiagnosticProperties.Add(properties, proof.AnalysisTruncation);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            condition,
            proof.Proof.Status.ToString(),
            ContractDiagnosticSupport.FormatUnknownReason(proof, "Requires"),
            condition);

        return Diagnostic.Create(
            SharpProofDiagnostics.RequiresNotProvenRule,
            location,
            AdditionalLocations(contractLocation),
            properties,
            callee,
            condition);
    }

    internal static Diagnostic CreateUnsupportedDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location? location,
        string reason,
        IEnumerable<Location>? additionalLocations,
        SymbolicAnalysisTruncationInfo? analysisTruncation = null)
    {
        var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var properties = ContractDiagnosticSupport.AddBaselineProperties(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.RequiresConditionProperty, condition)
                .Add(SharpProofDiagnostics.RequiresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                .Add(SharpProofDiagnostics.RequiresUnknownReasonProperty, reason)
                .Add(SharpProofDiagnostics.RequiresFailureReasonProperty, reason)
                .Add(SharpProofDiagnostics.RequiresCalleeProperty, callee),
            methodSymbol,
            "RequiresUnsupported",
            condition,
            RequiresContractHelpers.CreateEvidenceKey("unsupported", condition, location, reason));
        properties = AnalysisTruncationDiagnosticProperties.Add(
            properties,
            analysisTruncation ?? SymbolicAnalysisTruncationInfo.None);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            condition,
            SymbolicProofStatus.Unknown.ToString(),
            reason,
            condition);

        return Diagnostic.Create(
            SharpProofDiagnostics.RequiresUnsupportedRule,
            location,
            additionalLocations,
            properties,
            callee,
            condition,
            reason);
    }

    private static IEnumerable<Location>? AdditionalLocations(Location? location)
    {
        return location == null ? null : new[] { location };
    }

    private readonly record struct RequiresCallSite(
        IMethodSymbol Method,
        ImmutableDictionary<string, ExpressionSyntax> Arguments,
        SyntaxNode Syntax);
}
