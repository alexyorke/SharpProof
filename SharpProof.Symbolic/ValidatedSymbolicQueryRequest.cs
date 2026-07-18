using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class ValidatedSymbolicQueryRequest
{
    private ValidatedSymbolicQueryRequest(
        SymbolicSourceInput source,
        SharpProofTarget target,
        SymbolicQueryOptions options)
    {
        Source = source;
        Target = target;
        Options = options;
    }

    internal SymbolicSourceInput Source { get; }

    internal SharpProofTarget Target { get; }

    internal SymbolicQueryOptions Options { get; }

    internal static ValidatedSymbolicQueryRequest Create(SymbolicQueryContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        return new ValidatedSymbolicQueryRequest(context.Source, context.Target, context.Options);
    }

    internal SmtAnalysisService RequireSmt(string message)
    {
        return Options.SmtAnalysis ?? throw new ArgumentException(message, "context");
    }

    internal void RequireTarget(
        Func<SharpProofTargetKind, bool> predicate,
        string message)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        if (!predicate(Target.Kind)) throw new ArgumentException(message, "context");
    }
}
