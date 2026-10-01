using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using Microsoft.CodeAnalysis;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TypedMetadataReferenceTests
{
    [TestCase("object", "bool", "return value == null;")]
    [TestCase("object", "bool", "return value != null;")]
    [TestCase("string", "bool", "return value == null;")]
    [TestCase("string", "bool", "return value != null;")]
    [TestCase("object", "bool", "return value == Again(value);")]
    [TestCase("string", "bool", "return (object)value == (object)Again(value);")]
    [TestCase("object", "object", "return value;")]
    [TestCase("string", "string", "return value;")]
    [TestCase("object", "object", "return null;")]
    [TestCase("string", "string", "return null;")]
    [TestCase("object", "object", "value = null; return value;")]
    [TestCase("string", "string", "value = null; return value;")]
    [TestCase("object", "object", "return value == null ? null : Again(value);")]
    [TestCase("string", "string", "return value == null ? null : Again(value);")]
    [TestCase("object", "object", "object local = value; if (local != null) local = null; return local;")]
    [TestCase("string", "string", "string local = value; if (local != null) local = null; return local;")]
    public void ReferenceTransfersMatchCompiledIdentity(string type, string resultType, string body)
    {
        using var subject = new MetadataTestSubject(
            $"public static class Library {{ public static {resultType} Target({type} value) {{ {body} }} private static {type} Again({type} value) {{ return value; }} }}",
            $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject { public static {{resultType}} Target({{type}} value) {
                Contract.Ensures(true); return Library.Target(value);
            } }
            """);
        var metadataMethod = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var decoded = new CompilerTotalIlBodyProvider(subject.Compilation, null).Resolve(metadataMethod, CancellationToken.None);
        Assert.That(decoded, Is.Not.Null, "Captured reference implementation must decode.");
        Assert.That(TotalIlStack.TryValidate(decoded!, _ => true, out _), Is.True, "Reference stack must validate.");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var total = preparations.Single().Total;
        Assert.That(total, Is.Not.Null);
        var factory = total!.Program.Factory;
        var entry = total.Parameters.Single().Entry;
        var entryType = factory.GetVariableInfo(entry).Type;
        object?[] inputs = [null, type == "string" ? new string('x', 3) : new object()];
        foreach (var input in inputs)
        {
            var compiled = subject.Invoke(input!);
            var value = input == null ? factory.CreateNullValue(entryType) : type == "string"
                ? factory.CreateStringValue((string)input) : factory.CreateReferenceValue(entryType, input);
            var execution = new IrProgramInterpreter(factory).Execute(total.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = value });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            if (resultType == "bool")
            { Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo(compiled)); }
            else if (compiled == null)
            { Assert.That(execution.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Null)); }
            else
            {
                Assert.That(ReferenceEquals(compiled, input), Is.True);
                Assert.That(ReferenceEquals(execution.ReturnValue, value), Is.True);
            }
        }
    }

    [TestCase("Neg")]
    [TestCase("Conv_i4")]
    [TestCase("Add")]
    [TestCase("Cgt")]
    [TestCase("Cgt_un")]
    [TestCase("Bgt_un")]
    public void ReferenceArithmeticAndOrderingRemainClosed(string opcode)
    {
        var result = opcode == "Conv_i4" ? "int" : opcode is "Cgt" or "Cgt_un" ? "bool" : "object";
        var compilation = TestCompilation.Create("Shape", $"public static class Subject {{ public static {result} Target(object value) {{ throw null; }} }}");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        System.Collections.Immutable.ImmutableArray<TotalIlInstruction> instructions = opcode is "Neg" or "Conv_i4"
            ? [new(0, "Ldarg", 0), new(1, opcode, 0), new(2, "Ret", 0)]
            : opcode == "Bgt_un"
                ? [new(0, "Ldarg", 0), new(1, "Ldarg", 0), new(2, opcode, 0, 3), new(3, "Ldarg", 0), new(4, "Ret", 0)]
                : [new(0, "Ldarg", 0), new(1, "Ldarg", 0), new(2, opcode, 0), new(3, "Ret", 0)];
        var body = new TotalIlBody(method, new string('0', 64), "shape", [], true, 2,
            instructions);
        Assert.That(TotalIlStack.TryValidate(body, _ => true, out _), Is.False);
    }

    [TestCase("string", "object", "return value;")]
    [TestCase("object", "string", "return (string)value;")]
    [TestCase("string", "bool", "return value == \"x\";")]
    public void ReferenceConversionsAndContentCallsRemainClosed(string parameter, string result, string body)
    {
        using var subject = new MetadataTestSubject($"public static class Library {{ public static {result} Target({parameter} value) {{ {body} }} }}",
            $"using SharpProof.Attributes; public static class Subject {{ public static {result} Target({parameter} value) {{ Contract.Ensures(true); return Library.Target(value); }} }}");
        Assert.That(subject.CreateArtifact().Callables.Single().Total, Is.Null);
    }

    [TestCase("object", true)]
    [TestCase("object", false)]
    [TestCase("string", true)]
    [TestCase("string", false)]
    public async Task NativeNullGoalsProveAndRefuteThroughOwnedMetadataReplay(string type, bool equal)
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var comparison = equal ? "==" : "!=";
        using var subject = new MetadataTestSubject(
            $"public static class Library {{ public static bool Target({type} value) {{ return Again(value) {comparison} null; }} private static {type} Again({type} value) {{ return value; }} }}",
            $"using SharpProof.Attributes; public static class Subject {{ public static bool Target({type} value) {{ Contract.Ensures(true); return Library.Target(value); }} }}");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var total = preparations.Single().Total;
        Assert.That(total, Is.Not.Null);
        var factory = total!.Program.Factory;
        var parameter = total.Parameters.Single();
        var input = factory.Variable(parameter.Entry);
        var expected = factory.Binary(equal ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual, input, factory.Null(input.Type));
        var goal = factory.Binary(IrBinaryOperator.Equal, factory.Variable(total.Result!.Value), expected);
        var site = factory.CreateOperation("metadata-reference-postcondition");
        var candidate = new PassiveCallableCandidate("reference", total.Program,
            [new(parameter.Entry, parameter.Current, parameter.Old)], total.Result, [],
            [new(goal, factory.Boolean(true), site), new(factory.Unary(IrUnaryOperator.Not, goal), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var proven = await solver.VerifyEnsuresAsync(0);
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(proven.Outcome, Is.InstanceOf<ProvenOutcome>(), proven.Reason.ToString());
        Assert.That(refuted.Outcome, Is.InstanceOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(new[] { parameter.Entry }));
    }

    [Test]
    public async Task StringIdentityComparisonDistinguishesEqualContentAndPreservesNativeReplay()
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var subject = new MetadataTestSubject(
            "public static class Library { public static bool Target(string first, string second) { return (object)first == (object)second; } }",
            "using SharpProof.Attributes; public static class Subject { public static bool Target(string first, string second) { Contract.Ensures(true); return Library.Target(first, second); } }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var total = preparations.Single().Total;
        Assert.That(total, Is.Not.Null);
        var factory = total!.Program.Factory;
        var firstParameter = total.Parameters[0];
        var secondParameter = total.Parameters[1];
        var first = new string('x', 3);
        foreach (var alias in new[] { true, false })
        {
            var second = alias ? first : new string('x', 3);
            Assert.That(subject.Method.Invoke(null, [first, second]), Is.EqualTo(alias));
            var execution = new IrProgramInterpreter(factory).Execute(total.Program,
                new Dictionary<IrVarId, IrValue>
                { [firstParameter.Entry] = factory.CreateStringValue(first), [secondParameter.Entry] = factory.CreateStringValue(second) });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo(alias));
        }
        var x = factory.Variable(firstParameter.Entry);
        var y = factory.Variable(secondParameter.Entry);
        var expected = factory.Binary(IrBinaryOperator.Equal, x, y);
        var goal = factory.Binary(IrBinaryOperator.Equal, factory.Variable(total.Result!.Value), expected);
        var site = factory.CreateOperation("metadata-string-identity");
        var candidate = new PassiveCallableCandidate("identity", total.Program,
            [.. total.Parameters.Select(parameter => new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.Old))], total.Result,
            [new(factory.Binary(IrBinaryOperator.NotEqual, x, factory.Null(x.Type)), factory.Boolean(true), site),
                new(factory.Binary(IrBinaryOperator.NotEqual, y, factory.Null(y.Type)), factory.Boolean(true), site),
                new(factory.Binary(IrBinaryOperator.Equal, factory.Length(x), factory.Integer(3)), factory.Boolean(true), site),
                new(factory.Binary(IrBinaryOperator.Equal, factory.Length(y), factory.Integer(3)), factory.Boolean(true), site)],
            [new(goal, factory.Boolean(true), site), new(factory.Unary(IrUnaryOperator.Not, goal), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(total.Parameters.Select(parameter => parameter.Entry)));
    }
}
