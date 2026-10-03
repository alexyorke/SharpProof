using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeMetadataNominalOwnerTests
{
    [TestCase("class", false, false)]
    [TestCase("class", true, false)]
    [TestCase("interface", false, false)]
    [TestCase("interface", true, false)]
    [TestCase("delegate", false, false)]
    [TestCase("delegate", true, false)]
    [TestCase("class", false, true)]
    [TestCase("class", true, true)]
    [TestCase("string", false, false)]
    [TestCase("string", true, false)]
    public async Task ImplicitObjectWideningPreservesConcreteIdentityAndNativeNullness(string kind, bool isNull, bool nominalReturn)
    {
        var (definition, type) = kind switch
        {
            "class" => ("public sealed class Token { }", "Token"),
            "interface" => ("public interface IToken { } public sealed class Token : IToken { }", "IToken"),
            "delegate" => ("", "System.Action"),
            _ => ("", "string")
        };
        var library = nominalReturn ?
            "using SharpProof.Attributes; public static class Library { public static void Target([NotNull] object value) { } }" :
            "using SharpProof.Attributes; public static class Library { public static object Target([NotNull] object value) => value; }";
        var body = nominalReturn ? "Library.Target(value); return value;" : "return Library.Target(value);";
        using var subject = new MetadataTestSubject(library,
            $"{definition} public static class Subject {{ public static {(nominalReturn ? type : "object")} Target({type} value) {{ {body} }} }}");
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var originalManifest = JsonSerializer.Serialize(new ClaimManifestBuilder(subject.Compilation).Build().Manifest);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, enableMetadataRequires: true);
        Assert.That(batch.Gaps, Is.Empty);
        var preparation = batch.Callers.Single().Body;
        var decoded = CompilerDecodedShadowBody.Decode(preparation.CallableId,
            JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(preparation)))!,
            CancellationToken.None, references);
        var factory = decoded.Body.Program.Factory;
        var entry = decoded.Body.Parameters.Single().Entry;
        var entryType = factory.GetVariableInfo(entry).Type;
        object? concrete = isNull ? null : kind == "string" ? "hello" : kind == "delegate" ?
            new Action(static () => { }) : Activator.CreateInstance(subject.RootMethod.DeclaringType!.Assembly.GetType("Token")!);
        var value = concrete == null ? factory.CreateNullValue(entryType) : kind == "string" ?
            factory.CreateStringValue((string)concrete) : factory.CreateReferenceValue(entryType, concrete);
        var observations = new List<bool>();
        var marker = decoded.Body.CallPreconditions.Single().Instruction;
        var replay = new IrProgramInterpreter(factory).Execute(decoded.Body.Program,
            new Dictionary<IrVarId, IrValue> { [entry] = value }, 10000,
            new IrProgramReplayOptions(static _ => null)
            { AssignmentObserver = (instruction, assigned, _) => { if (instruction.Id == marker) { observations.Add(assigned.Boolean); } } });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Type, Is.EqualTo(nominalReturn ? entryType : factory.ObjectType));
        if (isNull)
        { Assert.That(replay.ReturnValue.Kind, Is.EqualTo(IrValueKind.Null)); }
        else
        { Assert.That(replay.ReturnValue.Reference, Is.SameAs(concrete)); }
        Assert.That(subject.RootMethod.Invoke(null, [concrete]), Is.SameAs(concrete));
        Assert.That(observations.Single(), Is.EqualTo(!isNull));
        Assert.That(JsonSerializer.Serialize(new ClaimManifestBuilder(subject.Compilation).Build().Manifest), Is.EqualTo(originalManifest));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var checkedCall = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(checkedCall.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(checkedCall.EntryModel[entry].Kind, Is.EqualTo(IrValueKind.Null));
        Assert.That(plan!.ReplayCallPrecondition(0, checkedCall.EntryModel, CancellationToken.None), Is.EqualTo(checkedCall.CallPreconditionWitness));
    }

    [Test]
    public async Task ObjectWidenedAliasGuardProvesRequiresAndMatchesClr()
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([NotNull] object value) => 7; }",
            """
            public sealed class Token { }
            public static class Subject {
                public static int Target(Token value, object alias) {
                    object widened = value;
                    if (widened == null || widened != alias) return 0;
                    return Library.Target(alias);
                }
            }
            """);
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, enableMetadataRequires: true);
        Assert.That(batch.Gaps, Is.Empty);
        var preparation = batch.Callers.Single().Body;
        var decoded = CompilerDecodedShadowBody.Decode(preparation.CallableId,
            JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(preparation)))!,
            CancellationToken.None, references);
        var factory = decoded.Body.Program.Factory;
        var entries = decoded.Body.Parameters.Select(parameter => parameter.Entry).ToArray();
        var token = Activator.CreateInstance(subject.RootMethod.DeclaringType!.Assembly.GetType("Token")!)!;
        foreach (var aliases in new[] { false, true })
        {
            var other = aliases ? token : new object();
            var observations = 0;
            var marker = decoded.Body.CallPreconditions.Single().Instruction;
            var replay = new IrProgramInterpreter(factory).Execute(decoded.Body.Program,
                new Dictionary<IrVarId, IrValue>
                {
                    [entries[0]] = factory.CreateReferenceValue(factory.GetVariableInfo(entries[0]).Type, token),
                    [entries[1]] = factory.CreateReferenceValue(factory.ObjectType, other)
                }, 10000,
                new IrProgramReplayOptions(static _ => null)
                { AssignmentObserver = (instruction, _, _) => { if (instruction.Id == marker) { observations++; } } });
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(aliases ? 7 : 0));
            Assert.That(observations, Is.EqualTo(aliases ? 1 : 0));
            Assert.That(subject.RootMethod.Invoke(null, [token, other]), Is.EqualTo(aliases ? 7 : 0));
        }
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.EnrollShadow(decoded), out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyCallPreconditionAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void NestedMetadataRequiresRetainsExplicitAbstention()
    {
        using var subject = new MetadataTestSubject("""
            using SharpProof.Attributes;
            public static class Library {
                public static int Target(object value) => Leaf(value);
                private static int Leaf([NotNull] object value) => 7;
            }
            """, "public sealed class Token { } public static class Subject { public static int Target(Token value) => Library.Target(value); }");
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, enableMetadataRequires: true);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
    }
}
