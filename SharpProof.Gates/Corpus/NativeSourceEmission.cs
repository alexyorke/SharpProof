using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Gates.Corpus;

// The caller owns the returned image. Emit options and diagnostics come from
// the original compilation; this helper does not validate any worker claim.
internal static class NativeSourceEmission
{
    internal static MemoryStream EmitImage(
        CSharpCompilation compilation,
        string errorPrefix,
        CancellationToken cancellationToken)
    {
        var image = new MemoryStream();
        try
        {
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            {
                throw new InvalidDataException(
                    errorPrefix + string.Join("; ", emitted.Diagnostics.Take(5)));
            }
            image.Position = 0;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
