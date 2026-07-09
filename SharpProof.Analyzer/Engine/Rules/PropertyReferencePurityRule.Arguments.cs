using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class PropertyReferencePurityRule
    {

        private static PurityAnalysisEngine.PurityAnalysisResult CheckArguments(
            IPropertyReferenceOperation propertyReferenceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (propertyReferenceOperation.Instance != null)
            {
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                    propertyReferenceOperation.Instance,
                    context,
                    currentState);
                if (!instanceResult.IsPure)
                {
                    return instanceResult;
                }
            }

            foreach (var argument in propertyReferenceOperation.Arguments)
            {
                if (argument.Value == null)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(argument.Syntax);
                }

                var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentResult.IsPure)
                {
                    return argumentResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
