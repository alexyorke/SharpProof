using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Contracts;
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
    [TestCase("int", "Positive", "0", false)]
    [TestCase("int", "Positive", "1", true)]
    [TestCase("ulong", "Positive", "18446744073709551615", true)]
    [TestCase("byte", "InRange(-1, 300)", "255", true)]
    [TestCase("byte", "InRange(1, 254)", "255", false)]
    [TestCase("sbyte", "InRange(-129, 128)", "-128", true)]
    [TestCase("ulong", "InRange(-1, 9223372036854775807)", "18446744073709551615", false)]
    [TestCase("ulong", "InRange(-1, 9223372036854775807)", "9223372036854775807", true)]
    public void MetadataRequiresUseTypedEntryValuesWithoutSourceSyntax(string type, string attribute,
        string input, bool expected)
    {
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ [return: Positive] public static int Target([{attribute}] {type} value) => 1; }}",
            "public static class Subject { public static int Target(int value) => value; }");
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binder = new ContractBinder(subject.Compilation, factory);
        var binding = binder.BindTotalMetadataRequires(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(1));
        var clause = binding.Clauses.Single();
        Assert.That(clause.ParameterOrdinal, Is.Zero);
        Assert.That(clause.Attribute, Is.SameAs(method.Parameters[0].GetAttributes().Single()));
        Assert.That(clause.Attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(binder.BindTotalRequires(context).Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
        var entry = context.Parameters[0].Entry;
        var values = new Dictionary<IrVarId, IrValue>
        {
            [entry] = type == "ulong"
                ? factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type,
                    ulong.Parse(input, System.Globalization.CultureInfo.InvariantCulture))
                : factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type,
                    long.Parse(input, System.Globalization.CultureInfo.InvariantCulture))
        };
        var interpreter = new IrInterpreter(factory);
        Assert.That(interpreter.Evaluate(clause.Value, values).Value!.Boolean, Is.EqualTo(expected));
        Assert.That(interpreter.Evaluate(clause.SafeCondition, values).Value!.Boolean, Is.True);
    }

    [TestCase("object", false)]
    [TestCase("object", true)]
    [TestCase("string", false)]
    [TestCase("string", true)]
    public void MetadataRequiresNotNullPreservesReferenceDomain(string type, bool nonnull)
    {
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ public static int Target([NotNull] {type} value) => 1; }}",
            "public static class Subject { public static int Target(int value) => value; }");
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binding = new ContractBinder(subject.Compilation, factory).BindTotalMetadataRequires(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        var entry = context.Parameters[0].Entry;
        var entryType = factory.GetVariableInfo(entry).Type;
        var value = !nonnull ? factory.CreateNullValue(entryType) : type == "string"
            ? factory.CreateStringValue("value") : factory.CreateReferenceValue(entryType, new object());
        var interpreter = new IrInterpreter(factory);
        var values = new Dictionary<IrVarId, IrValue> { [entry] = value };
        Assert.That(interpreter.Evaluate(binding.Clauses.Single().Value, values).Value!.Boolean, Is.EqualTo(nonnull));
    }

    [TestCase("[Positive] int first, [InRange(10, 1)] int second", false, 0)]
    [TestCase("[Positive, InRange(1, 10)] int value", true, 2)]
    [TestCase("[System.Runtime.InteropServices.In] int value", true, 0)]
    public void MetadataRequiresBindingIsTransactional(string parameters, bool success, int count)
    {
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ public static int Target({parameters}) => 1; }}",
            "public static class Subject { public static int Target(int value) => value; }");
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binding = new ContractBinder(subject.Compilation, factory).BindTotalMetadataRequires(context);
        Assert.That(binding.IsSuccess, Is.EqualTo(success));
        Assert.That(binding.Clauses, Has.Length.EqualTo(count));
        Assert.That(binding.Failure, Is.EqualTo(success ? ContractBindingFailure.None : ContractBindingFailure.InvalidClosedAttribute));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() =>
            new ContractBinder(subject.Compilation, factory).BindTotalMetadataRequires(context, cancellation.Token)));
        var foreignContext = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method);
        Assert.Throws<ArgumentException>(new Action(() =>
            new ContractBinder(subject.Compilation, factory).BindTotalMetadataRequires(foreignContext)));
    }

    [Test]
    public void ConcatenationAllocationIdentityRemainsOutsideNativeObservationProofs()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static bool Target(string first, string second) { return (object)first == (object)second; } }",
            """
            using SharpProof.Attributes;
            public static class Subject { public static bool Target(string value) {
                Contract.Ensures(!Contract.Result<bool>());
                return Library.Target(string.Concat("left", "right"), value);
            } }
            """);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAssignInstruction>().Any(assign => assign.Value is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat }), Is.True);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out _, out _), Is.False);
    }

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

