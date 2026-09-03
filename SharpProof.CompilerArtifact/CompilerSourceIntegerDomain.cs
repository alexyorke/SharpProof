using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

internal static class CompilerSourceIntegerDomain
{
    internal static bool Contains(
        CompilerIntegerInterval? interval,
        IrValue value)
    {
        return interval is not { } bounds ||
            value.Kind == IrValueKind.Integer &&
            value.Integer >= bounds.Minimum && value.Integer <= bounds.Maximum;
    }
}
