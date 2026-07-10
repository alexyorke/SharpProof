using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class PropertyDispatchHelper
{
    internal static INamedTypeSymbol? GetKnownReceiverType(IOperation? instanceOperation)
    {
        var unwrapped = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation);
        if (unwrapped is IObjectCreationOperation objectCreationOperation)
            return objectCreationOperation.Type as INamedTypeSymbol;

        return unwrapped?.Type as INamedTypeSymbol;
    }
}