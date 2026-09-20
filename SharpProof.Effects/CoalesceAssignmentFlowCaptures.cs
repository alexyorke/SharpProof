using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class CoalesceAssignmentFlowCaptures : OperationFlowCaptures
{
    protected override bool IsRelevant(IFlowCaptureOperation capture)
    {
        // A multi-block assignment captures its lvalue before evaluating the
        // RHS. Keep that capture-to-storage mapping for every assignment;
        // captures for conditional RHS values lie outside the left span and
        // must continue to join as ordinary values.
        var captureSpan = capture.Syntax.Span;
        return capture.Syntax.AncestorsAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left.Span.Contains(captureSpan));
    }
}