[TestFixture]
public sealed class MetadataClosedAttributeEvidenceTests
{
    [TestCase("positive", 1)]
    [TestCase("range", 1)]
    [TestCase("notnull", 1)]
    [TestCase("duplicate", 0)]
    [TestCase("truncated", 0)]
    [TestCase("wrong-constructor", 0)]
    [TestCase("lookalike", 0)]
    [TestCase("reversed-range", 0)]
    public void CapturesOwnedRowsFromAuthenticatedImage(string mode, int expectedCount)
    {
        using var directory = new TempDirectory("sharpproof-evidence-prototype-");
        var bytes = CreateImage(mode);
        var path = Path.Combine(directory.FullName, "MetadataTarget.dll");
        File.WriteAllBytes(path, bytes);
        var compilation = CSharpCompilation.Create("MetadataConsumer",
            [CSharpSyntaxTree.ParseText("public static class Consumer { }")],
            TestMetadataReferences.WithSharpProof.Add(MetadataReference.CreateFromFile(path)),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("MetadataTarget")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        if (mode == "duplicate")
        {
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var binding = new ContractBinder(compilation, factory).BindTotalMetadataRequires(new(factory, method));
            Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
            Assert.That(binding.Clauses, Is.Empty);
        }
        var body = new CompilerTotalIlBodyProvider(compilation, null).Resolve(method, CancellationToken.None);
        if (expectedCount == 0)
        {
            Assert.That(body, Is.Null);
            return;
        }
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParameterAttributes, Has.Length.EqualTo(expectedCount));
        Assert.That(body.ImageSha256.ToUpperInvariant(), Is.EqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        var reader = pe.GetMetadataReader();
        Assert.That(body.ModuleMvid, Is.EqualTo(reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D")));
        Assert.That(body.ParameterAttributes.Select(static row => row.AttributeToken).Distinct().Count(), Is.EqualTo(expectedCount));
        foreach (var row in body.ParameterAttributes)
        {
            Assert.That(row.MethodToken, Is.EqualTo(method.MetadataToken));
            Assert.That(row.ParameterOrdinal, Is.EqualTo(1));
            Assert.That(row.ParameterSequence, Is.EqualTo(2));
            var attribute = reader.GetCustomAttribute((CustomAttributeHandle)MetadataTokens.Handle(row.AttributeToken));
            Assert.That(MetadataTokens.GetToken(attribute.Parent), Is.EqualTo(row.ParameterToken));
            Assert.That(MetadataTokens.GetToken(attribute.Constructor), Is.EqualTo(row.ConstructorToken));
            Assert.That(row.ValueBlob, Is.EqualTo(reader.GetBlobBytes(attribute.Value)));
            Assert.That(row.ConstructorIdentity, Does.Contain("SharpProof.Attributes"));
            if (mode == "range")
            {
                Assert.That(row.Minimum, Is.EqualTo(1));
                Assert.That(row.Maximum, Is.EqualTo(10));
            }
        }
        Assert.That(reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(method.MetadataToken)).GetParameters().Count, Is.EqualTo(1), "The first argument has no Param row.");
    }

    internal static byte[] CreateImage(string mode)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("MetadataTarget.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("MetadataTarget"), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, System.Reflection.AssemblyHashAlgorithm.None);
        var attributesName = typeof(SharpProof.Attributes.Contract).Assembly.GetName();
        var attributes = metadata.AddAssemblyReference(metadata.GetOrAddString(mode == "lookalike" ? "Other.Attributes" : attributesName.Name!), attributesName.Version!, default, metadata.GetOrAddBlob(attributesName.GetPublicKeyToken() ?? []), (AssemblyFlags)0, default);
        var coreName = typeof(object).Assembly.GetName();
        var core = metadata.AddAssemblyReference(metadata.GetOrAddString(coreName.Name!), coreName.Version!, default, metadata.GetOrAddBlob(coreName.GetPublicKeyToken() ?? []), (AssemblyFlags)0, default);
        var objectType = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var inRange = mode is "range" or "reversed-range";
        var typeName = inRange ? "InRangeAttribute" : mode == "notnull" ? "NotNullAttribute" : "PositiveAttribute";
        var attributeType = metadata.AddTypeReference(attributes, metadata.GetOrAddString("SharpProof.Attributes"), metadata.GetOrAddString(typeName));
        var constructorSignature = inRange ? new byte[] { 0x20, 2, 1, 0x0a, 0x0a } :
            mode == "wrong-constructor" ? new byte[] { 0x20, 1, 1, 8 } : new byte[] { 0x20, 0, 1 };
        var constructor = metadata.AddMemberReference(attributeType, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(constructorSignature));
        var parameter = metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("value"), 2);
        var attributeBlob = new BlobBuilder();
        if (mode == "truncated")
        {
            attributeBlob.WriteByte(1);
        }
        else
        {
            attributeBlob.WriteUInt16(1);
            if (inRange)
            {
                attributeBlob.WriteInt64(mode == "reversed-range" ? 10 : 1);
                attributeBlob.WriteInt64(mode == "reversed-range" ? 1 : 10);
            }
            attributeBlob.WriteUInt16(0);
        }
        metadata.AddCustomAttribute(parameter, constructor, metadata.GetOrAddBlob(attributeBlob));
        if (mode == "duplicate")
        {
            metadata.AddCustomAttribute(parameter, constructor, metadata.GetOrAddBlob(attributeBlob));
        }
        var signature = new byte[] { 0, 2, 8, 8, mode == "notnull" ? (byte)0x0e : (byte)8 };
        metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.Managed, metadata.GetOrAddString("Read"), metadata.GetOrAddBlob(signature), 4, parameter);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract, default, metadata.GetOrAddString("MetadataTarget"), objectType, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var il = new BlobBuilder();
        il.WriteBytes(new byte[] { 0, 0, 0, 0, 0x0a, 0x02, 0x2a });
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll), new MetadataRootBuilder(metadata), il, flags: CorFlags.ILOnly).Serialize(image);
        return image.ToArray();
    }
}

