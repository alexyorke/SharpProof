using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

internal static class OperationUnwrapping
{
    internal static IOperation? Unwrap(
        IOperation? operation,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IConversionOperation { OperatorMethod: null } conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }
}
