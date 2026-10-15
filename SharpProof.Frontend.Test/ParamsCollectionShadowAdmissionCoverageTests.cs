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
public sealed class ParamsCollectionShadowAdmissionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void GenuineCSharp13ParamsCollectionKeepsExpandedShadowCallOutsideAdmission(bool expanded)
    {
        var source = "public static class Subject { public static int Target() => Helper(" +
            (expanded ? "1, 2" : "null") +
            "); public static int Helper(params System.Collections.Generic.IEnumerable<int> values) => 17; }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp13), "params-collection.cs");
        var compilation = CSharpCompilation.Create("ParamsCollectionShadowSubject", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var load = new AssemblyLoadContext("ParamsCollectionShadowSubject", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = load.LoadFromStream(image).GetType("Subject")!;
            Assert.That(runtime.GetMethod("Target")!.Invoke(null, []), Is.EqualTo(17));
            Assert.That(runtime.GetMethod("Helper")!.Invoke(null, [null]), Is.EqualTo(17));
            var model = compilation.GetSemanticModel(tree);
            var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
            var targetSyntax = methods.Single(m => m.Identifier.ValueText == "Target");
            var helperSyntax = methods.Single(m => m.Identifier.ValueText == "Helper");
            var invocation = (IInvocationOperation)model.GetOperation(targetSyntax.ExpressionBody!.Expression)!;
            Assert.That(invocation.Instance, Is.Null);
            Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
            Assert.That(invocation.TargetMethod.Parameters, Has.Length.EqualTo(1));
            var argument = invocation.Arguments[0];
            var parameter = invocation.TargetMethod.Parameters[0];
            Assert.That(parameter.IsParams, Is.True);
            Assert.That(parameter.RefKind, Is.EqualTo(RefKind.None));
            Assert.That(parameter.Type.TypeKind, Is.EqualTo(TypeKind.Interface));
            Assert.That(parameter.Type, Is.Not.InstanceOf<IArrayTypeSymbol>());
            Assert.That(CSharpOperationSemantics.IsValueDomain(parameter.Type), Is.True);
            Assert.That(argument.Parameter!.Ordinal, Is.Zero);
            Assert.That(SymbolEqualityComparer.Default.Equals(argument.Parameter.ContainingSymbol, invocation.TargetMethod), Is.True);
            Assert.That(argument.ArgumentKind, Is.EqualTo(expanded ? ArgumentKind.ParamCollection : ArgumentKind.Explicit));
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!);
            var policyCalls = 0;
            bool PreserveActualHelper(IMethodSymbol method)
            {
                policyCalls++;
                return SymbolEqualityComparer.Default.Equals(method, invocation.TargetMethod.OriginalDefinition);
            }
            var graph = ControlFlowGraph.Create(targetSyntax, model)!;
            var shadow = new RoslynProgramLowerer(factory).LowerShadowSourceBody(graph, context, PreserveActualHelper, default);
            Assert.That(shadow.IsShadowCallSkeleton, Is.True);
            Assert.That(shadow.IsExact, Is.False);
            Assert.That(shadow.ConstructionLimitExceeded, Is.False);
            Assert.That(policyCalls, Is.EqualTo(expanded ? 0 : 1));
            Assert.That(shadow.Classification.IsExact, Is.EqualTo(!expanded));
            Assert.That(shadow.PreservedSourceCalls, Has.Count.EqualTo(expanded ? 0 : 1));
            if (expanded)
            {
                Assert.That(shadow.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
            }
            var helperContext = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(helperSyntax)!);
            var helperGraph = ControlFlowGraph.Create(helperSyntax, model)!;
            var direct = new RoslynProgramLowerer(factory).LowerCandidate(helperGraph, helperContext);
            Assert.That(direct.IsExact, Is.True);
            Assert.That(direct.ConstructionLimitExceeded, Is.False);
            var replay = new IrProgramInterpreter(factory).Execute(direct.Program,
                helperContext.Parameters.ToDictionary(p => p.Entry, p => factory.CreateNullValue(factory.GetVariableInfo(p.Entry).Type)));
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17)));
        }
        finally
        {
            load.Unload();
        }
    }
}
