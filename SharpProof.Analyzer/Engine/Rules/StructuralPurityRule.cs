using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class StructuralPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
            OperationKind.Block,
            OperationKind.MethodBodyOperation,
            OperationKind.AnonymousFunction,
            OperationKind.FlowAnonymousFunction,
            OperationKind.LocalFunction,
            OperationKind.Try,
            OperationKind.CatchClause,
            OperationKind.VariableDeclarationGroup,
            OperationKind.VariableDeclaration,
            OperationKind.VariableDeclarator,
            OperationKind.VariableInitializer,
            OperationKind.Argument,
            OperationKind.Labeled,
            OperationKind.Empty,
            OperationKind.FieldInitializer,
            OperationKind.PropertyInitializer
            );

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }
    }
}
