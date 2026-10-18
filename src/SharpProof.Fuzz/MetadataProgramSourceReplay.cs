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

internal sealed record MetadataProgramSourceCase(string LibraryMember, string SourceMember, object Input)
{
    public string LibraryMember { get; init; } = LibraryMember ?? throw new System.ArgumentNullException(nameof(LibraryMember));
    public string SourceMember { get; init; } = SourceMember ?? throw new System.ArgumentNullException(nameof(SourceMember));
    public object Input { get; init; } = Input ?? throw new System.ArgumentNullException(nameof(Input));
}

internal sealed record MetadataProgramSourceResult(int Cases, int Agreements, int NativeProofs,
    int NativeRefutations, ImmutableArray<FuzzFailure> Failures)
{
    public ImmutableArray<FuzzFailure> Failures { get; init; } = Failures.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Failures)) : Failures;
}

internal sealed record MetadataCallablePremise(string CallableId, bool HasTotal, bool HasResult)
{
    public string CallableId { get; init; } = CallableId ?? throw new System.ArgumentNullException(nameof(CallableId));
}

internal sealed class MetadataSourceCompilation : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("sharpproof-metadata-fuzz-");
    private readonly AssemblyLoadContext runtime = new("MetadataProgramFuzz", isCollectible: true);
    private readonly ImmutableArray<string> libraryMembers;
    private readonly ImmutableArray<string> sourceMembers;
    private MemoryStream? libraryImage;
    private MemoryStream? sourceImage;

    internal MetadataSourceCompilation(IReadOnlyList<MetadataProgramSourceCase> cases)
    {
        cases = RepositoryParameterGuard.NotNull(cases, nameof(cases));
        libraryMembers = cases.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.LibraryMember)).ToImmutableArray();
        sourceMembers = cases.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.SourceMember)).ToImmutableArray();
    }

    internal Type Subject { get; private set; } = null!;
    internal Stopwatch Stage { get; private set; } = null!;
    internal ImmutableArray<CompilerCallablePreparation> Preparations { get; private set; }
    internal ImmutableArray<MetadataCallablePremise> Premises =>
        [.. Preparations.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (new MetadataCallablePremise(item.Entry.CallableId,
            item.Total != null, item.Total?.Result != null)))];

    internal async Task CompileAsync(CancellationToken cancellationToken,
        Action<string, long>? timingSink = null)
    {
        Stage = Stopwatch.StartNew();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release, deterministic: true);
        var librarySource = "public static class MetadataLibrary { " + string.Join("\n", libraryMembers) + " }";
        var library = CSharpCompilation.Create("MetadataLibrary", [Parse(librarySource, "Library.cs")],
            TestMetadataReferences.SortedDistinctPlatform, options);
        libraryImage = MetadataProgramDifferentialOracle.Emit(library, cancellationToken);
        var imagePath = Path.Combine(directory.FullName, "MetadataLibrary.dll");
        await File.WriteAllBytesAsync(imagePath, libraryImage.ToArray(), cancellationToken);
        runtime.LoadFromStream(libraryImage);
        var source = "using SharpProof.Attributes; public static class MetadataSubject { " + string.Join("\n", sourceMembers) + " }";
        var compilation = CSharpCompilation.Create("MetadataSubject", [Parse(source, Path.Combine(directory.FullName, "Subject.cs"))],
            TestMetadataReferences.WithAdditionalPaths([typeof(SharpProof.Attributes.Contract).Assembly.Location], sort: true)
                .Add(MetadataReference.CreateFromFile(imagePath)), options);
        sourceImage = MetadataProgramDifferentialOracle.Emit(compilation, cancellationToken);
        Subject = runtime.LoadFromStream(sourceImage).GetType("MetadataSubject")!;
        timingSink?.Invoke("compile", Stage.ElapsedMilliseconds);
        Stage.Restart();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, directory.FullName, "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken), out var preparations, cancellationToken);
        Preparations = preparations;
        SyntaxTree Parse(string text, string path)
        {
            path = RepositoryParameterGuard.NotNull(path, nameof(path));
            text = RepositoryParameterGuard.NotNull(text, nameof(text));
            return CSharpSyntaxTree.ParseText(text,
                new CSharpParseOptions(LanguageVersion.CSharp12), path, cancellationToken: cancellationToken);
        }
    }

    public void Dispose()
    {
        sourceImage?.Dispose();
        libraryImage?.Dispose();
        runtime.Unload();
        directory.Delete(recursive: true);
    }
}

