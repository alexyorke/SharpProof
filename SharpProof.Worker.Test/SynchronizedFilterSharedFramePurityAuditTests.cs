using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class SynchronizedFilterSharedFramePurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, int.MaxValue];
    private static readonly bool[] s_expectedReturns = [false, true, false];
    private static readonly string[] s_expectedFilterOpCodes = ["ldc.i4.1", "ret"];
    private static readonly Dictionary<ushort, OpCode> s_opCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());
    private const string SubjectTemplate = """
        using System;
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public static class Subject
        {
            [EnforcePure]
            public static bool Target(int x)
            {
                try { return checked(x + 1) > 0; }
                catch (OverflowException) when (Filter()) { return false; }
            }

            [MethodImpl(__METHOD_IMPL_OPTIONS__)]
            private static bool Filter() => true;
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task FilterImplementationFlagsMatchNativeCallerPurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized-filter" : "no-inlining-filter-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-filter-shared-frame-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedFilterSharedFramePurity_" + caseName, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidenceDirectory, "emission.json", new
        {
            SourceSha256 = sourceHash,
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            emission.Success,
            Diagnostics = emission.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray()
        });
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-subject.dll"), imageBytes);
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("SynchronizedFilterSharedFramePurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var target = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var filter = subject.GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var execute = target.CreateDelegate<Func<int, bool>>();
            var executeFilter = filter.CreateDelegate<Func<bool>>();
            var targetIl = DescribeIl(target, filter);
            var filterIl = DescribeIl(filter, filter);
            var clauses = target.GetMethodBody()!.ExceptionHandlingClauses.Select(clause => new ClauseObservation(
                clause.Flags.ToString(), clause.TryOffset, clause.TryLength, clause.HandlerOffset, clause.HandlerLength,
                clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset : (int?)null)).ToArray();
            runtimeObservation = new(DescribeMethod(target), DescribeMethod(filter),
                subject.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Length,
                subject.TypeInitializer != null, s_inputValues.Select(execute).ToArray(), executeFilter(), targetIl, filterIl, clauses);
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                Case = caseName,
                ExpectedFilterImplementationFlags = synchronized ? 40 : 8,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Func<int,bool>; Func<bool>",
                Inputs = s_inputValues,
                ExpectedReturns = s_expectedReturns,
                Observation = runtimeObservation,
                SynchronizationBasis = "Exact reflected Filter Synchronized flag and decoded Target call token binding; no contention or timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. The reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That(runtimeObservation.Target.RawImplementationFlags, Is.Zero);
                Assert.That(runtimeObservation.Filter.RawImplementationFlags, Is.EqualTo(synchronized ? 40 : 8));
                Assert.That(runtimeObservation.Filter.NoInlining, Is.True);
                Assert.That(runtimeObservation.Filter.Synchronized, Is.EqualTo(synchronized));
                Assert.That(runtimeObservation.Target.IsStatic, Is.True);
                Assert.That(runtimeObservation.Filter.IsStatic, Is.True);
                Assert.That(runtimeObservation.Target.ReturnType, Is.EqualTo(typeof(bool).FullName));
                Assert.That(runtimeObservation.Filter.ReturnType, Is.EqualTo(typeof(bool).FullName));
                Assert.That(runtimeObservation.Target.ParameterTypes, Is.EqualTo(new[] { typeof(int).FullName }));
                Assert.That(runtimeObservation.Filter.ParameterTypes, Is.Empty);
                Assert.That(runtimeObservation.FieldCount, Is.Zero);
                Assert.That(runtimeObservation.HasTypeInitializer, Is.False);
                Assert.That(runtimeObservation.TargetReturnValues, Is.EqualTo(s_expectedReturns));
                Assert.That(runtimeObservation.FilterReturnValue, Is.True);
                Assert.That(clauses, Has.Length.EqualTo(1));
                Assert.That(clauses.Single().Flags, Is.EqualTo(ExceptionHandlingClauseOptions.Filter.ToString()));
                var clause = clauses.Single();
                var call = targetIl.Instructions.Single(instruction => instruction.OpCode == "call");
                Assert.That(call.ResolvesExactFilter, Is.True);
                Assert.That(call.MethodToken, Is.EqualTo(filter.MetadataToken));
                Assert.That(call.Offset, Is.GreaterThanOrEqualTo(clause.FilterOffset!.Value).And.LessThan(clause.HandlerOffset));
                var overflow = targetIl.Instructions.Single(instruction => instruction.OpCode == "add.ovf");
                Assert.That(overflow.Offset, Is.GreaterThanOrEqualTo(clause.TryOffset).And.LessThan(clause.TryOffset + clause.TryLength));
                Assert.That(targetIl.Instructions.Single(instruction => instruction.OpCode == "isinst").ResolvedOperand,
                    Is.EqualTo(typeof(OverflowException).ToString()));
                Assert.That(filterIl.Instructions.Select(instruction => instruction.OpCode), Is.EqualTo(s_expectedFilterOpCodes));
            }
        }
        finally
        {
            loadContext.Unload();
        }

        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"),
            WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response) + "\n");
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, manifestBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        await Save(evidenceDirectory, "native-observations.json", new
        {
            SourceSha256 = sourceHash,
            EmittedPeSha256 = Hash(imageBytes),
            Case = caseName,
            SameCompilationForClrAndNative = true,
            CapturedTotal = artifact.Callables.Single().Total != null,
            ReflectedFilterImplementationFlags = runtimeObservation.Filter.ImplementationFlags,
            ReflectedFilterSynchronized = runtimeObservation.Filter.Synchronized,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            ManifestClaims = artifact.Manifest.Claims.Select(claim => new
            {
                claim.ClaimId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The source caller of synchronized Filter cannot be Proven EnforcePure."
                : "The NoInlining-only filter control remains Proven EnforcePure with complete native may-effect evidence."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "The reached source Filter acquires CLR synchronization, which EnforcePure excludes.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(response.CallableResults, Has.Length.EqualTo(1));
                Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            }
        }
    }

    private static MethodObservation DescribeMethod(MethodInfo method)
    {
        var flags = method.GetMethodImplementationFlags();
        return new(method.DeclaringType!.FullName + "." + method.Name, method.MetadataToken, method.Module.ModuleVersionId,
            (int)flags, flags.ToString(), (flags & MethodImplAttributes.NoInlining) != 0,
            (flags & MethodImplAttributes.Synchronized) != 0, method.IsStatic, method.ReturnType.FullName,
            method.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray());
    }

    private static IlObservation DescribeIl(MethodInfo method, MethodInfo filter)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var rows = new List<IlInstruction>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var start = offset;
            ushort value = bytes[offset++];
            if (value == 0xFE)
            {
                if (offset >= bytes.Length)
                { throw new InvalidOperationException("Truncated emitted IL opcode."); }
                value = (ushort)(0xFE00 | bytes[offset++]);
            }
            if (!s_opCodesByValue.TryGetValue(value, out var code))
            { throw new InvalidOperationException("Unknown emitted IL opcode."); }
            if (code.OperandType == OperandType.InlineSwitch && offset + 4 > bytes.Length)
            { throw new InvalidOperationException("Truncated emitted IL switch count."); }
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, offset)),
                _ => 4
            };
            if (size < 0 || offset + size > bytes.Length)
            { throw new InvalidOperationException("Truncated emitted IL operand."); }
            var token = code.OperandType == OperandType.InlineMethod ? BitConverter.ToInt32(bytes, offset) : (int?)null;
            var resolved = token is { } methodToken ? method.Module.ResolveMethod(methodToken) : null;
            var operand = code.OperandType switch
            {
                OperandType.InlineMethod => resolved?.ToString(),
                OperandType.InlineType => method.Module.ResolveType(BitConverter.ToInt32(bytes, offset)).ToString(),
                OperandType.InlineField => method.Module.ResolveField(BitConverter.ToInt32(bytes, offset))?.ToString(),
                OperandType.InlineTok => method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset))?.ToString(),
                _ => null
            };
            var resolvesExactFilter = resolved != null && resolved.MetadataToken == filter.MetadataToken &&
                resolved.Module.ModuleVersionId == filter.Module.ModuleVersionId && resolved.DeclaringType == filter.DeclaringType;
            rows.Add(new(start, code.Name ?? "<unnamed>", Convert.ToHexString(bytes.AsSpan(offset, size)), operand,
                token, resolvesExactFilter));
            offset += size;
        }
        return new(method.DeclaringType!.FullName + "." + method.Name, method.Module.ModuleVersionId,
            method.MetadataToken, method.GetMethodImplementationFlags().ToString(), Convert.ToHexString(bytes), rows);
    }

    private static string Hash(byte[] value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record MethodObservation(string Method, int MetadataToken, Guid ModuleVersionId, int RawImplementationFlags,
        string ImplementationFlags, bool NoInlining, bool Synchronized, bool IsStatic, string? ReturnType, string?[] ParameterTypes);
    private sealed record ClauseObservation(string Flags, int TryOffset, int TryLength, int HandlerOffset, int HandlerLength, int? FilterOffset);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, string? ResolvedOperand, int? MethodToken,
        bool ResolvesExactFilter);
    private sealed record IlObservation(string Method, Guid ModuleVersionId, int MetadataToken, string ImplementationFlags,
        string RawIl, List<IlInstruction> Instructions);
    private sealed record RuntimeObservation(MethodObservation Target, MethodObservation Filter, int FieldCount,
        bool HasTypeInitializer, bool[] TargetReturnValues, bool FilterReturnValue, IlObservation TargetIl, IlObservation FilterIl,
        ClauseObservation[] ExceptionClauses);
}
