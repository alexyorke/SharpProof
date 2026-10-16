using System.Numerics;
using System.Reflection.Metadata;
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
public sealed class SourceNativeIntegerConditionalEmissionApiTests
{
    private static readonly string[] EnabledSymbols = ["ABSENT"];
    private static readonly byte[] OmittedTargetIl = [0x1d, 0x2a];

    // SP-NATIVE-CONDITIONAL-001 nearby controls, added in the green phase.
    // These source-backed cases were NOT run on the red baseline. The original
    // six-case metadata/CLR fixture and its expectations remain unchanged.
    // As in Roslyn's minimal-core-library fixtures, these are real C# compilations
    // with successful Emit. Their custom core-library images are never CLR-loaded.
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
                TRACE_MEMBERS
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

    private const string SimpleMember = """
        [Diagnostics.Conditional("ABSENT")]
        public void Trace() { }
        """;

    private const string OverloadedMembers = """
        [Diagnostics.Conditional("ABSENT")]
        public void Trace(IntPtr value) { }
        [Diagnostics.Conditional("OTHER")]
        public void Trace(Int32 value) { }
        [Diagnostics.Conditional("OTHER")]
        public void Trace(UIntPtr value) { }
        """;

    [TestCase(false, false, TestName = "SP_NATIVE_CONDITIONAL_001_SourceNativeAbsent")]
    [TestCase(false, true, TestName = "SP_NATIVE_CONDITIONAL_001_SourceNativeEnabled")]
    [TestCase(true, false, TestName = "SP_NATIVE_CONDITIONAL_001_SourceNativeOverloadAbsent")]
    [TestCase(true, true, TestName = "SP_NATIVE_CONDITIONAL_001_SourceNativeOverloadEnabled")]
    public void SourceNativeMethodsDecodeTheActualDeclaration(bool overloaded, bool enabled)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: enabled ? EnabledSymbols : []);
        var core = CoreSource.Replace("TRACE_MEMBERS", overloaded ? OverloadedMembers : SimpleMember, StringComparison.Ordinal);
        var call = overloaded
            ? "((nint)default(System.IntPtr)).Trace((nint)default(System.IntPtr));"
            : "((nint)default(System.IntPtr)).Trace();";
        var source = "public static class Subject { public static int Target() { " + call + " return 7; } }";
        var coreTree = CSharpSyntaxTree.ParseText(core, options, "source-native-core.cs");
        var callerTree = CSharpSyntaxTree.ParseText(source, options, "source-native-caller.cs");
        var compilation = CSharpCompilation.Create("SourceNativeConditional", [coreTree, callerTree], [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, options: new EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(callerTree);
        var targetSyntax = callerTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var invocationSyntax = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        var method = invocation.TargetMethod;
        var candidates = compilation.GetSpecialType(SpecialType.System_IntPtr).GetMembers("Trace").OfType<IMethodSymbol>().ToArray();
        var raw = overloaded
            ? candidates.Single(candidate => candidate.Parameters[0].Type.SpecialType == SpecialType.System_IntPtr)
            : candidates.Single();
        var attribute = raw.GetAttributes().Single();
        Assert.That(candidates, Has.Length.EqualTo(overloaded ? 3 : 1));
        Assert.That(method.IsConditional, Is.True);
        Assert.That(method.ContainingType.IsNativeIntegerType, Is.True);
        Assert.That(method.OriginalDefinition, Is.SameAs(method));
        Assert.That(method.GetAttributes(), Is.Empty);
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(method.DeclaringSyntaxReferences[0].SyntaxTree, Is.SameAs(coreTree));
        Assert.That(method.DeclaringSyntaxReferences[0].Span, Is.EqualTo(raw.DeclaringSyntaxReferences[0].Span));
        Assert.That(attribute.ApplicationSyntaxReference!.SyntaxTree, Is.SameAs(coreTree));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo("ABSENT"));
        if (overloaded)
        {
            Assert.That(((INamedTypeSymbol)method.Parameters[0].Type).IsNativeIntegerType, Is.True);
            Assert.That(((INamedTypeSymbol)raw.Parameters[0].Type).IsNativeIntegerType, Is.False);
            Assert.That(compilation.ClassifyConversion(method.Parameters[0].Type, raw.Parameters[0].Type).IsIdentity, Is.True);
            Assert.That(compilation.ClassifyConversion(raw.Parameters[0].Type, method.Parameters[0].Type).IsIdentity, Is.True);
        }

        image.Position = 0;
        using var pe = new PEReader(image, PEStreamOptions.LeaveOpen);
        var reader = pe.GetMetadataReader();
        var targetDefinition = reader.MethodDefinitions.Select(reader.GetMethodDefinition)
            .Single(candidate => reader.GetString(candidate.Name) == "Target");
        var targetIl = pe.GetMethodBody(targetDefinition.RelativeVirtualAddress).GetILBytes()!;
        if (!enabled)
        { Assert.That(targetIl, Is.EqualTo(OmittedTargetIl)); }
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            Bug = "SP-NATIVE-CONDITIONAL-001",
            BaselineUnrun = true,
            overloaded,
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
}
