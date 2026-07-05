using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{

	internal class RangeOperationPurityRule : IPurityRule
	{
		public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Range);

		public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
		{
			// Roslyn exposes the C# range ("start..end") as OperationKind.Range.
			// Range construction is pure if its endpoints are pure. Check them when available.
			if (operation is IRangeOperation rangeOp)
			{
				if (rangeOp.LeftOperand != null)
				{
					var leftResult = PurityAnalysisEngine.CheckSingleOperation(rangeOp.LeftOperand, context, currentState);
					if (!leftResult.IsPure)
					{
						return leftResult;
					}
				}
				if (rangeOp.RightOperand != null)
				{
					var rightResult = PurityAnalysisEngine.CheckSingleOperation(rangeOp.RightOperand, context, currentState);
					if (!rightResult.IsPure)
					{
						return rightResult;
					}
				}
				return PurityAnalysisEngine.PurityAnalysisResult.Pure;
			}

			// If the specific interface isn't available in this Roslyn version, default to pure and
			// rely on other rules to analyze any constituent expressions.
			return PurityAnalysisEngine.PurityAnalysisResult.Pure;
		}
	}
}


