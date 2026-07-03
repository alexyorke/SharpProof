using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic.Ir
{
    internal delegate bool KnownApiLoweringHandler(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition);

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
                string.Equals(method.ContainingType?.ToDisplayString(), ContainingTypeMetadataName, StringComparison.Ordinal);
        }
    }
}
