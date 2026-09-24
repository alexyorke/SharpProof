namespace SharpProof.Effects;

internal sealed class ReadRegionFlowCaptures
{
    private readonly Dictionary<CaptureId, List<IOperation>> _captures = [];

    internal void Record(IFlowCaptureOperation capture)
    {
        if (!_captures.TryGetValue(capture.Id, out var values))
        {
            values = [];
            _captures.Add(capture.Id, values);
        }

        if (!values.Any(value => ReferenceEquals(value, capture.Value)))
        {
            values.Add(capture.Value);
        }
    }

    internal bool TryResolve(
        IFlowCaptureReferenceOperation capture,
        out IReadOnlyList<IOperation> values)
    {
        if (_captures.TryGetValue(capture.Id, out var captures))
        {
            values = captures;
            return true;
        }

        values = [];
        return false;
    }
}
