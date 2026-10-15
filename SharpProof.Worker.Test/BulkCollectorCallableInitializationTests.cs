using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkCollectorCallableInitializationTests
{
    [TestCase("7", 7)]
    [TestCase("-4", -4)]
    [TestCase("invalid", -1)]
    public async Task EmittedScriptEventRetainsCompilerGeneratedDesignationField(string input, int expected)
    {
        // Script events can introduce a static expression variable without a
        // VariableDeclaratorSyntax. This tests the SDK declaration boundary;
        // it makes no claim about script admission by an MSBuild host.
        var tree = CSharpSyntaxTree.ParseText($$"""
            static event System.Action Ready = int.TryParse("{{input}}", out var parsed) ? (() => { }) : null;
            [SharpProof.Attributes.ZeroAllocations] static int Read() => 1;
            Ready != null ? parsed : -1
            """, new CSharpParseOptions(LanguageVersion.CSharp12, kind: SourceCodeKind.Script),
            path: "event.csx");
        var compilation = CSharpCompilation.CreateScriptCompilation(
            "EventDesignation_" + Guid.NewGuid().ToString("N"), tree,
            TestMetadataReferences.WithSharpProof, TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary),
            returnType: typeof(object));
        TestCompilation.AssertNoErrors(compilation);
        var root = await tree.GetRootAsync();
        var designation = root.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Single();
        var field = (IFieldSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(designation)!;
        var owner = field.ContainingType;
        var fieldDeclaration = await field.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner.IsScriptClass, Is.True);
            Assert.That(field.IsStatic, Is.True);
            Assert.That(field.IsConst, Is.False);
            Assert.That(field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(fieldDeclaration, Is.TypeOf<SingleVariableDesignationSyntax>());
            Assert.That(owner.GetMembers().OfType<IFieldSymbol>(), Does.Contain(field));
            Assert.That(owner.StaticConstructors.All(constructor => constructor.IsImplicitlyDeclared), Is.True);
        }

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("EventDesignation", isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = runtime.LoadFromStream(image);
            var entry = compilation.GetEntryPoint(CancellationToken.None)!;
            var factory = assembly.GetType(entry.ContainingType.MetadataName, throwOnError: true)!
                .GetMethod(entry.MetadataName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            var execution = (Task<object>)factory.Invoke(null, [new object[2]])!;
            Assert.That(await execution, Is.EqualTo(expected));
        }
        finally { runtime.Unload(); }

        var body = Prepare(compilation);
        Assert.That(body.EffectsCompleteAtEntry, Is.True);
        var replay = new IrProgramInterpreter(body.Program.Factory).Execute(body.Program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(1));
    }

    [TestCase("public static readonly int Value = 7;", true)]
    [TestCase("public static int Value = 7;", false)]
    [TestCase("public static int Value { get; } = 7;", true)]
    [TestCase("public static int Value { get; set; } = 7;", false)]
    public void OrdinaryScalarInitializersRetainTheirCompilerDeclarationAdmission(string declaration, bool expected)
    {
        var compilation = TestCompilation.Create("ScalarInitialization",
            "public static class Subject { " + declaration +
            " [SharpProof.Attributes.ZeroAllocations] public static int Read() => Value; }");
        var owner = compilation.GetTypeByMetadataName("Subject")!;
        var value = owner.GetMembers("Value").Single();
        Assert.That(value.DeclaringSyntaxReferences.Single().GetSyntax(),
            Is.InstanceOf<VariableDeclaratorSyntax>().Or.InstanceOf<PropertyDeclarationSyntax>());
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ScalarInitialization", isCollectible: true);
        try
        {
            image.Position = 0;
            var read = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Read")!;
            Assert.That(read.Invoke(null, null), Is.EqualTo(7));
        }
        finally { runtime.Unload(); }

        Assert.That(Prepare(compilation).EffectsCompleteAtEntry, Is.EqualTo(expected));
    }

    private static CompilerTotalCallablePreparation Prepare(CSharpCompilation compilation)
    {
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        Assert.That(preparation.Total, Is.Not.Null);
        return preparation.Total!;
    }
}
