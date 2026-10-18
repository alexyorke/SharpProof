using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
public sealed class GenericNativeIntegerConditionalEmissionApiTests
{
    private static readonly string[] EnabledSymbols = ["ABSENT"];
    private static readonly byte[] OmittedTargetIl = [0x1d, 0x2a];
    private static readonly int[] UnderlyingArities = [0, 1];
    private static readonly int[] NativeArities = [0];

    // Genuine matcher rejection controls: native wrappers exclude generic methods,
    // while the underlying type still publishes both own Trace members. These
    // custom core-library images are emitted successfully and never CLR-loaded.
    private const string CoreSource = """
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
                [Diagnostics.Conditional("OTHER")]
                public void Trace<T>() { }
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

    [TestCase(false, TestName = "SP_NATIVE_CONDITIONAL_001_GenericCandidateAbsent")]
    [TestCase(true, TestName = "SP_NATIVE_CONDITIONAL_001_GenericCandidateEnabled")]
    public void GenericUnderlyingCandidatesDoNotMakeTheNativeDeclarationAmbiguous(bool enabled)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: enabled ? EnabledSymbols : []);
        const string core = CoreSource;
        const string call = "((nint)default(System.IntPtr)).Trace();";
        var source = "public static class Subject { public static int Target() { " + call + " return 7; } }";
        var coreTree = CSharpSyntaxTree.ParseText(core, options, "source-native-core.cs");
        var callerTree = CSharpSyntaxTree.ParseText(source, options, "source-native-caller.cs");
        var compilation = CSharpCompilation.Create("GenericNativeConditional", [coreTree, callerTree], [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, options: new EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(callerTree);
        var targetSyntax = callerTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var invocationSyntax = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        var method = invocation.TargetMethod;
        var candidates = compilation.GetSpecialType(SpecialType.System_IntPtr).GetMembers("Trace").OfType<IMethodSymbol>().ToArray();
        var raw = candidates.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Arity == 0));
        var generic = candidates.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Arity == 1));
        var attribute = raw.GetAttributes().Single();
        Assert.That(candidates, Has.Length.EqualTo(2));
        Assert.That(candidates.Select(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Arity)), Is.EquivalentTo(UnderlyingArities));
        Assert.That(method.Arity, Is.Zero);
        Assert.That(method.IsGenericMethod, Is.False);
        Assert.That(method.ContainingType.GetMembers("Trace").OfType<IMethodSymbol>().Select(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Arity)), Is.EqualTo(NativeArities));
        Assert.That(generic.GetAttributes().Single().ConstructorArguments[0].Value, Is.EqualTo("OTHER"));
        Assert.That(method.IsConditional, Is.True);
        Assert.That(method.ContainingType.IsNativeIntegerType, Is.True);
        Assert.That(method.OriginalDefinition, Is.SameAs(method));
        Assert.That(method.GetAttributes(), Is.Empty);
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(method.DeclaringSyntaxReferences[0].SyntaxTree, Is.SameAs(coreTree));
        Assert.That(method.DeclaringSyntaxReferences[0].Span, Is.EqualTo(raw.DeclaringSyntaxReferences[0].Span));
        Assert.That(attribute.ApplicationSyntaxReference!.SyntaxTree, Is.SameAs(coreTree));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo("ABSENT"));
        image.Position = 0;
        using var pe = new PEReader(image, PEStreamOptions.LeaveOpen);
        var reader = pe.GetMetadataReader();
        var targetDefinition = reader.MethodDefinitions.Select(reader.GetMethodDefinition)
            .Single(candidate => reader.GetString(candidate.Name) == "Target");
        var targetIl = pe.GetMethodBody(targetDefinition.RelativeVirtualAddress).GetILBytes()!;
        Assert.That(TraceCalls(image.ToArray()), Has.Length.EqualTo(enabled ? 1 : 0));
        if (!enabled)
        { Assert.That(targetIl, Is.EqualTo(OmittedTargetIl)); }
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            Bug = "SP-NATIVE-CONDITIONAL-001",
            MatcherRejectionControl = true,
            enabled,
            SourceBacked = true,
            method.IsConditional,
            SourceReferences = method.DeclaringSyntaxReferences.Length,
            WrapperAttributes = method.GetAttributes().Length,
            CandidateCount = candidates.Length,
            Attribute = attribute.ToString(),
            TargetIl = Convert.ToHexString(targetIl),
            CoreSource = core,
            CallerSource = source
        }));

        var policy = new CSharpInvocationEmissionPolicy(compilation);
        bool elided = false;
        Assert.DoesNotThrow((Action)(() => elided = policy.IsElided(invocation)));
        Assert.That(elided, Is.EqualTo(!enabled));
        var graph = ControlFlowGraph.Create(targetSyntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!);
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

    private static string[] TraceCalls(byte[] bytes)
    {
        System.ArgumentNullException.ThrowIfNull(bytes);
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
                {
                    Assert.That(handle.Kind, Is.EqualTo(HandleKind.MethodDefinition));
                    Assert.That(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetGenericParameters(), Is.Empty);
                    calls.Add(name);
                }
            }
            offset += size;
        }
        return [.. calls];
    }

    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : (field.FieldType == typeof(OpCode)))
        .Select(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : ((OpCode)field.GetValue(null)!))
        .GroupBy(opcode /* SPINT001: all values valid - every opcode value has an unsigned grouping key */ => unchecked((ushort)opcode.Value))
        .ToDictionary(group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.Key), group => group is null ? throw new System.ArgumentNullException(nameof(group)) : (group.First()));
}
