using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Each seed selects a type-correct shift or checked conversion claim. The
// Release CLR oracle runs boundary inputs before any Worker verdict is trusted.
[TestFixture]
[Explicit("Long-running fuzz campaign; select with TestCategory=LongFuzz.")]
[Category(LongFuzzSession.Category)]
[Order(2)]
[NonParallelizable]
public sealed class TypedWidthLongFuzzTests
{
    private const string Harness = "typed-width";
    private sealed record GeneratedCase(string Name, string Source, ImmutableArray<ImmutableArray<object>> Inputs, bool ExceptionClaim, bool ViolatedOnGrid)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public string Source { get; init; } = Source ?? throw new System.ArgumentNullException(nameof(Source));
        public ImmutableArray<ImmutableArray<object>> Inputs { get; init; } = Inputs.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Inputs)) : Inputs;
    }
    private sealed record Observation(int Normal, int Overflow, int Violations, string? Witness, string? Error);

    [Test]
    public async Task GeneratedWidthClaimsAgreeWithClrWitnesses()
    {
        var core = Cases().ToImmutableArray();
        var variableShifts = core.Where(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (!item.ExceptionClaim)).ToImmutableArray();
        var session = new LongFuzzSession(Harness, harnessIndex: 3, harnessCount: 6);
        foreach (var seed in session.Seeds())
        {
            var item = SeedCase(core, variableShifts, seed);
            var family = item.ExceptionClaim ? "checked-cast" : "shift";
            var type = item.Name.Split('/')[0];
            var variant = item.Name.EndsWith("/variable", StringComparison.Ordinal) ? "high-count" : "pinned";
            session.Count("selected:" + family + ":" + type);
            session.Count("selected:" + variant);
            try
            { await RunCase(session, seed, item, family, type, variant); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                session.RecordHit(new LongFuzzHit("HarnessException", LongFuzzHit.Error, seed, item.Name,
                    "", "", "", exception.ToString(), item.Source));
            }
        }
        var summary = session.Finish();
        Assert.That(session.Cases, Is.Not.Zero, "The campaign must run at least one case: " + summary);
    }

    private static async Task RunCase(LongFuzzSession session, int seed, GeneratedCase item, string family, string type,
        string variant)
    {
        System.ArgumentNullException.ThrowIfNull(family);
        System.ArgumentNullException.ThrowIfNull(session);
        System.ArgumentNullException.ThrowIfNull(type);
        System.ArgumentNullException.ThrowIfNull(variant);
        var compilation = Compile(item.Source);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        if (errors.Length != 0)
        {
            session.RecordHit(new LongFuzzHit("GeneratorCompileError", LongFuzzHit.Error, seed, item.Name,
                "", "", "", string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))), item.Source));
            return;
        }
        session.Count("compiled:" + family + ":" + type);
        var observation = Observe(compilation, item);
        var runtime = $"normal={observation.Normal}, overflow={observation.Overflow}, " +
            $"violations={observation.Violations}/{item.Inputs.Length}; {observation.Witness ?? "no violation"}";
        if (observation.Error is not null)
        {
            session.RecordHit(new LongFuzzHit("RuntimeOracleError", LongFuzzHit.Error, seed, item.Name,
                "", "", runtime, observation.Error, item.Source));
            return;
        }
        session.Count("clr:" + family + (observation.Violations > 0 ? ":witness" : ":valid"));
        session.Count("clr:" + variant + (observation.Violations > 0 ? ":witness" : ":valid"));
        if (observation.Overflow > 0)
        { session.Count("clr:" + family + ":overflow"); }
        if ((observation.Violations > 0) != item.ViolatedOnGrid)
        {
            session.RecordHit(new LongFuzzHit("OracleMismatch", LongFuzzHit.Error, seed, item.Name,
                "", "", runtime, "The selected case changed its expected CLR classification.", item.Source));
            return;
        }

        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        if (artifact.Callables.Single().Total is not null)
        { session.Count("admitted:" + family + ":" + type); }
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        var protocol = WorkerProtocolJson.Validate(response);
        if (response.Errors.Length != 0 || !protocol.IsValid)
        {
            session.RecordHit(new LongFuzzHit("WorkerErrors", LongFuzzHit.Error, seed, item.Name,
                "", "", runtime, response.Errors.Length != 0
                    ? string.Join(" | ", response.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code + ":" + error.Message)))
                    : "Worker response failed protocol validation.", item.Source));
            return;
        }
        var claims = response.ClaimResults.Where(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(claim =>
            claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId == result.ClaimId && claim.CallableId.Contains("Target", StringComparison.Ordinal))))).ToImmutableArray();
        if (claims.Length != 1)
        {
            session.RecordHit(new LongFuzzHit("ClaimSelectionError", LongFuzzHit.Error, seed, item.Name,
                "", "", runtime, "Expected one Target claim, got " + claims.Length, item.Source));
            return;
        }
        var claimResult = claims[0];
        session.Count("verdict:" + family + ":" + type + ":" + claimResult.Outcome);
        session.Count("verdict:" + variant + ":" + claimResult.Outcome);
        if (claimResult.Outcome == WorkerClaimOutcome.Unknown)
        { session.Count("unknown-reason:" + family + ":" + type + ":" + claimResult.Reason); }
        if (claimResult.Vacuity != WorkerVacuityKind.None)
        {
            session.RecordHit(new LongFuzzHit("UnexpectedVacuity", LongFuzzHit.Error, seed, item.Name,
                claimResult.Outcome.ToString(), claimResult.Reason.ToString(), runtime,
                "The CLR grid contains feasible entry inputs, but the claim is vacuous: " + claimResult.Vacuity, item.Source));
        }
        if (claimResult.Outcome == WorkerClaimOutcome.Proven && observation.Violations > 0)
        {
            session.RecordHit(new LongFuzzHit("FalseProven", LongFuzzHit.Error, seed, item.Name,
                claimResult.Outcome.ToString(), claimResult.Reason.ToString(), runtime,
                "The CLR falsifies the Proven claim at " + observation.Witness, item.Source));
        }
        else if (claimResult.Outcome == WorkerClaimOutcome.Refuted && observation.Violations == 0)
        {
            session.RecordHit(new LongFuzzHit("RefutedNotObserved", LongFuzzHit.Warning, seed, item.Name,
                claimResult.Outcome.ToString(), claimResult.Reason.ToString(), runtime,
                "No CLR grid input witnesses the reported refutation.", item.Source));
        }
    }

    private static CSharpCompilation Compile(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "/project/Subject.cs");
        return CSharpCompilation.Create("WidthFuzz_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Observation Observe(CSharpCompilation compilation, GeneratedCase item)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        if (!emission.Success)
        { return new(0, 0, 0, null, "Emit failed: " + string.Join(" | ", emission.Diagnostics)); }
        image.Position = 0;
        var context = new AssemblyLoadContext("typed-width-longfuzz", isCollectible: true);
        var normal = 0;
        var overflow = 0;
        var violations = 0;
        string? witness = null;
        try
        {
            var method = context.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in item.Inputs)
            {
                try
                {
                    var result = method.Invoke(null, input.ToArray());
                    normal++;
                    if (!item.ExceptionClaim)
                    {
                        if (result is not bool verdict)
                        { return new(normal, overflow, violations, witness, "Boolean claim returned a non-Boolean result."); }
                        if (!verdict)
                        {
                            violations++;
                            witness ??= FormatInput(input) + " returned false";
                        }
                    }
                }
                catch (TargetInvocationException exception) when (exception.InnerException is OverflowException)
                {
                    overflow++;
                    if (!item.ExceptionClaim)
                    { return new(normal, overflow, violations, witness, "Shift case unexpectedly overflowed."); }
                    violations++;
                    witness ??= FormatInput(input) + " threw OverflowException";
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new(normal, overflow, violations, witness, exception.ToString()); }
        finally
        { context.Unload(); }
        return new(normal, overflow, violations, witness,
            normal + overflow == item.Inputs.Length ? null : "The CLR grid did not execute every input.");
    }

    private static string FormatInput(ImmutableArray<object> input)
    {
        if (input.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(input)); }
        return "input=" + string.Join(",", input.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value is char character
            ? ((int)character).ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture))));
    }

    private static IEnumerable<GeneratedCase> Cases()
    {
        foreach (var (type, counts, values) in ShiftTypes())
        {
            var mask = type is "long" or "ulong" ? 63 : 31;
            foreach (var count in counts)
            {
                foreach (var shift in ImmutableArray.Create("<<", ">>", ">>>"))
                {
                    var name = $"{type}/{shift}/{count}/masked";
                    yield return new GeneratedCase(name, ShiftSource(type, shift, count, mask, false),
                        values.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (ImmutableArray.Create<object>(value, count))).ToImmutableArray(), false, false);
                    if (count == mask)
                    {
                        name = $"{type}/{shift}/{count}/identity";
                        yield return new GeneratedCase(name, ShiftSource(type, shift, count, mask, true),
                            values.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (ImmutableArray.Create<object>(value, count))).ToImmutableArray(), false, true);
                    }
                }
            }
        }

        foreach (var item in CastCases())
        { yield return item; }
    }

    private static GeneratedCase SeedCase(ImmutableArray<GeneratedCase> core, ImmutableArray<GeneratedCase> variableShifts, int seed)
    {
        // Every 302 consecutive seeds cover the 167 pinned edge/cast shapes
        // and 135 distinct shift-count variants. The latter retain the same
        // low count bits while exercising both signs and high count bits.
        if (variableShifts.IsDefault)
        { throw new ArgumentNullException(nameof(variableShifts)); }
        if (core.IsDefault)
        { throw new ArgumentNullException(nameof(core)); }
        var index = (int)((uint)seed % (uint)(core.Length + variableShifts.Length));
        if (index < core.Length)
        { return core[index]; }
        var template = variableShifts[index - core.Length];
        var parts = template.Name.Split('/').ToImmutableArray();
        var type = parts[0];
        var shift = parts[1];
        var baseCount = int.Parse(parts[2], CultureInfo.InvariantCulture);
        var mask = type is "long" or "ulong" ? 63 : 31;
        var random = new Random(seed);
        var count = (unchecked((int)random.NextInt64()) & ~mask) | (baseCount & mask);
        var identity = parts[3] == "identity";
        var name = type + "/" + shift + "/" + count.ToString(CultureInfo.InvariantCulture) + "/" +
            parts[3] + "/variable";
        return template with
        {
            Name = name,
            Source = ShiftSource(type, shift, count, mask, identity),
            Inputs = template.Inputs.Select(input => input.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(input)) : (ImmutableArray.Create<object>(input[0], count))).ToImmutableArray()
        };
    }

    private static IEnumerable<(string Type, ImmutableArray<int> Counts, ImmutableArray<object> Values)> ShiftTypes()
    {
        yield return ("sbyte", [31, 32, 63, 64], [(sbyte)-128, (sbyte)-1, (sbyte)0, (sbyte)1, (sbyte)127]);
        yield return ("byte", [31, 32, 63, 64], [(byte)0, (byte)1, (byte)0x7f, (byte)0x80, byte.MaxValue]);
        yield return ("short", [31, 32, 63, 64], [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue]);
        yield return ("ushort", [31, 32, 63, 64], [(ushort)0, (ushort)1, (ushort)0x7fff, (ushort)0x8000, ushort.MaxValue]);
        yield return ("char", [31, 32, 63, 64], [(char)0, (char)1, (char)0x7fff, (char)0x8000, (char)0xffff]);
        yield return ("int", [31, 32, 63, 64], [int.MinValue, -1, 0, 1, int.MaxValue]);
        yield return ("uint", [31, 32, 63, 64], [0U, 1U, 0x7fffffffU, 0x80000000U, uint.MaxValue]);
        yield return ("long", [31, 32, 63, 64], [long.MinValue, -1L, 0L, 1L, long.MaxValue]);
        yield return ("ulong", [31, 32, 63, 64], [0UL, 1UL, 0x7fffffffffffffffUL, 0x8000000000000000UL, ulong.MaxValue]);
    }

    private static IEnumerable<GeneratedCase> CastCases()
    {
        foreach (var (target, targetMinimum, targetMaximum) in CastTargets())
        {
            foreach (var source in ImmutableArray.Create("long", "ulong"))
            {
                if (source == target)
                { continue; }
                var sourceMinimum = source == "long" ? new BigInteger(long.MinValue) : BigInteger.Zero;
                var sourceMaximum = source == "long" ? new BigInteger(long.MaxValue) : new BigInteger(ulong.MaxValue);
                var minimum = BigInteger.Max(sourceMinimum, targetMinimum);
                var maximum = BigInteger.Min(sourceMaximum, targetMaximum);
                var boundary = ImmutableArray.Create(minimum, minimum + 1, BigInteger.Zero, BigInteger.One, maximum - 1, maximum)
                    .Where(value => value >= minimum && value <= maximum).Distinct().ToImmutableArray();
                var outside = ImmutableArray.Create(minimum - 1, maximum + 1)
                    .Where(value => value >= sourceMinimum && value <= sourceMaximum).ToImmutableArray();
                Assert.That(outside, Is.Not.Empty, $"{source}->{target} needs an overflowing witness.");
                ImmutableArray<object> Box(IEnumerable<BigInteger> values)
                {
                    System.ArgumentNullException.ThrowIfNull(values);
                    return values.Select(value => source == "long" ?
                        (object)(long)value : (ulong)value).ToImmutableArray();
                }
                var requires = source == "long"
                    ? "value >= " + minimum.ToString(CultureInfo.InvariantCulture) + "L && value <= " +
                      maximum.ToString(CultureInfo.InvariantCulture) + "L"
                    : "value <= " + maximum.ToString(CultureInfo.InvariantCulture) + "UL";
                yield return CastCase(target + "/checked/" + source + "/bounded", source, target, requires,
                    Box(boundary), false);
                yield return CastCase(target + "/checked/" + source + "/unbounded", source, target, "true",
                    Box(boundary.Concat(outside)), true);
            }
        }
    }

    private static IEnumerable<(string Type, BigInteger Minimum, BigInteger Maximum)> CastTargets()
    {
        yield return ("sbyte", sbyte.MinValue, sbyte.MaxValue);
        yield return ("byte", byte.MinValue, byte.MaxValue);
        yield return ("short", short.MinValue, short.MaxValue);
        yield return ("ushort", ushort.MinValue, ushort.MaxValue);
        yield return ("char", char.MinValue, char.MaxValue);
        yield return ("int", int.MinValue, int.MaxValue);
        yield return ("uint", uint.MinValue, uint.MaxValue);
        yield return ("long", long.MinValue, long.MaxValue);
        yield return ("ulong", ulong.MinValue, ulong.MaxValue);
    }

    private static GeneratedCase CastCase(string name, string sourceType, string target, string requires, ImmutableArray<object> values,
        bool violated)
    {
        if (values.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(values)); }
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(sourceType);
        System.ArgumentNullException.ThrowIfNull(target);
        var source = $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static {{target}} Target({{sourceType}} value) {
                    Contract.Requires({{requires}});
                    return checked(({{target}})value);
                }
            }
            """;
        return new GeneratedCase(name, source, values.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (ImmutableArray.Create<object>(value))).ToImmutableArray(), true, violated);
    }

    private static string ShiftSource(string type, string shift, int count, int mask, bool identity)
    {
        System.ArgumentNullException.ThrowIfNull(shift);
        System.ArgumentNullException.ThrowIfNull(type);
        var shifted = $"unchecked(({type})(value {shift} count))";
        var predicate = identity ? shifted + " == value" :
            shifted + $" == unchecked(({type})(value {shift} (count & {mask})))";
        return $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target({{type}} value, int count) {
                    Contract.Requires(count == {{count.ToString(CultureInfo.InvariantCulture)}});
                    Contract.Ensures(Contract.Result<bool>());
                    return {{predicate}};
                }
            }
            """;
    }
}
