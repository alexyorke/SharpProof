namespace SharpProof.Effects;

internal sealed class CreationFlowCaptures
{
    private readonly HashSet<CaptureId> _ambiguous = [];
    private readonly Dictionary<CaptureId, EffectRegionSet> _regions = [];

    internal void Record(IFlowCaptureOperation capture)
    {
        var value = capture.Value;
        while (value is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion)
        {
            value = conversion.Operand;
        }

        if (value is not (IObjectCreationOperation or IArrayCreationOperation))
        {
            // A later definition can reuse this capture ID at a control-flow
            // merge. Remember non-creation provenance even when no fresh
            // definition has been seen yet.
            _ambiguous.Add(capture.Id);
            return;
        }

        var region = EffectRegionSet.Create(
            EffectRegionId.Fresh(value.Syntax.SpanStart));
        if (_regions.TryGetValue(capture.Id, out var existing))
        {
            if (existing != region)
            {
                _ambiguous.Add(capture.Id);
            }
            return;
        }

        _regions.Add(capture.Id, region);
    }

    internal bool TryResolve(
        IFlowCaptureReferenceOperation capture,
        out EffectRegionSet region)
    {
        if (!_ambiguous.Contains(capture.Id) &&
            _regions.TryGetValue(capture.Id, out region))
        {
            return true;
        }

        region = EffectRegionSet.Unknown;
        return false;
    }
}
