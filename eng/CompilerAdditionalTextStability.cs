using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.CompilerSupport;

internal static class CompilerAdditionalTextStability
{
    private const string CommandLineAdditionalTextTypeName =
        "Microsoft.CodeAnalysis.AdditionalTextFile";

    internal static SourceText GetStableAdditionalText(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        var providerType = file.GetType();
        if (providerType.Assembly != typeof(AdditionalText).Assembly ||
            !string.Equals(
                providerType.FullName,
                CommandLineAdditionalTextTypeName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An additional file does not expose a stable compiler input snapshot.");
        }

        return file.GetText(cancellationToken) ??
            throw new InvalidOperationException(
                "An additional file has no compiler text.");
    }
}
