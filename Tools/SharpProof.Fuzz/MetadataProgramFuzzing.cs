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

    internal static async Task<MetadataProgramFuzzResult> RunAsync(int cases, int seed,
        Action<string, long>? timingSink = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cases, 1);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var random = new Random(seed);
        var failures = ImmutableArray.CreateBuilder<FuzzFailure>();
        var agreements = 0;
        var proofs = 0;
        var refutations = 0;
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
            var directory = Directory.CreateTempSubdirectory("sharpproof-metadata-fuzz-");
            var runtime = new AssemblyLoadContext("MetadataProgramFuzz", isCollectible: true);
            try
            {
                var stage = Stopwatch.StartNew();
                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release, deterministic: true);
                var librarySource = "public static class MetadataLibrary { " + string.Join("\n", libraryMembers) + " }";
                var library = CSharpCompilation.Create("MetadataLibrary", [Parse(librarySource, "Library.cs")],
                    TestMetadataReferences.SortedDistinctPlatform, options);
                using var libraryImage = Emit(library, cancellationToken);
                var imagePath = Path.Combine(directory.FullName, "MetadataLibrary.dll");
                await File.WriteAllBytesAsync(imagePath, libraryImage.ToArray(), cancellationToken);
                runtime.LoadFromStream(libraryImage);
                var source = "using SharpProof.Attributes; public static class MetadataSubject { " + string.Join("\n", sourceMembers) + " }";
                var compilation = CSharpCompilation.Create("MetadataSubject", [Parse(source, Path.Combine(directory.FullName, "Subject.cs"))],
                    TestMetadataReferences.WithAdditionalPaths([typeof(SharpProof.Attributes.Contract).Assembly.Location], sort: true)
                        .Add(MetadataReference.CreateFromFile(imagePath)), options);
                using var sourceImage = Emit(compilation, cancellationToken);
                var subject = runtime.LoadFromStream(sourceImage).GetType("MetadataSubject")!;
                timingSink?.Invoke("compile", stage.ElapsedMilliseconds);
                stage.Restart();
                var artifact = CompilerManifestArtifactProducer.Create(compilation, directory.FullName, "net9.0",
                    WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
                    WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
                CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken), out var preparations, cancellationToken);
                if (preparations.Length != count)
                { throw new InvalidOperationException("Metadata case universe does not match collected callables."); }
                var owned = preparations.ToDictionary(item => item.Entry.CallableId, StringComparer.Ordinal);
                timingSink?.Invoke("artifact", stage.ElapsedMilliseconds);
                stage.Restart();
                for (var index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = "Target" + (offset + index).ToString(CultureInfo.InvariantCulture);
                    void Fail(string detail)
                    {
                        if (failures.Count < FuzzRunner.MaximumRetainedFailures)
                        {
                            var reproducer = libraryMembers[index] + "\n" + sourceMembers[index] + "\n// input: " + JsonSerializer.Serialize(inputs[index]);
                            failures.Add(new(offset + index, seed, "metadata-program", reproducer, reproducer, detail));
                        }
                    }
                    var preparation = owned.Values.Single(item => item.Entry.CallableId.Contains("MetadataSubject." + name + "(", StringComparison.Ordinal));
                    if (preparation.Total is not { } total)
                    { Fail(name + ": incomplete metadata artifact"); continue; }
                    object? observed;
                    try
                    { observed = subject.GetMethod(name)!.Invoke(null, [inputs[index]]); }
                    catch (TargetInvocationException exception)
                    { Fail(name + ": unexpected compiled fault " + exception.InnerException?.GetType().Name); continue; }
                    var factory = total.Program.Factory;
                    var parameter = total.Parameters.Single();
                    var entries = new Dictionary<IrVarId, IrValue>
                    {
                        [parameter.Entry] = TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(parameter.Entry).Type, inputs[index], [])
                    };
                    var expected = TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(total.Result!.Value).Type, observed, []);
                    var execution = new IrProgramInterpreter(factory).Execute(total.Program, entries, cancellationToken: cancellationToken);
                    if (execution.Status != IrProgramExecutionStatus.Returned || execution.ConsumedApproximation ||
                        !TotalProgramDifferentialOracle.Equal(execution.ReturnValue!, expected))
                    { Fail(name + ": compiled/original disagreement"); continue; }
                    var site = factory.CreateOperation("metadata-fuzz-postcondition");
                    var goal = factory.Binary(IrBinaryOperator.Equal, factory.Variable(total.Result.Value), TotalProgramDifferentialOracle.Term(factory, expected));
                    var candidate = new PassiveCallableCandidate(name, total.Program,
                        [new(parameter.Entry, parameter.Current, parameter.Old)], total.Result,
                        [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(parameter.Entry), TotalProgramDifferentialOracle.Term(factory, entries[parameter.Entry])), factory.Boolean(true), site)],
                        [new(goal, factory.Boolean(true), site), new(factory.Unary(IrUnaryOperator.Not, goal), factory.Boolean(true), site)]);
                    if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken))
                    { Fail(name + ": native admission " + reason); continue; }
                    using var solver = new PassiveCallableSolver(plan!);
                    var proven = await solver.VerifyEnsuresAsync(0, cancellationToken);
                    var refuted = await solver.VerifyEnsuresAsync(1, cancellationToken);
                    if (proven.Outcome is ProvenOutcome)
                    { proofs++; }
                    if (refuted.Outcome is RefutedOutcome)
                    { refutations++; }
                    if (proven.Outcome is not ProvenOutcome || refuted.Outcome is not RefutedOutcome ||
                        refuted.EntryModel.Count != 1 || !refuted.EntryModel.ContainsKey(parameter.Entry))
                    { Fail(name + ": native agreement " + proven.Reason + "/" + refuted.Reason); continue; }
                    agreements++;
                }
                timingSink?.Invoke("verification", stage.ElapsedMilliseconds);
            }
            finally
            {
                runtime.Unload();
                directory.Delete(recursive: true);
            }
        }
        return new(cases, agreements, proofs, refutations, typeMask, recipeMask, failures.ToImmutable());

        SyntaxTree Parse(string text, string path)
        {
            return CSharpSyntaxTree.ParseText(text,
                new CSharpParseOptions(LanguageVersion.CSharp12), path, cancellationToken: cancellationToken);
        }
    }

    private static MemoryStream Emit(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var image = new MemoryStream();
        var result = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!result.Success)
        {
            image.Dispose();
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }
        image.Position = 0;
        return image;
    }
}
