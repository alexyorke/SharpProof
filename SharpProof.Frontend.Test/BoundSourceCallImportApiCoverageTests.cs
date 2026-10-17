using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BoundSourceCallImportApiCoverageTests
{
    [TestCase("plain", false)]
    [TestCase("assume", false)]
    [TestCase("requires", false)]
    [TestCase("requires", true)]
    public void ActualBoundCalleeRetainsImporterAndMarkerAdmission(string clauseKind, bool longPath)
    {
        System.ArgumentNullException.ThrowIfNull(clauseKind);
        var clause = clauseKind switch
        {
            "assume" => "SharpProof.Attributes.Contract.Assume(true);",
            "requires" => "SharpProof.Attributes.Contract.Requires(value >= 0);",
            _ => ""
        };
        var source = "public static class Subject { public static int Target(int value) => Helper(value); " +
            "public static int Helper(int value) { " + clause + " return value + 1; } }";
        var path = longPath ? new string('p', 4097) + ".cs" : "bound-source.cs";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), path);
        var compilation = CSharpCompilation.Create("BoundSourceCallSubject", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var load = new AssemblyLoadContext("BoundSourceCallSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = load.LoadFromStream(image);
            var runtime = assembly.GetType("Subject")!;
            Assert.That(runtime.GetMethod("Target")!.Invoke(null, [5]), Is.EqualTo(6));
            Assert.That(runtime.GetMethod("Helper")!.Invoke(null, [5]), Is.EqualTo(6));
            var model = compilation.GetSemanticModel(tree);
            var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var targetSyntax = methods.Single(m => m is null ? throw new System.ArgumentNullException(nameof(m)) : (m.Identifier.ValueText == "Target"));
            var helperSyntax = methods.Single(m => m is null ? throw new System.ArgumentNullException(nameof(m)) : (m.Identifier.ValueText == "Helper"));
            var targetGraph = ControlFlowGraph.Create(targetSyntax, model)!;
            var helperGraph = ControlFlowGraph.Create(helperSyntax, model)!;
            var invocation = (IInvocationOperation)model.GetOperation(targetSyntax.ExpressionBody!.Expression)!;
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!);
            var preparedCount = 0;
            bool Prepare(TotalLoweringContext candidate)
            {
                System.ArgumentNullException.ThrowIfNull(candidate);
                preparedCount++;
                Assert.That(candidate.Factory, Is.SameAs(factory));
                Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, invocation.TargetMethod), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target.ContainingAssembly, compilation.Assembly), Is.True);
                Assert.That(helperGraph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
                var binding = new ContractBinder(compilation, factory).BindTotal(candidate);
                Assert.That(binding.IsSuccess, Is.True);
                Assert.That(binding.Clauses, Has.Length.EqualTo(clauseKind == "plain" ? 0 : 1));
                if (clauseKind != "plain")
                {
                    Assert.That(binding.Clauses[0].Kind, Is.EqualTo(clauseKind == "assume" ? BoundContractKind.Assume : BoundContractKind.Requires));
                    Assert.That(factory.GetOperationInfo(binding.Clauses[0].SourceOperation).SourceSpan!.Document, Is.EqualTo(path));
                }
                candidate.SourceCallPreconditions = [.. binding.Clauses.Where(c => c is null ? throw new System.ArgumentNullException(nameof(c)) : (c.Kind == BoundContractKind.Requires))
                    .Select(c => c is null ? throw new System.ArgumentNullException(nameof(c)) : (new TotalSourcePrecondition(c.Value, c.SafeCondition, c.SourceOperation)))];
                var prepared = new RoslynProgramLowerer(factory).LowerCandidate(helperGraph, candidate);
                Assert.That(candidate.Compilation, Is.SameAs(compilation));
                Assert.That(prepared.IsExact, Is.True);
                Assert.That(prepared.ConstructionLimitExceeded, Is.False);
                Assert.That(prepared.Program.Blocks.SelectMany(b => b is null ? throw new System.ArgumentNullException(nameof(b)) : (b.Instructions)).OfType<IrAssumeInstruction>().Count(),
                    Is.EqualTo(clauseKind == "assume" ? 1 : 0));
                var direct = new IrProgramInterpreter(factory).Execute(prepared.Program,
                    candidate.Parameters.ToDictionary(p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (p.Entry), p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (factory.CreateIntegerValue(factory.GetVariableInfo(p.Entry).Type, 5))));
                Assert.That(direct.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(direct.ConsumedApproximation, Is.False);
                Assert.That(direct.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
                return prepared.IsExact;
            }
            var session = new TotalSourceCallSession(compilation, Prepare, null, default);
            var lowered = new RoslynTotalProgramLowerer(context, default, session).Lower(targetGraph);
            Assert.That(preparedCount, Is.EqualTo(1));
            Assert.That(session.IsActive(context.Target), Is.False);
            Assert.That(session.IsActive(invocation.TargetMethod), Is.False);
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            if (clauseKind == "assume" || longPath)
            {
                Assert.That(lowered.IsExact, Is.False);
                Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            }
            else
            {
                Assert.That(lowered.IsExact, Is.True);
                Assert.That(lowered.CallPreconditions, Has.Count.EqualTo(clauseKind == "requires" ? 1 : 0));
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    context.Parameters.ToDictionary(p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (p.Entry), p => p is null ? throw new System.ArgumentNullException(nameof(p)) : (factory.CreateIntegerValue(factory.GetVariableInfo(p.Entry).Type, 5))));
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(6)));
            }
        }
        finally
        {
            load.Unload();
        }
    }
}
