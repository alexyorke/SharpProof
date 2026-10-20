namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private IParameterSymbol? _modeledBoolean;
    private bool _modeledBooleanValue;

    private static Result AnalyzeInputModel(IMethodSymbol method, IOperation body, CancellationToken cancellation)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        var expected = body.SemanticModel?.Compilation.GetTypeByMetadataName("SharpProof.Attributes.ResourceBooleanInputAttribute");
        IParameterSymbol? parameter = null;
        ResourceBooleanDistribution? distribution = null;
        foreach (var candidate in method.Parameters)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var attribute in candidate.GetAttributes())
            {
                if (expected is null || expected.ContainingAssembly.Identity.Name != "SharpProof.Attributes" || !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected))
                { continue; }
                var arguments = attribute.ConstructorArguments;
                if (distribution is not null || candidate.Type.SpecialType != SpecialType.System_Boolean || candidate.RefKind != RefKind.None ||
                    candidate.Name.Length > ResourceExpression.MaximumTextLength || candidate.Name.Any(char.IsControl) ||
                    arguments.Length != 2 || !attribute.NamedArguments.IsEmpty || arguments[0].Value is not int yes || arguments[1].Value is not int no ||
                    yes is < 0 or > ResourceBooleanDistribution.MaximumWeight || no is < 0 or > ResourceBooleanDistribution.MaximumWeight || yes + no == 0)
                { return new(null, "ResourceInputModelUnsupported"); }
                parameter = candidate;
                distribution = new(candidate.Ordinal, candidate.Name, yes, no);
            }
        }
        if (distribution is null)
        { return AnalyzeCase(method, body, cancellation); }
        var whenFalse = AnalyzeCase(method, body, cancellation, parameter, false);
        var whenTrue = AnalyzeCase(method, body, cancellation, parameter, true);
        if (whenFalse.Bounds is not { } noCost || whenTrue.Bounds is not { } yesCost)
        { return new(null, whenFalse.UnknownReason ?? whenTrue.UnknownReason ?? "ResourceInputModelUnsupported"); }
        try
        {
            var worst = noCost.Max(yesCost);
            return new(new Cost(worst.LoopIterations, worst.Arrays, worst.Elements,
                ResourceProgram.WithDistribution(distribution, noCost.Program, yesCost.Program)), null)
            {
                Assumptions = [.. whenFalse.Assumptions.Concat(whenTrue.Assumptions).Distinct(StringComparer.Ordinal).OrderBy(static value => value is null ? throw new ArgumentNullException(nameof(value)) : value, StringComparer.Ordinal)]
            };
        }
        catch (ResourceExpressionLimitException)
        { return new(null, "AnalysisBudgetExceeded"); }
    }

    private bool? FixedBoolean(IOperation operation, int depth = 0)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        _cancellation.ThrowIfCancellationRequested();
        if (_modeledBoolean is null || depth > 16)
        { return null; }
        if (operation.ConstantValue is { HasValue: true, Value: bool literal })
        { return literal; }
        if (operation is IParameterReferenceOperation parameter && SymbolEqualityComparer.Default.Equals(parameter.Parameter, _modeledBoolean))
        { return _modeledBooleanValue; }
        if (operation is IConversionOperation { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean } conversion)
        { return FixedBoolean(conversion.Operand, depth + 1); }
        if (operation is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } unary && FixedBoolean(unary.Operand, depth + 1) is { } value)
        { return !value; }
        return null;
    }
}
