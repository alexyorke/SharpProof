using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic.Ir;

internal delegate bool KnownApiLoweringHandler(
    InvocationExpressionSyntax invocation,
    IMethodSymbol method,
    SymbolicLoweringContext context,
    out SymbolicCondition condition);

internal delegate bool KnownApiTermLoweringHandler(
    InvocationExpressionSyntax invocation,
    IMethodSymbol method,
    SymbolicLoweringContext context,
    out SymbolicTerm term);

internal sealed class KnownApiLoweringDescriptor
{
    public KnownApiLoweringDescriptor(
        string containingTypeMetadataName,
        string methodName,
        KnownApiLoweringHandler handler)
    {
        ContainingTypeMetadataName = containingTypeMetadataName;
        MethodName = methodName;
        Handler = handler;
    }

    public string ContainingTypeMetadataName { get; }

    public string MethodName { get; }

    public KnownApiLoweringHandler Handler { get; }

    public bool Matches(IMethodSymbol method)
    {
        return string.Equals(method.Name, MethodName, StringComparison.Ordinal) &&
               string.Equals(method.ContainingType?.ToDisplayString(), ContainingTypeMetadataName,
                   StringComparison.Ordinal);
    }
}

internal sealed class KnownApiTermLoweringDescriptor
{
    public KnownApiTermLoweringDescriptor(
        SpecialType containingTypeSpecialType,
        string methodName,
        KnownApiTermLoweringHandler handler)
    {
        ContainingTypeSpecialType = containingTypeSpecialType;
        MethodName = methodName;
        Handler = handler;
    }

    public SpecialType ContainingTypeSpecialType { get; }

    public string MethodName { get; }

    public KnownApiTermLoweringHandler Handler { get; }

    public bool Matches(IMethodSymbol method)
    {
        return string.Equals(method.Name, MethodName, StringComparison.Ordinal) &&
               method.ContainingType?.OriginalDefinition.SpecialType == ContainingTypeSpecialType;
    }
}