[TestFixture]
public sealed class MetadataClosedAttributeEvidencePairingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void UnrelatedLocalAttributesRemainAdmitted(bool generic)
    {
        using var directory = new TempDirectory("sharpproof-evidence-pairing-");
        var attribute = generic ? "Local<int>" : "Local";
        var definition = generic ? "LocalAttribute<T>" : "LocalAttribute";
        var (compilation, method, image) = CreateSubject(directory.FullName,
            "using System; public sealed class " + definition + " : Attribute { } public static class Library { public static int Target([" + attribute + "] int value) => value; }");
        using var readerImage = new PEReader(new MemoryStream(image, writable: false));
        var reader = readerImage.GetMetadataReader();
        var methodDefinition = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(method.MetadataToken));
        var parameter = reader.GetParameter(methodDefinition.GetParameters().Single());
        var row = reader.GetCustomAttribute(parameter.GetCustomAttributes().Single());
        Assert.That(row.Constructor.Kind, Is.EqualTo(generic ? HandleKind.MemberReference : HandleKind.MethodDefinition));
        if (generic)
        {
            Assert.That(reader.GetMemberReference((MemberReferenceHandle)row.Constructor).Parent.Kind, Is.EqualTo(HandleKind.TypeSpecification));
        }
        var body = new CompilerTotalIlBodyProvider(compilation, null).Resolve(method, CancellationToken.None);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParameterAttributes, Is.Empty);
    }

    [Test]
    public void BoundClausesPairByIdentityWithDistinctTokensWhenOrderChanges()
    {
        using var directory = new TempDirectory("sharpproof-evidence-pairing-");
        var (compilation, method, _) = CreateSubject(directory.FullName,
            "using SharpProof.Attributes; public static class Library { public static int Target([InRange(1L, 10L), Positive] int value) => value; }");
        var body = new CompilerTotalIlBodyProvider(compilation, null).Resolve(method, CancellationToken.None);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParameterAttributes, Has.Length.EqualTo(2));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new ContractBinder(compilation, factory).BindTotalMetadataRequires(new(factory, method));
        Assert.That(binding.IsSuccess, Is.True);
        Assert.That(binding.Clauses, Has.Length.EqualTo(2));
        var reversed = binding.Clauses.Reverse().ToImmutableArray();
        Assert.That(CompilerTotalIlBodyProvider.TryPairMetadataRequires(reversed, body.ParameterAttributes,
            CancellationToken.None, out var paired), Is.True);
        Assert.That(paired.Select(static row => row.AttributeToken).Distinct().Count(), Is.EqualTo(2));
        for (var ordinal = 0; ordinal < reversed.Length; ordinal++)
        {
            Assert.That(paired[ordinal].ClauseOrdinal, Is.EqualTo(ordinal));
            Assert.That(paired[ordinal].ParameterOrdinal, Is.EqualTo(reversed[ordinal].ParameterOrdinal));
            Assert.That(paired[ordinal].AttributeIdentity, Is.EqualTo(CompilerIdentityBridge.CreateSymbolDisplay(reversed[ordinal].Attribute.AttributeClass)));
            Assert.That(paired[ordinal].ConstructorIdentity, Is.EqualTo(CompilerIdentityBridge.CreateSymbolDisplay(reversed[ordinal].Attribute.AttributeConstructor)));
            Assert.That(paired[ordinal].Kind, Is.EqualTo(reversed[ordinal].Validation.Kind.ToString()));
            Assert.That(paired[ordinal].Minimum, Is.EqualTo(reversed[ordinal].Validation.Minimum));
            Assert.That(paired[ordinal].Maximum, Is.EqualTo(reversed[ordinal].Validation.Maximum));
            Assert.That(paired[ordinal].AttributeToken, Is.EqualTo(body.ParameterAttributes.Single(row => row.Kind == paired[ordinal].Kind).AttributeToken));
        }
        Assert.That(CompilerTotalIlBodyProvider.TryPairMetadataRequires(
            [binding.Clauses[0], binding.Clauses[0]], body.ParameterAttributes, CancellationToken.None, out _), Is.False, "A metadata token cannot be consumed twice.");
        Assert.That(CompilerTotalIlBodyProvider.TryPairMetadataRequires(
            binding.Clauses, [body.ParameterAttributes[0]], CancellationToken.None, out _), Is.False, "Missing evidence cannot bind.");
    }

    [Test]
    public void InvalidLaterAttributeDiscardsEarlierBindings()
    {
        using var directory = new TempDirectory("sharpproof-evidence-pairing-");
        var (compilation, method, _) = CreateSubject(directory.FullName,
            "using SharpProof.Attributes; public static class Library { public static int Target([Positive, InRange(10L, 1L)] int value) => value; }");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new ContractBinder(compilation, factory).BindTotalMetadataRequires(new(factory, method));
        Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(binding.Clauses, Is.Empty);
        Assert.That(new CompilerTotalIlBodyProvider(compilation, null).Resolve(method, CancellationToken.None), Is.Null);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method, byte[] Image) CreateSubject(string directory, string source)
    {
        var library = TestCompilation.Create("EvidencePairingLibrary", source);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True);
        var bytes = image.ToArray();
        var path = Path.Combine(directory, "EvidencePairingLibrary.dll");
        File.WriteAllBytes(path, bytes);
        var compilation = CSharpCompilation.Create("EvidencePairingConsumer",
            [CSharpSyntaxTree.ParseText("public static class Consumer { }")],
            TestMetadataReferences.WithSharpProof.Add(MetadataReference.CreateFromFile(path)),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        return (compilation, method, bytes);
    }
}

