using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class ConditionalTruthOperatorFlowCaptures
{
    private readonly HashSet<CaptureId> _ambiguous = [];
    private readonly Dictionary<CaptureId, IOperation> _capturedValues = [];

    internal void Record(IFlowCaptureOperation capture)
    {
        if (!capture.Syntax.AncestorsAndSelf().Any(static syntax => syntax is
            BinaryExpressionSyntax
            {
                RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression or
                    (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression
            }))
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

    internal bool TryResolve(
        IFlowCaptureReferenceOperation capture,
        out IOperation resolved)
    {
        var seen = new HashSet<CaptureId>();
        resolved = capture;
        while (resolved is IFlowCaptureReferenceOperation reference &&
               seen.Add(reference.Id) &&
               !_ambiguous.Contains(reference.Id) &&
               _capturedValues.TryGetValue(reference.Id, out var captured))
        {
            resolved = captured;
        }

        return resolved is not IFlowCaptureReferenceOperation;
    }
}
