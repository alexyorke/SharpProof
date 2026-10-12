using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SharpProof.Fuzz;

internal static class GeneratedCompilationEmitter
{
    internal static (MemoryStream Image, EmitResult Emit) Emit(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var image = new MemoryStream();
        try
        {
            var emit = compilation.Emit(
                image, cancellationToken: cancellationToken);
            return (image, emit);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
