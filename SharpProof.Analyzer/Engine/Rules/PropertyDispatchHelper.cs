using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal static class PropertyDispatchHelper
    {
        internal static INamedTypeSymbol? GetKnownReceiverType(IOperation? instanceOperation)
        {
            var unwrapped = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation);
            if (unwrapped is IObjectCreationOperation objectCreationOperation)
            {
                return objectCreationOperation.Type as INamedTypeSymbol;
            }

            return unwrapped?.Type as INamedTypeSymbol;
        }

    }
}
