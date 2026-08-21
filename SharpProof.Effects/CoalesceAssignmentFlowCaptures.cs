using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class CoalesceAssignmentFlowCaptures
{
    private readonly HashSet<CaptureId> _ambiguous = [];
    private readonly Dictionary<CaptureId, IOperation> _capturedValues = [];

    internal void Record(IFlowCaptureOperation capture)
    {
        if (!IsCoalesceAssignmentCapture(capture))
        {
            return;
        }

        if (_capturedValues.TryGetValue(capture.Id, out var existing))
        {
            if (!ManagedFlowResult.HasSameIdentity(existing, capture.Value))
            {
                _ambiguous.Add(capture.Id);
            }
            return;
        }

        _capturedValues.Add(capture.Id, capture.Value);
    }

    internal IOperation Resolve(IOperation operation)
    {
        var seen = new HashSet<CaptureId>();
        while (operation is IFlowCaptureReferenceOperation capture &&
               seen.Add(capture.Id) &&
               !_ambiguous.Contains(capture.Id) &&
               _capturedValues.TryGetValue(capture.Id, out var captured))
        {
            operation = captured;
        }

        return operation;
    }

    internal bool TryResolve(
        IFlowCaptureReferenceOperation capture,
        out IOperation resolved)
    {
        resolved = Resolve(capture);
        return resolved is not IFlowCaptureReferenceOperation;
    }

    private static bool IsCoalesceAssignmentCapture(
        IFlowCaptureOperation capture)
    {
        return capture.Syntax.AncestorsAndSelf()
            .Any(static syntax => syntax is AssignmentExpressionSyntax
            {
                RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind
                    .CoalesceAssignmentExpression
            });
    }
}
