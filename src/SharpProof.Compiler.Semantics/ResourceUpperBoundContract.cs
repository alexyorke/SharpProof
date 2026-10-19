namespace SharpProof.Compiler.Semantics;

internal static class ResourceUpperBoundContract
{
    internal static ResourceBoundConstraint? Decode(IMethodSymbol method, AttributeData attribute)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        var arguments = attribute.ConstructorArguments;
        if (method.MethodKind != MethodKind.Ordinary || arguments.Length != 4 || !attribute.NamedArguments.IsEmpty ||
            arguments.Any(static argument => argument.Kind == TypedConstantKind.Error) ||
            arguments[0].Value is not int metricValue || !Enum.IsDefined(typeof(ResourceCostMetric), metricValue) ||
            arguments[1].Value is not long constant || constant < 0 ||
            arguments[3].Value is not long coefficient || coefficient < 0)
        { return null; }
        ResourceExpression upper = ResourceExpression.Literal(constant);
        if (arguments[2].Value is null)
        { return coefficient == 0 ? new((ResourceCostMetric)metricValue, upper) : null; }
        if (arguments[2].Value is not string size || string.IsNullOrWhiteSpace(size) || coefficient == 0)
        { return null; }
        var arrayLength = size.EndsWith(".Length", StringComparison.Ordinal);
        var name = arrayLength ? size.Substring(0, size.Length - ".Length".Length) : size;
        var parameter = method.Parameters.FirstOrDefault(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.Name == name);
        if (parameter is null || parameter.RefKind != RefKind.None || parameter.Name.Length == 0 ||
            parameter.Name.Length > ResourceExpression.MaximumTextLength || parameter.Name.Any(static value => char.IsControl(value)))
        { return null; }
        ResourceInputKind inputKind;
        long maximum;
        if (arrayLength && parameter.Type is IArrayTypeSymbol { IsSZArray: true, Rank: 1 })
        {
            inputKind = ResourceInputKind.ArrayLength;
            maximum = int.MaxValue;
        }
        else if (!arrayLength && parameter.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64)
        {
            inputKind = ResourceInputKind.NonNegativeScalar;
            maximum = parameter.Type.SpecialType == SpecialType.System_Int32 ? int.MaxValue : long.MaxValue;
        }
        else
        { return null; }
        var input = ResourceExpression.InputSize(new ResourceInput(parameter.Ordinal, inputKind, parameter.Name, maximum));
        upper = ResourceExpression.Add(ResourceExpression.Multiply(input, ResourceExpression.Literal(coefficient)), upper);
        return new((ResourceCostMetric)metricValue, upper);
    }
}
