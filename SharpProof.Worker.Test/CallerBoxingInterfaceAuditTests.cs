using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CallerBoxingInterfaceAuditTests
{
    private const int WarmupIterations = 10000;
    private const int MeasuredIterations = 128;
    private const int MeasurementRounds = 3;
    private const double WitnessValue = 1.25d;
    private static readonly string[] TargetNames = ["TargetDouble", "TargetNullable"];
    private static readonly byte[] IdentityBody = [0x02, 0x2A];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());
    private static volatile IComparable? _escaped;

    private const string BoundarySource = """
        #nullable enable
        using System;
        using SharpProof.Attributes;
        using System.Runtime.CompilerServices;
        public static class Boundary {
            [SharpProofTrusted("Reviewed identity body: return the supplied interface reference.")]
            [EffectContract(SharpProofEffect.None, Complete = true, PreconditionFree = true)]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? Echo(IComparable? value) => value;
        }
        """ + "\n";

    private const string SubjectSource = """
        #nullable enable
        #undef SHARPPROOF_CONTRACTS
        using System;
        using SharpProof.Attributes;
        using System.Runtime.CompilerServices;
        public static class Subject {
            [ZeroAllocations, MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? TargetDouble(double value, IComparable? sentinel) => Boundary.Echo(value);
            [ZeroAllocations, MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? TargetNullable(double? value, IComparable? sentinel) => Boundary.Echo(value);
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? ControlDouble(double value, IComparable? sentinel) => Boundary.Echo(sentinel);
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? ControlNullable(double? value, IComparable? sentinel) => Boundary.Echo(sentinel);
        }
        """ + "\n";

    [Test]
    public async Task InterfaceBoxingVerdictRequiresGroupedObservedAllocationEvidence()
    {
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "caller-boxing-interface-audit",
            "runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Subject.cs"), SubjectSource);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Boundary.cs"), BoundarySource);

        var external = TestCompilation.Create("CallerBoxingInterfaceBoundary", ("Boundary.cs", BoundarySource));
        external = external.WithOptions(external.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var directory = new TempDirectory("sharpproof-interface-boxing");
        var boundaryPath = Path.Combine(directory.FullName, "Boundary.dll");
        var boundaryEmission = external.Emit(boundaryPath);
        await File.WriteAllTextAsync(Path.Combine(evidence, "boundary-emit-diagnostics.txt"),
            string.Join("\n", boundaryEmission.Diagnostics) + "\n");
        Assert.That(boundaryEmission.Success, Is.True, string.Join("\n", boundaryEmission.Diagnostics));
        var boundaryBytes = await File.ReadAllBytesAsync(boundaryPath);
        await File.WriteAllBytesAsync(Path.Combine(evidence, "boundary.dll"), boundaryBytes);
        var tree = CSharpSyntaxTree.ParseText(SubjectSource, (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs");
        var compilation = CSharpCompilation.Create("CallerBoxingInterfaceSubject", [tree],
            external.References.Append(MetadataReference.CreateFromFile(boundaryPath)), external.Options);
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        await File.WriteAllTextAsync(Path.Combine(evidence, "caller-compiler-diagnostics.txt"),
            string.Join("\n", compilation.GetDiagnostics()) + "\n");
        TestCompilation.AssertNoErrors(compilation);
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var comparable = compilation.GetTypeByMetadataName("System.IComparable")!;
        var bindings = new List<object>();
        foreach (var methodName in TargetNames)
        {
            var syntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == methodName);
            var invocation = (IInvocationOperation)model.GetOperation(
                syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
            Assert.That(invocation.TargetMethod.IsStatic, Is.True);
            Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Echo"));
            Assert.That(invocation.TargetMethod.ContainingType.Name, Is.EqualTo("Boundary"));
            Assert.That(invocation.TargetMethod.ContainingAssembly.Identity.Name, Is.EqualTo(external.AssemblyName));
            Assert.That(invocation.TargetMethod.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ReturnType, comparable), Is.True);
            var argument = invocation.Arguments.Single();
            Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
            Assert.That(argument.Parameter!.Ordinal, Is.Zero);
            Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter.Type, comparable), Is.True);
            Assert.That(argument.Value, Is.InstanceOf<IConversionOperation>());
            var conversion = (IConversionOperation)argument.Value;
            Assert.That(SymbolEqualityComparer.Default.Equals(conversion.Type, comparable), Is.True);
            Assert.That(conversion.Type!.TypeKind, Is.EqualTo(TypeKind.Interface));
            Assert.That(conversion.IsImplicit, Is.True);
            Assert.That(conversion.IsTryCast, Is.False);
            Assert.That(conversion.OperatorMethod, Is.Null);
            Assert.That(conversion.Conversion.Exists, Is.True);
            Assert.That(conversion.Conversion.IsIdentity, Is.False);
            Assert.That(conversion.Conversion.IsImplicit, Is.True);
            Assert.That(conversion.Conversion.IsReference, Is.False);
            Assert.That(conversion.Conversion.IsNullable, Is.False);
            Assert.That(conversion.Conversion.IsUserDefined, Is.False);
            Assert.That(argument.InConversion.IsIdentity, Is.True);
            Assert.That(argument.OutConversion.IsIdentity, Is.True);
            var classified = compilation.ClassifyConversion(conversion.Operand.Type!, conversion.Type);
            Assert.That(classified.IsBoxing, Is.True);
            if (methodName == "TargetDouble")
            { Assert.That(conversion.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Double)); }
            else
            {
                var nullable = (INamedTypeSymbol)conversion.Operand.Type!;
                Assert.That(nullable.OriginalDefinition.SpecialType, Is.EqualTo(SpecialType.System_Nullable_T));
                Assert.That(nullable.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Double));
            }
            var attributes = invocation.TargetMethod.GetAttributes();
            Assert.That(attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "SharpProof.Attributes.SharpProofTrustedAttribute"), Is.True);
            var effect = attributes.Single(attribute => attribute.AttributeClass?.ToDisplayString() ==
                "SharpProof.Attributes.EffectContractAttribute");
            Assert.That(effect.ConstructorArguments.Single().Value, Is.EqualTo(0L));
            Assert.That(effect.NamedArguments.Single(pair => pair.Key == "Complete").Value.Value, Is.EqualTo(true));
            Assert.That(effect.NamedArguments.Single(pair => pair.Key == "PreconditionFree").Value.Value, Is.EqualTo(true));
            bindings.Add(new
            {
                Method = methodName,
                Callee = invocation.TargetMethod.ToDisplayString(),
                CalleeAssembly = invocation.TargetMethod.ContainingAssembly.Identity.ToString(),
                ArgumentKind = argument.ArgumentKind.ToString(),
                ParameterOrdinal = argument.Parameter.Ordinal,
                ArgumentValueKind = argument.Value.Kind.ToString(),
                ArgumentValueType = argument.Value.Type?.ToDisplayString(),
                ArgumentValueIsImplicit = argument.Value.IsImplicit,
                OperandKind = conversion.Operand.Kind.ToString(),
                OperandType = conversion.Operand.Type?.ToDisplayString(),
                OperandIsImplicit = conversion.Operand.IsImplicit,
                ConversionIsBoxing = classified.IsBoxing,
                ConversionIsReference = conversion.Conversion.IsReference,
                ConversionIsNullable = conversion.Conversion.IsNullable,
                InConversionIsIdentity = argument.InConversion.IsIdentity,
                OutConversionIsIdentity = argument.OutConversion.IsIdentity,
                SpanStart = argument.Value.Syntax.SpanStart,
                SpanLength = argument.Value.Syntax.Span.Length
            });
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-binding.json"),
            JsonSerializer.Serialize(bindings, WorkerProtocolJson.SharedOptions));

        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await File.WriteAllTextAsync(Path.Combine(evidence, "caller-emit-diagnostics.txt"),
            string.Join("\n", emission.Diagnostics) + "\n");
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "oracle.dll"), imageBytes);
        image.Position = 0;
        var probes = new List<ProbeObservation>();
        var observations = new List<RuntimeObservation>();
        var identityControls = new List<IdentityObservation>();
        var runtime = new AssemblyLoadContext("InterfaceBoxingOracle", isCollectible: true);
        try
        {
            using var externalImage = new MemoryStream(boundaryBytes);
            var boundaryMethod = runtime.LoadFromStream(externalImage).GetType("Boundary")!.GetMethod("Echo")!;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var doubleMethod = type.GetMethod("TargetDouble")!;
            var nullableMethod = type.GetMethod("TargetNullable")!;
            var doubleControlMethod = type.GetMethod("ControlDouble")!;
            var nullableControlMethod = type.GetMethod("ControlNullable")!;
            var methods = new[] { doubleMethod, nullableMethod, doubleControlMethod, nullableControlMethod, boundaryMethod };
            await File.WriteAllTextAsync(Path.Combine(evidence, "decoded-il.json"),
                JsonSerializer.Serialize(methods.Select(DescribeIl).ToArray(), WorkerProtocolJson.SharedOptions));
            foreach (var method in methods)
            { AssertNoInliningOnly(method); }
            Assert.That(boundaryMethod.GetMethodBody()!.GetILAsByteArray(), Is.EqualTo(IdentityBody),
                "The trusted boundary must only return its supplied interface reference.");
            var echo = boundaryMethod.CreateDelegate<Func<IComparable?, IComparable?>>();
            var targetDouble = doubleMethod.CreateDelegate<Func<double, IComparable?, IComparable?>>();
            var targetNullable = nullableMethod.CreateDelegate<Func<double?, IComparable?, IComparable?>>();
            var controlDouble = doubleControlMethod.CreateDelegate<Func<double, IComparable?, IComparable?>>();
            var controlNullable = nullableControlMethod.CreateDelegate<Func<double?, IComparable?, IComparable?>>();
            IComparable sentinel = WitnessValue; // A preboxed typed control, outside every measurement.
            for (var repeat = 0; repeat < WarmupIterations; repeat++)
            {
                _escaped = echo(sentinel);
                _escaped = echo(null);
            }
            for (var round = 0; round < MeasurementRounds; round++)
            {
                var present = MeasureIdentity(echo, sentinel);
                var absent = MeasureIdentity(echo, null);
                identityControls.Add(new("preboxed-double", round, present.Bytes, ReferenceEquals(present.Result, sentinel)));
                identityControls.Add(new("null", round, absent.Bytes, absent.Result == null));
            }
            MeasureCase("TargetDouble", "double-present", targetDouble, controlDouble, WitnessValue, sentinel, false, probes, observations);
            MeasureCase("TargetNullable", "nullable-present", targetNullable, controlNullable, (double?)WitnessValue, sentinel, false, probes, observations);
            MeasureCase("TargetNullable", "nullable-null", targetNullable, controlNullable, null, null, true, probes, observations);
            GC.KeepAlive(sentinel);
            GC.KeepAlive(_escaped);
        }
        finally { runtime.Unload(); }
        await File.WriteAllTextAsync(Path.Combine(evidence, "clr-probes.json"),
            JsonSerializer.Serialize(probes, WorkerProtocolJson.SharedOptions));
        await File.WriteAllTextAsync(Path.Combine(evidence, "clr-measurements.json"),
            JsonSerializer.Serialize(observations, WorkerProtocolJson.SharedOptions));
        await File.WriteAllTextAsync(Path.Combine(evidence, "identity-control-measurements.json"),
            JsonSerializer.Serialize(identityControls, WorkerProtocolJson.SharedOptions));
        await File.WriteAllTextAsync(Path.Combine(evidence, "compilation-identity.txt"),
            $"assembly={compilation.Assembly.Identity}; PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}; " +
            $"boundaryAssembly={external.Assembly.Identity}; boundaryPE_SHA256={WorkerProtocolJson.ComputeSha256(boundaryBytes)}; " +
            $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(SubjectSource))}; " +
            $"boundarySourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(BoundarySource))}; " +
            $"compiler={typeof(CSharpCompilation).Assembly.GetName().Version}; optimization={compilation.Options.OptimizationLevel}; " +
            $"language={((CSharpParseOptions)tree.Options).LanguageVersion}; warmup={WarmupIterations}; measured={MeasuredIterations}; rounds={MeasurementRounds}; " +
            $"framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
            $"DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<unset>"}; " +
            $"COMPlus_TieredCompilation={Environment.GetEnvironmentVariable("COMPlus_TieredCompilation") ?? "<unset>"}\n");

        // Both CLR subjects and all native claims come from this exact compilation.
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(2));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var nativeObservations = new List<NativeObservation>();
        foreach (var methodName in TargetNames)
        {
            var declaration = artifact.Manifest.Claims.Single(claim =>
                claim.CallableId.Contains("Subject." + methodName, StringComparison.Ordinal));
            var claim = response.ClaimResults.Single(result => result.ClaimId == declaration.ClaimId);
            var captured = artifact.Callables.Single(callable => callable.CallableId == declaration.CallableId);
            var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId == declaration.CallableId);
            var group = observations.Where(row => row.Method == methodName).ToArray();
            Assert.That(group, Has.Length.EqualTo(methodName == "TargetDouble" ? MeasurementRounds : 2 * MeasurementRounds));
            var positive = group.Any(row => row.SubjectBytes > 0);
            var zero = group.All(row => row.SubjectBytes == 0);
            nativeObservations.Add(new(methodName, declaration.CallableId, claim.ClaimId,
                claim.Outcome, claim.Reason, claim.Vacuity, captured.Total != null, captured.Total?.IsBodyAbstraction,
                preparation.Total?.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAllocationInstruction>().Count(),
                positive, zero, zero && claim.Outcome == WorkerClaimOutcome.Refuted));
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "grouped-native-observations.json"),
            JsonSerializer.Serialize(nativeObservations, WorkerProtocolJson.SharedOptions));
        foreach (var native in nativeObservations)
        { TestContext.WriteLine($"{native.Method}; native={native.Outcome}; reason={native.Reason}; positive={native.AnyPositiveBytes}; allZero={native.AllSubjectZero}"); }
        TestContext.WriteLine($"interface-boxing claims={nativeObservations.Count}; inputs=3; evidence={evidence}");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(identityControls.All(row => row.Bytes == 0 && row.SameReference), Is.True,
                "The exact identity boundary must preserve preboxed/null references without allocating.");
            Assert.That(observations.All(row => row.BeforeControlBytes == 0 && row.AfterControlBytes == 0), Is.True,
                "Matching typed preboxed/null controls must calibrate to zero bytes.");
            Assert.That(observations.All(row => row.BeforeSameReference && row.AfterSameReference && row.SubjectPayloadMatches), Is.True);
            foreach (var probe in probes)
            {
                Assert.That(probe.FirstNull, Is.EqualTo(probe.ExpectedNull), probe.Input);
                Assert.That(probe.SecondNull, Is.EqualTo(probe.ExpectedNull), probe.Input);
                Assert.That(probe.SameProbeReference, Is.EqualTo(probe.ExpectedNull), probe.Input);
                Assert.That(probe.FirstPayloadMatches && probe.SecondPayloadMatches, Is.True, probe.Input);
            }
            foreach (var native in nativeObservations.Where(row => row.AnyPositiveBytes))
            {
                Assert.That(native.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "Positive warmed caller allocation for any input cannot receive a valid allocation-free proof. " +
                    native.Method + "; " + native.Reason);
            }
        }));
        // Zero bytes are finite observations. They do not prove universal absence
        // or materialize an opaque native Refuted witness.
    }

    private static void MeasureCase<T>(string method, string input, Func<T, IComparable?, IComparable?> target,
        Func<T, IComparable?, IComparable?> control, T value, IComparable? sentinel, bool expectedNull,
        List<ProbeObservation> probes, List<RuntimeObservation> observations)
    {
        for (var repeat = 0; repeat < WarmupIterations; repeat++)
        {
            _escaped = target(value, sentinel);
            _escaped = control(value, sentinel);
        }
        var first = target(value, sentinel);
        var second = target(value, sentinel);
        _escaped = first;
        _escaped = second;
        probes.Add(new(method, input, expectedNull, first == null, second == null, ReferenceEquals(first, second),
            MatchesPayload(first, expectedNull), MatchesPayload(second, expectedNull),
            first?.GetType().AssemblyQualifiedName, second?.GetType().AssemblyQualifiedName));
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        for (var round = 0; round < MeasurementRounds; round++)
        {
            var before = Measure(control, value, sentinel);
            var subject = Measure(target, value, sentinel);
            var after = Measure(control, value, sentinel);
            observations.Add(new(method, input, round, subject.Bytes, MatchesPayload(subject.Result, expectedNull),
                before.Bytes, ReferenceEquals(before.Result, sentinel), after.Bytes, ReferenceEquals(after.Result, sentinel)));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (long Bytes, IComparable? Result) Measure<T>(Func<T, IComparable?, IComparable?> target, T value, IComparable? sentinel)
    {
        IComparable? result = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        {
            result = target(value, sentinel);
            _escaped = result;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(sentinel);
        GC.KeepAlive(result);
        return (bytes, result);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (long Bytes, IComparable? Result) MeasureIdentity(Func<IComparable?, IComparable?> target, IComparable? value)
    {
        IComparable? result = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        {
            result = target(value);
            _escaped = result;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(value);
        GC.KeepAlive(result);
        return (bytes, result);
    }

    private static bool MatchesPayload(IComparable? value, bool expectedNull)
    { return expectedNull ? value == null : value is double number && number.Equals(WitnessValue); }

    private static void AssertNoInliningOnly(MethodInfo method)
    {
        var flags = method.GetMethodImplementationFlags();
        Assert.That(flags & MethodImplAttributes.NoInlining, Is.EqualTo(MethodImplAttributes.NoInlining));
        Assert.That(flags & MethodImplAttributes.NoOptimization, Is.EqualTo((MethodImplAttributes)0));
    }

    private static IlObservation DescribeIl(MethodInfo method)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var rows = new List<IlInstruction>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var start = offset;
            ushort value = bytes[offset++];
            if (value == 0xFE)
            { value = (ushort)(0xFE00 | bytes[offset++]); }
            if (!OpCodesByValue.TryGetValue(value, out var code))
            { throw new InvalidOperationException("Unknown emitted IL opcode."); }
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
            if (offset + size > bytes.Length)
            { throw new InvalidOperationException("Truncated emitted IL operand."); }
            var operand = code.OperandType switch
            {
                OperandType.InlineMethod => method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))?.ToString(),
                OperandType.InlineType => method.Module.ResolveType(BitConverter.ToInt32(bytes, offset)).ToString(),
                OperandType.InlineField => method.Module.ResolveField(BitConverter.ToInt32(bytes, offset))?.ToString(),
                OperandType.InlineTok => method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset))?.ToString(),
                _ => null
            };
            rows.Add(new(start, code.Name ?? "<unnamed>", Convert.ToHexString(bytes.AsSpan(offset, size)), operand));
            offset += size;
        }
        return new(method.DeclaringType!.FullName + "." + method.Name, method.Module.ModuleVersionId,
            method.MetadataToken, method.GetMethodImplementationFlags().ToString(), Convert.ToHexString(bytes), rows);
    }

    private sealed record ProbeObservation(string Method, string Input, bool ExpectedNull, bool FirstNull, bool SecondNull,
        bool SameProbeReference, bool FirstPayloadMatches, bool SecondPayloadMatches, string? FirstRuntimeType, string? SecondRuntimeType);
    private sealed record RuntimeObservation(string Method, string Input, int Round, long SubjectBytes, bool SubjectPayloadMatches,
        long BeforeControlBytes, bool BeforeSameReference, long AfterControlBytes, bool AfterSameReference);
    private sealed record IdentityObservation(string Input, int Round, long Bytes, bool SameReference);
    private sealed record NativeObservation(string Method, string CallableId, string ClaimId, WorkerClaimOutcome Outcome,
        WorkerClaimReason Reason, WorkerVacuityKind Vacuity, bool CapturedTotal, bool? BodyAbstraction, int? CandidateAllocationSites,
        bool AnyPositiveBytes, bool AllSubjectZero, bool ZeroByteRefutedNeedsWitnessReview);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, string? ResolvedOperand);
    private sealed record IlObservation(string Method, Guid ModuleVersionId, int MetadataToken, string ImplementationFlags,
        string RawIl, List<IlInstruction> Instructions);
}
