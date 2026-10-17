using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalInvalidReturnCoverageTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);
    private static readonly string[] VoidReturnErrors = ["CS0029"];
    private static readonly string[] NonVoidConditionalErrors = ["CS0578"];

    private const string VoidReturnSource = """
        public static class Subject
        {
            [System.Diagnostics.Conditional("ABSENT")]
            private static void Trace() { }
            public static int Target() { return Trace(); }
        }
        """;

    private const string NonVoidConditionalSource = """
        public static class Subject
        {
            [System.Diagnostics.Conditional("ABSENT")]
            private static int Trace() { return 0; }
            public static int Target() { return Trace(); }
        }
        """;

    private const string OmittedStatementSource = """
        public static class Subject
        {
            [System.Diagnostics.Conditional("ABSENT")]
            private static void Trace() { throw new System.InvalidOperationException(); }
            public static int Target() { Trace(); return 7; }
        }
        """;

    [Test]
    public void VoidConditionalReturnRefusesControlFlow()
    {
        AssertRejectedReturn(VoidReturnSource, VoidReturnErrors, failedConversion: true);
    }

    [Test]
    public void NonVoidConditionalReturnRefusesControlFlow()
    {
        AssertRejectedReturn(NonVoidConditionalSource, NonVoidConditionalErrors, failedConversion: false);
    }

    [Test]
    public void OmittedStatementEmitsAndReplaysSeven()
    {
        var compilation = CreateCompilation(OmittedStatementSource);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var (graph, context, factory, invocation) = CreateCandidate(compilation);
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(emission.IsElided(invocation), Is.True);
        var branch = graph.Blocks.Single(block => block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return);
        Assert.That(branch.BranchValue, Is.InstanceOf<ILiteralOperation>());
        Assert.That(emission.IsElided(branch.BranchValue!), Is.False);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var execution = new IrProgramInterpreter(factory).Execute(lowering.Program, new Dictionary<IrVarId, IrValue>());
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
        var runtime = new AssemblyLoadContext("ConditionalInvalidReturnControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, null), Is.EqualTo(7));
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static void AssertRejectedReturn(string source, string[] expectedErrors, bool failedConversion)
    {
        var compilation = CreateCompilation(source);
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors.Select(diagnostic => diagnostic.Id), Is.EqualTo(expectedErrors));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.False);
        Assert.That(emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id), Is.EqualTo(expectedErrors));
        var (graph, context, factory, invocation) = CreateCandidate(compilation);
        var branch = graph.Blocks.Single(block => block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return);
        Assert.That(branch.IsReachable, Is.True);
        Assert.That(branch.ConditionKind, Is.EqualTo(ControlFlowConditionKind.None));
        Assert.That(branch.Operations, Is.Empty);
        Assert.That(branch.FallThroughSuccessor!.Destination, Is.Not.Null);
        Assert.That(branch.FallThroughSuccessor.FinallyRegions, Is.Empty);
        var branchValue = branch.BranchValue!;
        IInvocationOperation cfgInvocation;
        if (failedConversion)
        {
            Assert.That(branchValue, Is.InstanceOf<IConversionOperation>());
            var conversion = (IConversionOperation)branchValue;
            Assert.That(conversion.IsImplicit, Is.True);
            Assert.That(conversion.Conversion.Exists, Is.False);
            Assert.That(conversion.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(conversion.Operand, Is.InstanceOf<IInvocationOperation>());
            cfgInvocation = (IInvocationOperation)conversion.Operand;
            Assert.That(cfgInvocation.Type!.SpecialType, Is.EqualTo(SpecialType.System_Void));
        }
        else
        {
            Assert.That(branchValue, Is.InstanceOf<IInvocationOperation>());
            cfgInvocation = (IInvocationOperation)branchValue;
            Assert.That(cfgInvocation.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        }
        Assert.That(cfgInvocation.IsImplicit, Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(cfgInvocation.TargetMethod, invocation.TargetMethod), Is.True);
        Assert.That(branchValue.Syntax.SyntaxTree, Is.SameAs(invocation.Syntax.SyntaxTree));
        Assert.That(branchValue.Syntax.Span, Is.EqualTo(invocation.Syntax.Span));
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        Assert.That(emission.IsElided(invocation), Is.True);
        Assert.That(emission.OwningInvocation(branchValue), Is.Not.Null);
        Assert.That(emission.IsElided(branchValue), Is.True);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "conditional-invalid-return.cs");
        return CSharpCompilation.Create("ConditionalInvalidReturn", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
    }

    private static (ControlFlowGraph Graph, TotalLoweringContext Context, IrFactory Factory, IInvocationOperation Invocation)
        CreateCandidate(CSharpCompilation compilation)
    {
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var target = model.GetDeclaredSymbol(syntax)!;
        var invocationSyntax = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.That(model.GetOperation(invocationSyntax), Is.InstanceOf<IInvocationOperation>());
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Trace"));
        Assert.That(invocation.TargetMethod.IsConditional, Is.True);
        Assert.That(invocation.IsImplicit, Is.False);
        Assert.That(CSharpPreprocessorSymbols.GetDefined(tree, TestContext.CurrentContext.CancellationToken), Does.Not.Contain("ABSENT"));
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(context.HasScalarSignature, Is.True);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        return (graph, context, factory, invocation);
    }
}
