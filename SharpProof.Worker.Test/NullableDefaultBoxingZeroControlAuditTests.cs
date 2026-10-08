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
public sealed class NullableDefaultBoxingZeroControlAuditTests
{
    private const int WarmupIterations = 10000;
    private const int MeasuredIterations = 128;
    private const int MeasurementRounds = 3;
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
            public static IComparable? Target() => Boundary.Echo(default(double?));
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static IComparable? Control() => Boundary.Echo((IComparable?)null);
        }
        """ + "\n";

    [Test]
    public async Task ConstantNullableWithoutValueKeepsAllocationFreeProof()
    {
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "caller-boxing-nullable-default-control",
            "runtime", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Subject.cs"), SubjectSource);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Boundary.cs"), BoundarySource);
        var external = TestCompilation.Create("NullableDefaultBoxingBoundary", ("Boundary.cs", BoundarySource));
        external = external.WithOptions(external.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var directory = new TempDirectory("sharpproof-nullable-default-boxing");
        var boundaryPath = Path.Combine(directory.FullName, "Boundary.dll");
        var boundaryEmission = external.Emit(boundaryPath);
        await File.WriteAllTextAsync(Path.Combine(evidence, "boundary-emit-diagnostics.txt"),
            string.Join("\n", boundaryEmission.Diagnostics) + "\n");
        Assert.That(boundaryEmission.Success, Is.True, string.Join("\n", boundaryEmission.Diagnostics));
        var boundaryBytes = await File.ReadAllBytesAsync(boundaryPath);
        await File.WriteAllBytesAsync(Path.Combine(evidence, "boundary.dll"), boundaryBytes);
        var tree = CSharpSyntaxTree.ParseText(SubjectSource, (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs");
        var compilation = CSharpCompilation.Create("NullableDefaultBoxingSubject", [tree],
            external.References.Append(MetadataReference.CreateFromFile(boundaryPath)), external.Options);
        await File.WriteAllTextAsync(Path.Combine(evidence, "caller-compiler-diagnostics.txt"),
            string.Join("\n", compilation.GetDiagnostics()) + "\n");
        TestCompilation.AssertNoErrors(compilation);
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree)
            .GetOperation(syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
        var argument = invocation.Arguments.Single();
        var conversion = argument.Value as IConversionOperation;
        var operand = conversion?.Operand;
        var classified = conversion == null ? default : compilation.ClassifyConversion(operand!.Type!, conversion.Type!);
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-binding.json"),
            JsonSerializer.Serialize(new
            {
                Callee = invocation.TargetMethod.ToDisplayString(),
                CalleeAssembly = invocation.TargetMethod.ContainingAssembly.Identity.ToString(),
                argument.ArgumentKind,
                ParameterType = argument.Parameter?.Type.ToDisplayString(),
                ValueKind = argument.Value.Kind,
                ValueType = argument.Value.Type?.ToDisplayString(),
                ValueIsImplicit = argument.Value.IsImplicit,
                ConversionHasConstant = argument.Value.ConstantValue.HasValue,
                ConversionConstant = argument.Value.ConstantValue.HasValue ? argument.Value.ConstantValue.Value : null,
                OperandKind = operand?.Kind,
                OperandType = operand?.Type?.ToDisplayString(),
                OperandSyntax = operand?.Syntax.ToString(),
                OperandIsImplicit = operand?.IsImplicit,
                OperandHasConstant = operand?.ConstantValue.HasValue,
                OperandConstant = operand != null && operand.ConstantValue.HasValue ? operand.ConstantValue.Value : null,
                ConversionIsBoxing = classified.IsBoxing,
                ConversionIsReference = conversion?.Conversion.IsReference,
                ConversionIsNullable = conversion?.Conversion.IsNullable,
                InConversionIsIdentity = argument.InConversion.IsIdentity,
                OutConversionIsIdentity = argument.OutConversion.IsIdentity
            }, WorkerProtocolJson.SharedOptions));
        var comparable = compilation.GetTypeByMetadataName("System.IComparable")!;
        Assert.That(invocation.TargetMethod.ContainingAssembly.Identity.Name, Is.EqualTo(external.AssemblyName));
        Assert.That(invocation.TargetMethod.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Echo"));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ReturnType, comparable), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter!.Type, comparable), Is.True);
        Assert.That(argument.ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        Assert.That(argument.Parameter.Ordinal, Is.Zero);
        Assert.That(conversion, Is.Not.Null);
        Assert.That(conversion!.IsImplicit, Is.True);
        Assert.That(conversion.IsTryCast, Is.False);
        Assert.That(conversion.OperatorMethod, Is.Null);
        Assert.That(conversion.Conversion.Exists && conversion.Conversion.IsImplicit, Is.True);
        Assert.That(conversion.Conversion.IsIdentity || conversion.Conversion.IsReference ||
            conversion.Conversion.IsNullable || conversion.Conversion.IsUserDefined, Is.False);
        Assert.That(classified.IsBoxing, Is.True);
        Assert.That(argument.InConversion.IsIdentity && argument.OutConversion.IsIdentity, Is.True);
        Assert.That(operand, Is.InstanceOf<IDefaultValueOperation>());
        Assert.That(operand!.Syntax.ToString(), Is.EqualTo("default(double?)"));
        Assert.That(operand.IsImplicit, Is.False);
        Assert.That(operand.ConstantValue.HasValue, Is.False,
            "Pinned Roslyn represents typed nullable default without a public constant value.");
        var nullable = (INamedTypeSymbol)operand.Type!;
        Assert.That(nullable.OriginalDefinition.SpecialType, Is.EqualTo(SpecialType.System_Nullable_T));
        Assert.That(nullable.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Double));
        var attributes = invocation.TargetMethod.GetAttributes();
        Assert.That(attributes.Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
            "SharpProof.Attributes.SharpProofTrustedAttribute"), Is.True);
        var effect = attributes.Single(attribute => attribute.AttributeClass?.ToDisplayString() ==
            "SharpProof.Attributes.EffectContractAttribute");
        Assert.That(effect.ConstructorArguments.Single().Value, Is.EqualTo(0L));
        Assert.That(effect.NamedArguments.Single(pair => pair.Key == "Complete").Value.Value, Is.EqualTo(true));
        Assert.That(effect.NamedArguments.Single(pair => pair.Key == "PreconditionFree").Value.Value, Is.EqualTo(true));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await File.WriteAllTextAsync(Path.Combine(evidence, "caller-emit-diagnostics.txt"),
            string.Join("\n", emission.Diagnostics) + "\n");
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "oracle.dll"), imageBytes);
        image.Position = 0;
        var rows = new List<MeasurementObservation>();
        var runtime = new AssemblyLoadContext("NullableDefaultBoxingOracle", isCollectible: true);
        bool firstNull;
        bool secondNull;
        bool sameProbeReference;
        try
        {
            using var boundaryImage = new MemoryStream(boundaryBytes);
            var echoMethod = runtime.LoadFromStream(boundaryImage).GetType("Boundary")!.GetMethod("Echo")!;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var targetMethod = type.GetMethod("Target")!;
            var controlMethod = type.GetMethod("Control")!;
            var methods = new[] { targetMethod, controlMethod, echoMethod };
            await File.WriteAllTextAsync(Path.Combine(evidence, "decoded-il.json"),
                JsonSerializer.Serialize(methods.Select(DescribeIl).ToArray(), WorkerProtocolJson.SharedOptions));
            foreach (var method in methods)
            {
                var flags = method.GetMethodImplementationFlags();
                Assert.That(flags & MethodImplAttributes.NoInlining, Is.EqualTo(MethodImplAttributes.NoInlining));
                Assert.That(flags & MethodImplAttributes.NoOptimization, Is.EqualTo((MethodImplAttributes)0));
            }
            Assert.That(echoMethod.GetMethodBody()!.GetILAsByteArray(), Is.EqualTo(IdentityBody),
                "The trusted None boundary must only return its supplied reference.");
            var target = targetMethod.CreateDelegate<Func<IComparable?>>();
            var control = controlMethod.CreateDelegate<Func<IComparable?>>();
            var echo = echoMethod.CreateDelegate<Func<IComparable?, IComparable?>>();
            for (var repeat = 0; repeat < WarmupIterations; repeat++)
            {
                _escaped = target();
                _escaped = control();
                _escaped = echo(null);
            }
            var first = target();
            var second = target();
            firstNull = first == null;
            secondNull = second == null;
            sameProbeReference = ReferenceEquals(first, second);
            _escaped = first;
            _escaped = second;
            await File.WriteAllTextAsync(Path.Combine(evidence, "clr-probes.json"),
                JsonSerializer.Serialize(new { FirstNull = firstNull, SecondNull = secondNull, SameProbeReference = sameProbeReference },
                    WorkerProtocolJson.SharedOptions));
            for (var round = 0; round < MeasurementRounds; round++)
            {
                var before = Measure(control);
                var subject = Measure(target);
                var after = Measure(control);
                var identity = MeasureIdentity(echo);
                rows.Add(new(round, subject.Bytes, subject.Result == null, before.Bytes, before.Result == null,
                    after.Bytes, after.Result == null, identity.Bytes, identity.Result == null));
            }
            GC.KeepAlive(first);
            GC.KeepAlive(second);
            GC.KeepAlive(_escaped);
        }
        finally { runtime.Unload(); }
        await File.WriteAllTextAsync(Path.Combine(evidence, "clr-measurements.json"),
            JsonSerializer.Serialize(rows, WorkerProtocolJson.SharedOptions));
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
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var declaration = artifact.Manifest.Claims.Single();
        var claim = response.ClaimResults.Single(result => result.ClaimId == declaration.ClaimId);
        var captured = artifact.Callables.Single(callable => callable.CallableId == declaration.CallableId);
        var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId == declaration.CallableId);
        await File.WriteAllTextAsync(Path.Combine(evidence, "native-observation.json"),
            JsonSerializer.Serialize(new
            {
                declaration.CallableId,
                declaration.ClaimId,
                claim.Outcome,
                claim.Reason,
                claim.Vacuity,
                CapturedTotal = captured.Total != null,
                BodyAbstraction = captured.Total?.IsBodyAbstraction,
                CandidateAllocationSites = preparation.Total?.Program.Blocks.SelectMany(block => block.Instructions)
                    .OfType<IrAllocationInstruction>().Count()
            }, WorkerProtocolJson.SharedOptions));
        TestContext.WriteLine($"nullable-default-null; native={claim.Outcome}; reason={claim.Reason}; " +
            $"subjectBytes={string.Join(",", rows.Select(row => row.SubjectBytes))}; evidence={evidence}");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(firstNull && secondNull && sameProbeReference, Is.True,
                "Boxing default(double?) produces the null reference.");
            Assert.That(rows.All(row => row.SubjectBytes == 0 && row.SubjectNull), Is.True,
                "The exact nullable-without-value subject is a zero-only control.");
            Assert.That(rows.All(row => row.BeforeBytes == 0 && row.BeforeNull && row.AfterBytes == 0 &&
                row.AfterNull && row.IdentityBytes == 0 && row.IdentityNull), Is.True,
                "Matching typed null and exact metadata identity controls must allocate zero bytes.");
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                "Preserve the current allocation-free native expectation for this exact null-only source. " + claim.Reason);
        }));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (long Bytes, IComparable? Result) Measure(Func<IComparable?> target)
    {
        IComparable? result = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        {
            result = target();
            _escaped = result;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(result);
        return (bytes, result);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (long Bytes, IComparable? Result) MeasureIdentity(Func<IComparable?, IComparable?> target)
    {
        IComparable? result = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        {
            result = target(null);
            _escaped = result;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(result);
        return (bytes, result);
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
    private sealed record MeasurementObservation(int Round, long SubjectBytes, bool SubjectNull,
        long BeforeBytes, bool BeforeNull, long AfterBytes, bool AfterNull, long IdentityBytes, bool IdentityNull);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, string? ResolvedOperand);
    private sealed record IlObservation(string Method, Guid ModuleVersionId, int MetadataToken, string ImplementationFlags,
        string RawIl, List<IlInstruction> Instructions);
}
