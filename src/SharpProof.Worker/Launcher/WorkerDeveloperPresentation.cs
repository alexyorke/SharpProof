using System.Globalization;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static partial class LauncherPresentation
{
    internal static string VariableName(WorkerClaimResult result, string variable)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(variable);
        const string prefix = "parameter:";
        if (result.Presentation is { } presentation && variable.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(variable.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
            ordinal >= 0 && ordinal < presentation.ParameterNames.Length)
        {
            return presentation.ParameterNames[ordinal];
        }
        return variable;
    }
}