internal sealed class MetadataProgramSourceReplay
{
    private readonly int seed;
    private readonly Action<string, long>? timingSink;
    private readonly ImmutableArray<FuzzFailure>.Builder failures = ImmutableArray.CreateBuilder<FuzzFailure>();
    private int totalCases;
    private int agreements;
    private int proofs;
    private int refutations;

    internal MetadataProgramSourceReplay(int seed, Action<string, long>? timingSink = null)
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        this.seed = seed;
        this.timingSink = timingSink;
    }

    internal MetadataProgramSourceResult Result => new(totalCases, agreements, proofs,
        refutations, failures.ToImmutable());

    internal async Task CompareBatchAsync(IReadOnlyList<MetadataProgramSourceCase> cases,
        int offset, CancellationToken cancellationToken)
    {
        cases = RepositoryParameterGuard.NotNull(cases, nameof(cases));
        var count = cases.Count;
        var libraryMembers = cases.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.LibraryMember)).ToArray();
        var sourceMembers = cases.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.SourceMember)).ToArray();
        var inputs = cases.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Input)).ToArray();
        totalCases += count;
        using var actual = new MetadataSourceCompilation(cases);
        await actual.CompileAsync(cancellationToken, timingSink);
        var subject = actual.Subject;
        var stage = actual.Stage;
        var preparations = actual.Preparations;
        if (preparations.Length != count)
        { throw new InvalidOperationException("Metadata case universe does not match collected callables."); }
        var owned = preparations.ToDictionary(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId), StringComparer.Ordinal);
        timingSink?.Invoke("artifact", stage.ElapsedMilliseconds);
        stage.Restart();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = "Target" + (offset + index).ToString(CultureInfo.InvariantCulture);
            void Fail(string detail)
            {
                detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
                if (failures.Count < FuzzRunner.MaximumRetainedFailures)
                {
                    var reproducer = libraryMembers[index] + "\n" + sourceMembers[index] + "\n// input: " + JsonSerializer.Serialize(inputs[index]);
                    failures.Add(new(offset + index, seed, "metadata-program", reproducer, reproducer, detail));
                }
            }
            var preparation = owned.Values.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId.Contains("MetadataSubject." + name + "(", StringComparison.Ordinal)));
            if (preparation.Total is not { } total || total.Result is null)
            { Fail(name + ": incomplete metadata artifact"); continue; }
            object? observed;
            var method = subject.GetMethod(name)
                ?? throw new InvalidOperationException("Compiled metadata subject lacks method " + name + ".");
            try
            { observed = method.Invoke(null, [inputs[index]]); }
            catch (TargetInvocationException exception)
            { Fail(name + ": unexpected compiled fault " + exception.InnerException?.GetType().Name); continue; }
            var factory = total.Program.Factory;
            var parameter = total.Parameters.Single();
            var entries = new Dictionary<IrVarId, IrValue>
            {
                [parameter.Entry] = TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(parameter.Entry).Type, inputs[index], [])
            };
            var expected = TotalProgramDifferentialOracle.Value(factory, factory.GetVariableInfo(total.Result!.Value).Type, observed, []);
            var execution = new IrProgramInterpreter(factory).Execute(total.Program, entries, maximumSteps: FuzzExecutionDefaults.MaximumReplaySteps, cancellationToken: cancellationToken);
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
}
