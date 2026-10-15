using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class GenericCatchSemanticAgreementTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AdmittedSourceCatchRetainsTheActualConstructedClrBehavior(bool generic)
    {
        var source = generic
            ? "int Target(int x) => Helper<System.DivideByZeroException>(x); static int Helper<T>(int x) where T : System.Exception { try { return 10 / x; } catch (T) { return 17; } }"
            : "int Target(int x) => Helper(x); static int Helper(int x) { try { return 10 / x; } catch (System.DivideByZeroException) { return 17; } }";
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([0]), Is.EqualTo(17));
        Assert.That(subject.Invoke([2]), Is.EqualTo(5));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var invocationSyntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        var helperSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Helper");
        var helperSymbol = (IMethodSymbol)model.GetDeclaredSymbol(helperSyntax)!;
        var helperGraph = ControlFlowGraph.Create(helperSyntax, model)!;
        var catchSyntax = helperSyntax.DescendantNodes().OfType<CatchClauseSyntax>().Single();
        var catchOperation = (ICatchClauseOperation)model.GetOperation(catchSyntax)!;
        var catchType = catchOperation.ExceptionType!;
        Assert.That(SymbolEqualityComparer.Default.Equals(helperSymbol, invocation.TargetMethod.OriginalDefinition), Is.True);
        Assert.That(invocation.TargetMethod.IsGenericMethod, Is.EqualTo(generic));
        Assert.That(invocation.TargetMethod.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        Assert.That(invocation.Arguments[0].ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.Arguments[0].Parameter!.ContainingSymbol,
            invocation.TargetMethod), Is.True);
        Assert.That(helperGraph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        Assert.That(catchType.TypeKind, Is.EqualTo(generic ? TypeKind.TypeParameter : TypeKind.Class));
        if (generic)
        {
            var parameter = (ITypeParameterSymbol)catchType;
            var exception = subject.Compilation.GetTypeByMetadataName("System.Exception")!;
            Assert.That(parameter.ConstraintTypes, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ConstraintTypes[0], exception), Is.True);
            TestContext.WriteLine($"Compiler SDK={typeof(Compilation).Assembly.FullName}; CSharp SDK={typeof(Microsoft.CodeAnalysis.CSharp.CSharpCompilation).Assembly.FullName}; compilation={subject.Compilation.Assembly.Identity}");
            var projectedAdmission = CSharpOperationSemantics.TryCatchKinds(subject.Compilation, parameter, out var projectedKinds);
            TestContext.WriteLine($"SDK catch parameter BaseType={parameter.BaseType?.ToDisplayString() ?? "<null>"}; BaseKind={parameter.BaseType?.TypeKind}; constraints={string.Join("|", parameter.ConstraintTypes.Select(type => type.ToDisplayString()))}; catchAdmitted={projectedAdmission}; kindCount={(projectedKinds.IsDefault ? -1 : projectedKinds.Length)}");
            Assert.That(parameter.BaseType, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.TypeArguments.Single(),
                subject.Compilation.GetTypeByMetadataName("System.DivideByZeroException")), Is.True);
        }
        var regions = new Stack<ControlFlowRegion>();
        regions.Push(helperGraph.Root);
        var catches = new List<ControlFlowRegion>();
        while (regions.TryPop(out var region))
        {
            if (region.Kind == ControlFlowRegionKind.Catch)
            { catches.Add(region); }
            foreach (var child in region.NestedRegions)
            { regions.Push(child); }
        }
        Assert.That(catches, Has.Count.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(catches[0].ExceptionType, catchType), Is.True);
        var catchAdmitted = CSharpOperationSemantics.TryCatchKinds(subject.Compilation, catchType, out var kinds);
        TestContext.WriteLine($"actual catch generic={generic}; type={catchType.TypeKind}; admitted={catchAdmitted}; kinds={(kinds.IsDefault ? -1 : kinds.Length)}");
        FrontendProgramLoweringResult? preparedHelper = null;
        var preparations = 0;
        bool Prepare(TotalLoweringContext candidate)
        {
            preparations++;
            Assert.That(candidate.Factory, Is.SameAs(subject.Factory));
            Assert.That(SymbolEqualityComparer.Default.Equals(candidate.Target, helperSymbol), Is.True);
            Assert.That(candidate.HasScalarSignature, Is.True);
            preparedHelper = new RoslynProgramLowerer(subject.Factory).LowerCandidate(helperGraph, candidate);
            Assert.That(candidate.Compilation, Is.SameAs(subject.Compilation));
            Assert.That(preparedHelper.ConstructionLimitExceeded, Is.False);
            return preparedHelper.IsExact;
        }
        var session = new TotalSourceCallSession(subject.Compilation, Prepare, null, default);
        var lowering = new RoslynTotalProgramLowerer(subject.Context, default, session).Lower(subject.Graph);
        Assert.That(preparations, Is.EqualTo(1));
        Assert.That(preparedHelper, Is.Not.Null);
        var actualHelper = preparedHelper!;
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        Assert.That(session.IsActive(subject.Context.Target), Is.False);
        Assert.That(session.IsActive(helperSymbol), Is.False);
        TestContext.WriteLine($"actual helper exact={actualHelper.IsExact}; reason={actualHelper.Classification.Abstention}; caller exact={lowering.IsExact}; reason={lowering.Classification.Abstention}");
        if (!lowering.IsExact)
        {
            // A declaration-frame catch type that cannot be modeled is rejected;
            // this negative result is never replayed as an executable program.
            Assert.That(generic, Is.True);
            Assert.That(actualHelper.IsExact, Is.False);
            Assert.That(actualHelper.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
            return;
        }
        Assert.That(actualHelper.IsExact, Is.True);
        Assert.That(actualHelper.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        foreach (var input in new[] { 2, 0 })
        {
            var expected = input == 0 ? 17 : 5;
            var replay = subject.Execute(lowering, [input]);
            TestContext.WriteLine($"actual replay x={input}; status={replay.Status}; exception={replay.Exception?.Kind}");
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}
