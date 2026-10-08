using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GenericTestedTypeBoundaryAuditTests
{
    private const int WarmupCount = 10_000;
    private const int MeasurementCount = 4_096;
    private const int InputGuard = 1;

    [Test]
    public async Task BoundaryClaimsMatchFrozenControlsAndMeasuredClr()
    {
        string[] boundaries = ["fixed-int", "reference-T", "nullable-as-T", "unreachable-U"];
        var observations = new List<BoundaryObservation>();
        foreach (var boundary in boundaries)
        {
            observations.Add(await ObserveBoundary(boundary));
        }
        using (Assert.EnterMultipleScope())
        {
            foreach (var observation in observations)
            {
                if (observation.Boundary != "nullable-as-T")
                {
                    Assert.That(observation.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                        observation.Boundary + ": preserve native support.");
                }
                foreach (var measured in observation.Cases)
                {
                    Assert.That(measured.Target.AllocatedBytes == 0 || observation.Outcome != WorkerClaimOutcome.Proven, Is.True,
                        $"{measured.Name}: bytes{MeasurementCount}={measured.Target.AllocatedBytes}; " +
                        $"native={observation.Outcome}/{observation.Reason}");
                }
            }
        }
    }

    private static async Task<BoundaryObservation> ObserveBoundary(string boundary)
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "generic-type-values-audit",
            "boundary-qualification");
        Directory.CreateDirectory(evidenceDirectory);
        var source = SourceFor(boundary);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.That(sourceHash, Is.EqualTo(ExpectedSourceHash(boundary)), "Frozen boundary source changed.");
        var compilation = TestCompilation.Create("GenericTypeBoundary_" + boundary, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("GenericTypeBoundary_" + boundary, isCollectible: true);
        CaseObservation[] cases;
        try
        {
            var subject = runtime.LoadFromStream(image).GetType("Subject")!;
            var target = subject.GetMethod("Target")!;
            var control = subject.GetMethod("Control")!;
            cases = ObserveCases(boundary, target, control);
            var targetFlags = target.GetMethodImplementationFlags();
            var controlFlags = control.GetMethodImplementationFlags();
            var il = target.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = ReadIl(il);
            await Save(evidenceDirectory, boundary + "-runtime.json", new
            {
                Boundary = boundary,
                SourceSha256 = sourceHash,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                WarmupCount,
                MeasurementCount,
                InputGuard,
                TargetNoInlining = (targetFlags & MethodImplAttributes.NoInlining) != 0,
                ControlNoInlining = (controlFlags & MethodImplAttributes.NoInlining) != 0,
                TargetNoOptimization = (targetFlags & MethodImplAttributes.NoOptimization) != 0,
                HasBox = instructions.Any(instruction => instruction.OpCode == "box"),
                HasIsinst = instructions.Any(instruction => instruction.OpCode == "isinst"),
                IlHex = Convert.ToHexString(il),
                Instructions = instructions,
                Cases = cases,
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                ByteExpectation = "No subject byte count is required in advance. Control bytes must be zero.",
                Coverage = "Current-thread managed bytes on this runtime/tiering configuration; no JIT machine-code disassembly."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That((targetFlags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((controlFlags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((targetFlags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                foreach (var observed in cases)
                {
                    AssertMeasurement(observed.Name, observed.Target);
                    AssertMeasurement(observed.Name + "/control", observed.Control);
                    Assert.That(observed.Control.AllocatedBytes, Is.Zero, observed.Name + ": typed-loop control.");
                }
            }
        }
        finally
        {
            runtime.Unload();
        }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var preserveProof = boundary != "nullable-as-T";
        await Save(evidenceDirectory, boundary + "-native.json", new
        {
            Boundary = boundary,
            SourceSha256 = sourceHash,
            SameCompilationForClrAndNative = true,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            Errors = response.Errors,
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString()
            }).ToArray(),
            Cases = cases,
            NativeExpectation = preserveProof ? "Proven preservation control" : "Exploratory nullable outcome; no fixed outcome",
            SubjectBytes = "Exploratory actual counts, with no zero or positive allocation prerequisite",
            FrozenRule = "Observed allocation > 0 implies ZeroAllocations cannot be Proven.",
            ObservedPositiveAllocation = cases.Any(observed => observed.Target.AllocatedBytes > 0)
        });
        Assert.That(response.Errors, Is.Empty);
        Assert.That(project.Request.Cache.Enabled, Is.False);
        var claim = response.ClaimResults.Single();
        return new(boundary, cases, claim.Outcome, claim.Reason);
    }

    private static CaseObservation[] ObserveCases(string boundary, MethodInfo target, MethodInfo control)
    {
        return boundary switch
        {
            "fixed-int" =>
            [
                Observe<int, object>("fixed/int/non-null", target, control, 7, expected: true),
                Observe<int?, object>("fixed/nullable/non-null", target, control, 7, expected: true),
                Observe<int?, object>("fixed/nullable/null", target, control, null, expected: false),
                Observe<string, object>("fixed/string/non-null", target, control, "text", expected: false)
            ],
            "reference-T" =>
            [
                Observe<string, object>("reference/string/object", target, control, "text", expected: true),
                Observe<string, int>("reference/string/int", target, control, "text", expected: false),
                Observe<string?, object>("reference/null/object", target, control, null, expected: false)
            ],
            "nullable-as-T" =>
            [
                Observe<int?, object>("nullable/non-null/object", target, control, 7, expected: true),
                Observe<int?, object>("nullable/null/object", target, control, null, expected: false),
                Observe<int?, IComparable>("nullable/non-null/IComparable", target, control, 7, expected: true),
                Observe<int?, IComparable>("nullable/null/IComparable", target, control, null, expected: false),
                Observe<int?, string>("nullable/non-null/string", target, control, 7, expected: false),
                Observe<int?, string>("nullable/null/string", target, control, null, expected: false)
            ],
            "unreachable-U" =>
            [
                Observe<int, object>("unreachable/int/object", target, control, 7, expected: false),
                Observe<int?, object>("unreachable/nullable-null/object", target, control, null, expected: false),
                Observe<string?, object>("unreachable/reference-null/object", target, control, null, expected: false)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
    }

    private static CaseObservation Observe<TValue, TTest>(string name, MethodInfo generic, MethodInfo control, TValue value,
        bool expected)
    {
        var target = generic.MakeGenericMethod(typeof(TValue), typeof(TTest)).CreateDelegate<Func<TValue, int, bool>>();
        var controlTarget = control.MakeGenericMethod(typeof(TValue)).CreateDelegate<Func<TValue, int, bool>>();
        return new(name, typeof(TValue).ToString(), typeof(TTest).ToString(),
            Measure(target, value, expected), Measure(controlTarget, value, expected: true));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement Measure<TValue>(Func<TValue, int, bool> target, TValue value, bool expected)
    {
        var actual = target(value, InputGuard);
        for (var index = 0; index < WarmupCount; index++)
        {
            _ = target(value, InputGuard);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var trueResults = 0;
        for (var index = 0; index < MeasurementCount; index++)
        {
            if (target(value, InputGuard))
            {
                trueResults++;
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new(expected, actual, trueResults, allocated, allocated / (double)MeasurementCount);
    }

    private static void AssertMeasurement(string name, Measurement measured)
    {
        Assert.That(measured.Actual, Is.EqualTo(measured.Expected), name);
        Assert.That(measured.TrueResults, Is.EqualTo(measured.Expected ? MeasurementCount : 0), name);
        Assert.That(measured.AllocatedBytes, Is.GreaterThanOrEqualTo(0), name);
    }

    private static string SourceFor(string boundary)
    {
        return boundary switch
        {
            "fixed-int" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value, int guard)
                    {
                        return value is int;
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control<T>(T value, int guard)
                    {
                        return guard == 1;
                    }
                }
                """ + "\n",
            "reference-T" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value, int guard) where T : class
                    {
                        return value is U;
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control<T>(T value, int guard)
                    {
                        return guard == 1;
                    }
                }
                """ + "\n",
            "nullable-as-T" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value, int guard)
                    {
                        return value is U;
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control<T>(T value, int guard)
                    {
                        return guard == 1;
                    }
                }
                """ + "\n",
            "unreachable-U" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value, int guard)
                    {
                        Contract.Requires(guard != 0);
                        if (guard == 0)
                        {
                            return value is U;
                        }
                        return false;
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control<T>(T value, int guard)
                    {
                        return guard == 1;
                    }
                }
                """ + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
    }

    private static string ExpectedSourceHash(string boundary)
    {
        return boundary switch
        {
            "fixed-int" => "E2CC9D6DB5324A34CD87155F185FAA5A41B54CD44D9F4C798825112DD4A21DDE",
            "reference-T" => "F4FF627CA0FB4B3AF9C67522CB51AB5F947BB143626C4CB655D94FB5F94990DD",
            "nullable-as-T" => "678F37EEB5B351B5C6E6B122909EE1D324DDB1C12771442B2503386900363833",
            "unreachable-U" => "C4E0EE0AC77B8A8691AD4542BAF5C2F0EFCAC1172539BD90208166ADC01C060C",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
    }

    private static IlInstruction[] ReadIl(byte[] bytes)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var instructions = new List<IlInstruction>();
        for (var position = 0; position < bytes.Length;)
        {
            var offset = position;
            ushort code = bytes[position++];
            if (code == 0xFE)
            {
                code = (ushort)(0xFE00 | bytes[position++]);
            }
            var opcode = opcodes[code];
            var operandSize = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, position),
                _ => 4
            };
            instructions.Add(new(offset, opcode.Name!, Convert.ToHexString(bytes.AsSpan(position, operandSize))));
            position += operandSize;
        }
        return [.. instructions];
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record BoundaryObservation(string Boundary, CaseObservation[] Cases, WorkerClaimOutcome Outcome, WorkerClaimReason Reason);

    private sealed record CaseObservation(string Name, string ValueType, string TestedType, Measurement Target, Measurement Control);

    private sealed record Measurement(bool Expected, bool Actual, int TrueResults, long AllocatedBytes, double BytesPerCall);

    private sealed record IlInstruction(int Offset, string OpCode, string OperandHex);
}
