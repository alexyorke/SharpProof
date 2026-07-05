using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;
using System;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal interface IPurityRule
    {

        PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState);


        IEnumerable<OperationKind> ApplicableOperationKinds { get; }
    }
}