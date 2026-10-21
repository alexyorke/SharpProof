using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendRegionsRecoveryTests
{
    [TestCase("throw;", "CS0156", ControlFlowBranchSemantics.Rethrow)]
    [TestCase("if (x == 0) throw; return x;", "CS0156", ControlFlowBranchSemantics.Rethrow)]
    public void CompilerRecoveryTerminalOutsideCatchIsRejected(string body, string diagnosticId,
        ControlFlowBranchSemantics semantics)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(diagnosticId);
        var source = FrontendTestHelpers.WrapSubjectMembers(
            "public static int Target(int x) { try { " + body + " } finally { x++; } }");
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "region-recovery.cs");
        var compilation = CSharpCompilation.Create("RegionRecovery", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)), Does.Contain(diagnosticId));
        using var stream = new MemoryStream();
        Assert.That(compilation.Emit(stream).Success, Is.False);
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration is null ? throw new System.ArgumentNullException(nameof(declaration)) : (declaration.Identifier.ValueText == "Target"));
        var graph = ControlFlowGraph.Create(method, model)!;
        var terminal = graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.IsReachable &&
            (block.FallThroughSuccessor?.Semantics == semantics || block.ConditionalSuccessor?.Semantics == semantics)));
        for (var region = terminal.EnclosingRegion; region != null; region = region.EnclosingRegion)
        {
            Assert.That(region.Kind, Is.Not.EqualTo(ControlFlowRegionKind.Catch));
        }
        Assert.That(terminal.EnclosingRegion.Kind, Is.EqualTo(ControlFlowRegionKind.Try));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(method)!);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void LexicallyOwnedConditionalRethrowPreservesOriginalExceptionAndFinally(int input)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) {
                try {
                    try { return 10 / (x - x); }
                    catch (System.DivideByZeroException) {
                        if (x == 0) throw;
                        return x;
                    }
                }
                finally { x++; }
            }
            """);
        Assert.That(subject.Invoke([input]), input == 0 ? Is.TypeOf<DivideByZeroException>() : Is.EqualTo(1));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, [input]);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(input + 1)));
        Assert.That(replay.Status, Is.EqualTo(input == 0 ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        if (input == 0)
        {
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        }
        else
        {
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.One));
        }
    }
}
