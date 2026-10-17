using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Smt.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class NativeDispatchIsolationTests : IDisposable
{
    private TempDirectory? directory;
    private string? runtimeDirectory;

    [OneTimeSetUp]
    public void PrepareNativeChild()
    {
        TestRepository.RequireCanonicalContainer();
        directory = new TempDirectory("SharpProof-native-dispatch-");
        runtimeDirectory = Path.Combine(directory.FullName, "runtime");
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(runtimeDirectory, Path.GetRelativePath(AppContext.BaseDirectory, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Concat([typeof(IrFactory).Assembly.Location, typeof(VerificationQuery).Assembly.Location,
                typeof(CallableSolverSession).Assembly.Location, typeof(SharpProof.Host.ContainerNativeLibrary).Assembly.Location,
                typeof(Microsoft.Z3.Context).Assembly.Location])
            .Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "SharpProof.Smt.DispatchChild",
            [CSharpSyntaxTree.ParseText(ChildSource, new CSharpParseOptions(LanguageVersion.CSharp12))],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var assembly = File.Create(Path.Combine(runtimeDirectory, "SharpProof.Smt.DispatchChild.dll"));
        var emitted = compilation.Emit(assembly);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        directory?.Dispose();
        directory = null;
        GC.SuppressFinalize(this);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RealNativeQueriesCompleteWithAnOccupiedChildThreadPool(bool occupyPool)
    {
        var start = ProcessRunner.CreateStartInfo(runtimeDirectory!, "dotnet",
        [
            "exec", "--runtimeconfig", Path.Combine(runtimeDirectory!, "SharpProof.Smt.Test.runtimeconfig.json"),
            "--depsfile", Path.Combine(runtimeDirectory!, "SharpProof.Smt.Test.deps.json"),
            Path.Combine(runtimeDirectory!, "SharpProof.Smt.DispatchChild.dll"), occupyPool ? "busy" : "control"
        ]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The native child did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!child.HasExited)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (child.HasExited)
                {
                    // The owned child exited between the lifetime check and Kill.
                }
            }
            await child.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            if (timeout.IsCancellationRequested)
            {
                await TestContext.Progress.WriteLineAsync("Native child stdout: " + await output);
                await TestContext.Progress.WriteLineAsync("Native child stderr: " + await error);
            }
        }

        var capturedOutput = await output;
        var capturedError = await error;
        Assert.That(child.ExitCode, Is.Zero, capturedOutput + Environment.NewLine + capturedError);
        Assert.That(capturedError, Is.Empty);
        using var document = JsonDocument.Parse(capturedOutput);
        var observation = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.GetProperty("OccupiedPool").GetBoolean(), Is.EqualTo(occupyPool));
            Assert.That(observation.GetProperty("Unsatisfiable").GetString(), Is.EqualTo("Unsatisfiable"));
            Assert.That(observation.GetProperty("Satisfiable").GetString(), Is.EqualTo("Satisfiable"));
            Assert.That(observation.GetProperty("ModelValue").GetInt32(), Is.EqualTo(1));
            Assert.That(observation.GetProperty("NativeResources").GetInt64(), Is.GreaterThan(0));
            Assert.That(observation.GetProperty("QueryRlimit").GetUInt32(), Is.EqualTo(IrSmtBackendOptions.DefaultQueryRlimit));
            Assert.That(observation.GetProperty("Proven").GetBoolean(), Is.True);
        }
    }

    private const string ChildSource = """
        #nullable enable
        using System;
        using System.Numerics;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using SharpProof.Host;
        using SharpProof.Ir;
        using SharpProof.Smt;
        using SharpProof.Verify;

        internal static class Program
        {
            private static void Main(string[] args)
            {
                ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
                var occupyPool = args.Length == 1 && args[0] == "busy";
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var x = factory.CreateVariable("x", factory.GetOrCreateIntegerType(32, true));
                var one = factory.Integer(1);
                var equalsOne = factory.Binary(IrBinaryOperator.Equal, factory.Variable(x), one);
                var equalsTwo = factory.Binary(IrBinaryOperator.Equal, factory.Variable(x), factory.Integer(2));
                var assumption = new Assumption(factory, equalsOne, new LoweredJustification(factory.CreateOperation()));
                var valid = new VerificationQuery(factory, [assumption],
                    new Goal(factory, equalsOne, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
                var invalid = new VerificationQuery(factory, [assumption],
                    new Goal(factory, equalsTwo, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
                var options = new IrSmtBackendOptions();
                using var session = new CallableSolverSession(factory, options);
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                using var blockerFinished = new ManualResetEventSlim();
                using var markerFinished = new ManualResetEventSlim();
                ThreadPool.GetMinThreads(out var originalMin, out var originalIoMin);
                ThreadPool.GetMaxThreads(out var originalMax, out var originalIoMax);
                var settingsChanged = false;
                var blockerQueued = false;
                var markerQueued = false;
                Task<BackendCheckResult>? pending = null;
                BackendCheckResult unsatisfiable;
                BackendCheckResult satisfiable;
                try
                {
                    if (occupyPool)
                    {
                        settingsChanged = true;
                        Require(ThreadPool.SetMinThreads(1, originalIoMin), "Cannot set the child minimum worker count.");
                        Require(ThreadPool.SetMaxThreads(1, originalIoMax), "Cannot set the child maximum worker count.");
                        blockerQueued = ThreadPool.QueueUserWorkItem(_ =>
                        {
                            entered.Set();
                            release.Wait();
                            blockerFinished.Set();
                        });
                        Require(blockerQueued, "The child blocker was not queued.");
                        Require(entered.Wait(TimeSpan.FromSeconds(5)), "The child blocker did not enter.");
                        markerQueued = ThreadPool.QueueUserWorkItem(_ => markerFinished.Set());
                        Require(markerQueued, "The child marker was not queued.");
                        Require(!markerFinished.IsSet, "The child pool is not occupied.");
                    }

                    // Main is an OS thread: neither the waits nor cleanup require a pool continuation.
                    pending = session.CheckAsync(valid, CancellationToken.None);
                    Require(pending.Wait(TimeSpan.FromSeconds(5)), "The genuine UNSAT query missed the fixed dispatch bound.");
                    unsatisfiable = pending.GetAwaiter().GetResult();
                    Require(unsatisfiable.Status == BackendCheckStatus.Unsatisfiable &&
                        unsatisfiable.FailureReason == BackendFailureReason.None, "The genuine valid query was not UNSAT.");
                    pending = session.CheckAsync(invalid, CancellationToken.None);
                    Require(pending.Wait(TimeSpan.FromSeconds(5)), "The genuine SAT query missed the fixed dispatch bound.");
                    satisfiable = pending.GetAwaiter().GetResult();
                    Require(satisfiable.Status == BackendCheckStatus.Satisfiable &&
                        satisfiable.FailureReason == BackendFailureReason.None, "The genuine invalid query was not SAT.");
                    Require(satisfiable.Model != null && satisfiable.Model.Assignments[x].IntegerNumericValue == BigInteger.One,
                        "The genuine native model did not assign x=1.");
                    Require(session.ConsumedResourceCount > 0, "No native resource use was recorded.");
                    Require(!occupyPool || !markerFinished.IsSet, "The blocker released before both native queries completed.");
                }
                finally
                {
                    // Always release before waiting for queued native work or disposing its context.
                    release.Set();
                    try
                    {
                        if (blockerQueued)
                        {
                            Require(blockerFinished.Wait(TimeSpan.FromSeconds(5)), "The child blocker did not finish.");
                        }
                        if (markerQueued)
                        {
                            Require(markerFinished.Wait(TimeSpan.FromSeconds(5)), "The child marker did not drain.");
                        }
                        if (pending != null && !pending.IsCompleted)
                        {
                            Require(pending.Wait(TimeSpan.FromSeconds(5)), "The native query did not drain after pool release.");
                        }
                    }
                    finally
                    {
                        if (settingsChanged)
                        {
                            var restoredMax = ThreadPool.SetMaxThreads(originalMax, originalIoMax);
                            var restoredMin = ThreadPool.SetMinThreads(originalMin, originalIoMin);
                            Require(restoredMax && restoredMin, "The child pool settings were not restored.");
                        }
                    }
                }

                var proof = new ProofKernel(session).VerifyAsync(valid, CancellationToken.None).GetAwaiter().GetResult();
                Require(proof is ProvenOutcome, "The real kernel did not validate the native result after pool release.");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    OccupiedPool = occupyPool,
                    Unsatisfiable = unsatisfiable.Status.ToString(),
                    Satisfiable = satisfiable.Status.ToString(),
                    ModelValue = (int)satisfiable.Model!.Assignments[x].IntegerNumericValue,
                    NativeResources = session.ConsumedResourceCount,
                    QueryRlimit = options.QueryRlimit,
                    Proven = proof is ProvenOutcome
                }));
            }

            private static void Require(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }
        }
        """;
}
