using Microsoft.CodeAnalysis;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    internal static class PurelySharpVerifierReferences
    {
        internal static MetadataReference AnalyzerReference { get; } =
            MetadataReference.CreateFromFile(typeof(PurelySharpAnalyzer).Assembly.Location);

        internal static MetadataReference EnforcePureAttributeReference { get; } =
            MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location);

        internal static MetadataReference PureAttributeReference { get; } =
            MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.PureAttribute).Assembly.Location);
    }
}
