using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class LocalLifetimeRegionWorkApiCoverageTests
{
    [TestCase(8, false)]
    [TestCase(2049, true)]
    public void ActualRetainedScopesRespectDefaultRegionScanLimit(int leaves, bool exceedsLimit)
    {
        var ordinal = 0;
        var members = "int Target() {\n" + Scope(leaves) + "return 7;\n}";
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([]), Is.EqualTo(7));
        Assert.That(subject.Context.HasScalarSignature, Is.True);
        Assert.That(subject.Context.OwnsBody(subject.Graph.OriginalOperation), Is.True);
        Assert.That(subject.Graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var regions = new List<ControlFlowRegion>();
        var pending = new Stack<ControlFlowRegion>();
        pending.Push(subject.Graph.Root);
        while (pending.TryPop(out var region))
        {
            regions.Add(region);
            foreach (var child in region.NestedRegions)
            { pending.Push(child); }
        }
        var scopes = regions.Where(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (!region.Locals.IsEmpty)).ToArray();
        TestContext.WriteLine($"Region scan leaves={leaves}; actualScopes={scopes.Length}; totalRegions={regions.Count}; blocks={subject.Graph.Blocks.Length}; membersSha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(members)))}");
        Assert.That(scopes, Has.Length.EqualTo(2 * leaves - 1));
        foreach (var region in scopes)
        {
            Assert.That(region.Kind, Is.EqualTo(ControlFlowRegionKind.LocalLifetime));
            Assert.That(region.Locals, Has.Length.EqualTo(1));
            var local = region.Locals.Single();
            Assert.That(local.Name, Does.StartWith("unused"));
            Assert.That(local.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(local.HasConstantValue, Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, subject.Context.Target), Is.True);
        }
        Assert.That(regions.All(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (region.Kind is ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime)), Is.True);
        Assert.That(subject.Graph.Blocks.Length, Is.LessThanOrEqualTo(RoslynTotalProgramLowerer.MaximumRegionSteps));
        Assert.That(regions.Count > RoslynTotalProgramLowerer.MaximumRegionSteps, Is.EqualTo(exceedsLimit));
        var lowered = subject.Lower();
        TestContext.WriteLine($"Region scan exact={lowered.IsExact}; abstention={lowered.Classification.Abstention}; limited={lowered.ConstructionLimitExceeded}");
        Assert.That(lowered.IsExact, Is.EqualTo(!exceedsLimit));
        Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(exceedsLimit));
        Assert.That(lowered.Classification.Abstention,
            Is.EqualTo(exceedsLimit ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.None));
        if (!exceedsLimit)
        {
            var replay = subject.Execute(lowered, []);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(7)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }

        string Scope(int count)
        {
            var name = "unused" + (ordinal++).ToString(CultureInfo.InvariantCulture);
            if (count == 1)
            { return "{ int " + name + " = 0; }\n"; }
            var left = count / 2;
            return "{ int " + name + ";\n" + Scope(left) + Scope(count - left) + "}\n";
        }
    }
}
