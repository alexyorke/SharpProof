using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NativeIntegerConditionalEmissionApiTests
{
    private static readonly string[] EnabledSymbols = ["ABSENT"];

    // SP-NATIVE-CONDITIONAL-001. Frozen before production changes at f1195945502a9393621576f4fc305513e9dbf840.
    // Roslyn's TypeDefinitions_FromSource fixture emits the same minimal core-library family:
    // https://github.com/dotnet/roslyn/blob/8edf7bcd4f1594c3d68a6a567469f41dbd33dd1b/src/Compilers/CSharp/Test/Semantic/Semantics/NativeIntegerTests.cs#L113
    // Both images below are ordinary C# Emit output. The custom core-library images are never CLR-loaded.
    private const string CoreLibrarySource = """
        namespace System
        {
            public class Object { }
            public class String { }
            public abstract class ValueType { }
            public class Attribute { }
            public struct Void { }
            public struct Boolean { }
            public struct Int32 { }
            public struct UIntPtr { }
            public struct IntPtr
            {
                [Diagnostics.Conditional("ABSENT")]
                public void Trace() { }
            }
        }
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : Attribute
            {
                public ConditionalAttribute(string symbol) { }
            }
        }
        """;

    private const string NativeCallerSource = """
        public static class Subject
        {
            public static int Target()
            {
                ((nint)default(System.IntPtr)).Trace();
                return 7;
            }
        }
        """;

    private const string DirectCallerSource = """
        public static class Subject
        {
            public static int Target()
            {
                default(System.IntPtr).Trace();
                return 7;
            }
        }
        """;

    [TestCase(true, false, TestName = "SP_NATIVE_CONDITIONAL_001_NativeAbsent")]
    [TestCase(true, true, TestName = "SP_NATIVE_CONDITIONAL_001_NativeEnabled")]
    [TestCase(false, false, TestName = "SP_NATIVE_CONDITIONAL_001_DirectAbsent")]
    [TestCase(false, true, TestName = "SP_NATIVE_CONDITIONAL_001_DirectEnabled")]
    public void CompilerEmittedNativeConditionalMethodsRetainEmissionSemantics(bool native, bool enabled)
    {
        var core = Compile("NativeConditionalCore", CoreLibrarySource, false, []);
        var coreBytes = Emit(core);
        var coreReference = MetadataReference.CreateFromImage(coreBytes);
        var source = native ? NativeCallerSource : DirectCallerSource;
        var compilation = Compile("NativeConditionalCaller", source, enabled, [coreReference]);
        var bytes = Emit(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var invocationSyntax = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        var method = invocation.TargetMethod;
        var raw = compilation.GetSpecialType(SpecialType.System_IntPtr).GetMembers("Trace").OfType<IMethodSymbol>().Single();
        var attribute = raw.GetAttributes().Single();
        var calls = TraceCalls(bytes);

        Assert.That(((CSharpParseOptions)tree.Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp9));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        Assert.That(compilation.Options.OutputKind, Is.EqualTo(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames,
            Is.EqualTo(enabled ? EnabledSymbols : Array.Empty<string>()));
        Assert.That(method.IsConditional, Is.True);
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.OriginalDefinition, Is.SameAs(method));
        Assert.That(method.ContainingType.IsNativeIntegerType, Is.EqualTo(native));
        Assert.That(method.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_IntPtr));
        Assert.That(MetadataTokens.Handle(raw.MetadataToken).Kind, Is.EqualTo(HandleKind.MethodDefinition));
        Assert.That(MetadataTokens.GetRowNumber((MethodDefinitionHandle)MetadataTokens.Handle(raw.MetadataToken)), Is.GreaterThan(0));
        Assert.That(method.MetadataToken, Is.EqualTo(native ? 0 : raw.MetadataToken));
        // The wrapper forwards conditional decoding, but its public attribute list is empty.
        Assert.That(method.GetAttributes(), Has.Length.EqualTo(native ? 0 : 1));
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingModule, raw.ContainingModule), Is.True);
        var metadata = method.ContainingModule.GetMetadata();
        Assert.That(metadata, Is.Not.Null);
        // Borrow the compilation-owned metadata only; it is not disposed by this fixture.
        Assert.That(metadata!.GetMetadataReader().MethodDefinitions.Count, Is.GreaterThan(0));
        Assert.That(attribute.AttributeClass!.ToDisplayString(), Is.EqualTo("System.Diagnostics.ConditionalAttribute"));
        Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(attribute.AttributeConstructor, Is.Not.Null);
        Assert.That(attribute.ConstructorArguments, Has.Length.EqualTo(1));
        Assert.That(attribute.ConstructorArguments[0].Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo("ABSENT"));
        Assert.That(calls, Has.Length.EqualTo(enabled ? 1 : 0));
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            Bug = "SP-NATIVE-CONDITIONAL-001",
            Baseline = "f1195945502a9393621576f4fc305513e9dbf840",
            native,
            enabled,
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            method.IsConditional,
            method.MetadataToken,
            RawMetadataToken = raw.MetadataToken,
            NativeContainingType = method.ContainingType.IsNativeIntegerType,
            Attribute = attribute.ToString(),
            EmittedTraceCalls = calls,
            CoreSource = CoreLibrarySource,
            CallerSource = source
        }));

        // Valid compiler-produced metadata must preserve conditional omission through the native wrapper.
        var policy = new CSharpInvocationEmissionPolicy(compilation);
        bool elided = false;
        Assert.DoesNotThrow((Action)(() => elided = policy.IsElided(invocation)));
        Assert.That(elided, Is.EqualTo(!enabled));
        var graph = ControlFlowGraph.Create(targetSyntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        FrontendProgramLoweringResult? lowered = null;
        Assert.DoesNotThrow((Action)(() => lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context)));
        Assert.That(lowered, Is.Not.Null);
        Assert.That(lowered!.ConstructionLimitExceeded, Is.False);
        if (!enabled)
        {
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FrameworkConditionalControlAgreesWithClr(bool enabled)
    {
        const string source = """
            public static class Subject
            {
                private static int calls;
                [System.Diagnostics.Conditional("ABSENT")]
                private static void Trace() { calls++; }
                public static int Target() { Trace(); return 7 + calls; }
            }
            """;
        var compilation = Compile("FrameworkConditionalControl", source, enabled, TestMetadataReferences.Platform);
        var bytes = Emit(compilation);
        Assert.That(TraceCalls(bytes), Has.Length.EqualTo(enabled ? 1 : 0));
        var runtime = new AssemblyLoadContext("FrameworkConditionalControl", isCollectible: true);
        try
        {
            using var image = new MemoryStream(bytes);
            var subject = runtime.LoadFromStream(image).GetType("Subject")!;
            Assert.That(subject.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(enabled ? 8 : 7));
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static CSharpCompilation Compile(string name, string source, bool enabled, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: enabled ? EnabledSymbols : []), name + ".cs");
        var compilation = CSharpCompilation.Create(name, [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, options: new EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return image.ToArray();
    }

    private static string[] TraceCalls(byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        var target = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .Single(method => metadata.GetString(method.Name) == "Target");
        var il = pe.GetMethodBody(target.RelativeVirtualAddress).GetILBytes()!;
        var calls = new List<string>();
        for (var offset = 0; offset < il.Length;)
        {
            ushort value = il[offset++];
            if (value == 0xfe)
            { value = (ushort)(0xfe00 | il[offset++]); }
            var opcode = OpCodesByValue[value];
            var size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            Assert.That(offset + size, Is.LessThanOrEqualTo(il.Length));
            if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
            {
                var handle = MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset));
                var name = handle.Kind == HandleKind.MethodDefinition
                    ? metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Name)
                    : metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)handle).Name);
                if (name == "Trace")
                { calls.Add(name); }
            }
            offset += size;
        }
        return [.. calls];
    }

    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(opcode => unchecked((ushort)opcode.Value))
        .ToDictionary(group => group.Key, group => group.First());
}
