using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerDependencyEvidenceFormatter
{
    internal static string Format(
        ImmutableArray<CompilerPreparedSummaryEvidence> evidence,
        bool throwOnUnsupportedOrigin)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var values = evidence.Select(item =>
        {
            var prefix = CompilerSpecificationPackAuthorityValidation
                .GetSummaryPrefix(item.Origin);
            if (prefix == null)
            {
                if (throwOnUnsupportedOrigin)
                {
                    throw new InvalidDataException(
                        "A summary dependency has an unsupported origin.");
                }

                return string.Empty;
            }

            var evidencePrefix = item.Origin ==
                    CompilerSummaryOrigin.SpecificationPack
                ? prefix + ":" + item.EvidenceIdentity
                : prefix;
            return evidencePrefix + ":" + item.CallIdentity + ":" +
                item.EvidenceSha256;
        });
        return ":deps=" + string.Join(";", values);
    }
}
