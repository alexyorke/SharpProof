#nullable enable
namespace SharpProof.Worker;

internal static class WorkerProjections
{
    internal static (string Kind, string Value) FormatTotalValue(IrValue value)
    {
        // The protocol requires a nonblank display value. A quoted string also
        // preserves empty strings, whitespace, control characters and UTF-16.
        if (value.Kind != IrValueKind.String)
        { return FormatValue(value); }
        var display = new System.Text.StringBuilder().Append('"');
        foreach (var character in value.String)
        {
            if (character == '"' || character == '\\')
            { display.Append('\\').Append(character); }
            else if (character < ' ' || char.IsSurrogate(character))
            { display.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)); }
            else
            { display.Append(character); }
        }
        return (nameof(IrValueKind.String), display.Append('"').ToString());
    }

    internal static (string Kind, string Value) FormatValue(
        IrValue value
    )
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return value.Kind switch
        {
            IrValueKind.Boolean => (nameof(IrValueKind.Boolean), value.Boolean ? "true" : "false"),
            IrValueKind.Integer => (nameof(IrValueKind.Integer), value.IntegerNumericValue.ToString(CultureInfo.InvariantCulture)),
            IrValueKind.String => (nameof(IrValueKind.String), value.String),
            IrValueKind.Null => (nameof(IrValueKind.Null), "null"),
            IrValueKind.Reference => (nameof(IrValueKind.Reference), "<opaque>"),
            // The remaining current owned kind is Sequence; kind extensions require a producer-domain review.
            _ => (nameof(IrValueKind.Sequence), "<opaque>")
        };
    }

    internal static WorkerClaimReason MapAbstention(
        AbstentionReason reason
    )
    {
        return reason switch
        {
            AbstentionReason.ResourceLimit => WorkerClaimReason.ResourceLimit,
            AbstentionReason.Timeout => WorkerClaimReason.MethodTimeout,
            AbstentionReason.BackendUnavailable => WorkerClaimReason.BackendUnavailable,
            AbstentionReason.SolverIncomplete => WorkerClaimReason.SolverIncomplete,
            AbstentionReason.InfrastructureFailure => WorkerClaimReason.InfrastructureFailure,
            AbstentionReason.MalformedBackendResult => WorkerClaimReason.MalformedBackendResult,
            AbstentionReason.CounterexampleReplayFailed => WorkerClaimReason.CounterexampleReplayFailed,
            AbstentionReason.CounterexampleNotReplayable => WorkerClaimReason.CounterexampleNotReplayable,
            AbstentionReason.PostconditionMayBeUndefined => WorkerClaimReason.PostconditionMayBeUndefined,
            _ => WorkerClaimReason.UnsupportedExpression
        };
    }

}
