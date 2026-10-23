using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

// Descriptive metadata only; neither field supplies proof authority.
internal sealed record WorkerCallablePresentation
{
    [JsonRequired] public string Name { get; init; } = string.Empty;
    [JsonRequired]
    [JsonConverter(typeof(ProtocolArrayJsonConverter))]
    public ImmutableArray<string> ParameterNames { get; init; } = [];
}

internal sealed record WorkerDiagnosticContext
{
    [JsonRequired] public string Stage { get; init; } = "CompilerLowering";
    [JsonRequired] public string Code { get; init; } = string.Empty;
    [JsonRequired] public WorkerSourceLocation Location { get; init; } = new();
}

internal static partial class WorkerProtocolJson
{
    internal static bool IsPresentationValid(WorkerCallablePresentation? presentation)
    {
        if (presentation is null)
        { return true; }
        if (string.IsNullOrWhiteSpace(presentation.Name) || !IsSingleLineText(presentation.Name) ||
            presentation.ParameterNames.IsDefault)
        { return false; }
        foreach (var name in presentation.ParameterNames)
        {
            if (string.IsNullOrWhiteSpace(name) || !IsSingleLineText(name))
            { return false; }
        }
        return true;
    }

    internal static bool IsDiagnosticContextValid(WorkerDiagnosticContext? detail)
    {
        if (detail is null)
        { return true; }
        return detail.Stage == "CompilerLowering" && detail.Code is { Length: > 0 and <= 128 } &&
            detail.Code.All(static character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_') &&
            HasValidLocation(detail.Location);
    }
}
