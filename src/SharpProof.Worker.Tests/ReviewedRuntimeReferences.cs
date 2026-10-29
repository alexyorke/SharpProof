using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Attributes;

namespace SharpProof.Worker.Test;

// The net8.0 reference pack that the reviewed runtime-layout tests compile
// against. Loading it once lets their compilations share one set of reference
// images instead of reading every reference assembly again for each test.
internal static class ReviewedRuntimeReferences
{
    private const string PackDirectory =
        "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";

    private static readonly Lazy<ImmutableArray<MetadataReference>> s_net8 = new(static () =>
        [.. Directory.GetFiles(PackDirectory, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : (MetadataReference)MetadataReference.CreateFromFile(path))]);

    internal static ImmutableArray<MetadataReference> Net8 => s_net8.Value;
}