[TestFixture]
public sealed class MetadataClosedAttributeEvidenceIdentityTests
{
    [TestCase("normal", true)]
    [TestCase("retargetable", false)]
    [TestCase("winrt", false)]
    [TestCase("reserved", false)]
    [TestCase("token-budget", false)]
    [TestCase("key-budget", false)]
    [TestCase("empty-public-key", false)]
    public void AssemblyReferenceFlagsAndKeyLengthsAreCheckedBeforeCopy(string mode, bool accepted)
    {
        var compilation = TestCompilation.Create("AssemblyIdentityProbe", "public static class Subject { }");
        var expected = compilation.GetTypeByMetadataName("SharpProof.Attributes.PositiveAttribute")!.ContainingAssembly.Identity;
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("IdentityProbe.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("IdentityProbe"), new Version(1, 0, 0, 0), default, default, (AssemblyFlags)0, System.Reflection.AssemblyHashAlgorithm.None);
        var flags = mode switch
        {
            "retargetable" => AssemblyFlags.Retargetable,
            "winrt" => (AssemblyFlags)0x0200,
            "reserved" => (AssemblyFlags)0x4000,
            "key-budget" or "empty-public-key" => AssemblyFlags.PublicKey,
            _ => (AssemblyFlags)0
        };
        var key = mode is "token-budget" or "key-budget" ? new byte[65_536] : [];
        var handle = metadata.AddAssemblyReference(metadata.GetOrAddString(expected.Name), expected.Version,
            string.IsNullOrEmpty(expected.CultureName) ? default : metadata.GetOrAddString(expected.CultureName),
            metadata.GetOrAddBlob(key), flags, default);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly).Serialize(image);
        using var pe = new PEReader(new MemoryStream(image.ToArray(), writable: false));
        var reader = pe.GetMetadataReader();
        Assert.That(CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(reader, handle, expected, out var actual), Is.EqualTo(accepted));
        if (accepted)
        {
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual.IsRetargetable, Is.EqualTo(expected.IsRetargetable));
            Assert.That(actual.ContentType, Is.EqualTo(expected.ContentType));
        }
        else
        {
            Assert.That(actual, Is.Null);
        }
    }
}
