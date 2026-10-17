using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Fuzz;

public sealed record MetadataProgramFuzzCoverage(int Cases, int Agreements, int NativeProofs,
    int NativeRefutations, int TypeMask, int RecipeMask)
{
    [JsonIgnore]
    public bool HasValidCounts => Cases > 0 && Agreements >= 0 && Agreements <= Cases &&
        NativeProofs >= Agreements && NativeProofs <= Cases && NativeRefutations >= Agreements && NativeRefutations <= Cases &&
        TypeMask is > 0 and <= 1023 && RecipeMask is > 0 and <= 127;
    [JsonIgnore]
    public bool HasExpandedCategories => TypeMask == 1023 && RecipeMask == 127;
}

internal sealed record MetadataProgramFuzzResult(int Cases, int Agreements, int NativeProofs,
    int NativeRefutations, int TypeMask, int RecipeMask, ImmutableArray<FuzzFailure> Failures)
{
    internal MetadataProgramFuzzCoverage Coverage => new(Cases, Agreements, NativeProofs, NativeRefutations, TypeMask, RecipeMask);
    internal bool Passed => Coverage.HasValidCounts && Agreements == Cases && Failures.IsEmpty;
}

// Qualify the implementation-IL route through its real producer and decoder.
// Generated library bodies have no exception regions; caller handlers remain
// source IR. This does not qualify reference IL or module initialization.
internal static class MetadataProgramDifferentialOracle
{
    private const int CompilationBatchSize = 280;

    internal static async Task<MetadataProgramFuzzResult> RunAsync(int cases, int seed, CancellationToken cancellationToken,
        Action<string, long>? timingSink = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cases, 1);
        var replay = new MetadataProgramSourceReplay(seed, timingSink);
        var random = new Random(seed);
        var typeMask = 0;
        var recipeMask = 0;
        for (var offset = 0; offset < cases; offset += CompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(CompilationBatchSize, cases - offset);
            var libraryMembers = new string[count];
            var sourceMembers = new string[count];
            var inputs = new object[count];
            for (var index = 0; index < count; index++)
            {
                var ordinal = offset + index;
                var name = ordinal.ToString(CultureInfo.InvariantCulture);
                var typeIndex = ordinal % TotalProgramDifferentialOracle.Types.Length;
                var type = TotalProgramDifferentialOracle.Types[typeIndex];
                var recipe = ordinal / TotalProgramDifferentialOracle.Types.Length % 7;
                typeMask |= 1 << typeIndex;
                recipeMask |= 1 << recipe;
                inputs[index] = TotalProgramDifferentialOracle.Input(type, random.NextInt64(), ordinal);
                var expression = type == "bool" ? recipe == 4 ? $"Again{name}(value)" : recipe % 2 == 0 ? "!value" : "value == false" : recipe switch
                {
                    0 => $"unchecked(({type})(value + 1))",
                    1 => $"checked(({type})(value - 1))",
                    2 => $"unchecked(({type})(value * 3))",
                    3 => $"value > 0 ? unchecked(({type})(value + 2)) : unchecked(({type})(value - 2))",
                    4 => $"Again{name}(value)",
                    5 => $"unchecked(({type})(value / (value - value)))",
                    _ => $"unchecked(({type})(value % (value - value)))"
                };
                libraryMembers[index] = $"public static {type} M{name}({type} value) {{ return {expression}; }} " +
                    $"private static {type} Again{name}({type} value) {{ return " +
                    (type == "bool" ? "!value" : $"checked(({type})(value + 1))") + "; }";
                sourceMembers[index] = $"public static {type} Target{name}({type} x) {{ Contract.Ensures(true); " +
                    $"try {{ return MetadataLibrary.M{name}(x); }} catch (System.OverflowException) {{ return x; }} " +
                    "catch (System.DivideByZeroException) { return x; } }";
            }
            var batch = libraryMembers.Select((member, index) =>
                new MetadataProgramSourceCase(member, sourceMembers[index], inputs[index])).ToArray();
            await replay.CompareBatchAsync(batch, offset, cancellationToken);
        }
        var result = replay.Result;
        return new(cases, result.Agreements, result.NativeProofs, result.NativeRefutations, typeMask, recipeMask, result.Failures);

    }

    internal static MemoryStream Emit(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var (image, result) = GeneratedCompilationEmitter.Emit(compilation, cancellationToken);
        if (!result.Success)
        {
            image.Dispose();
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }
        image.Position = 0;
        return image;
    }
}
