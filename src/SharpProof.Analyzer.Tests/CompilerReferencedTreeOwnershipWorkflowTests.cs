using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerReferencedTreeOwnershipWorkflowTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void ActualEmittedReferencesRetainTheirSyntaxTreeOwnership(bool shareTree)
    {
        const string librarySource = "public static class Helper { public static int Value(int value) => value; }";
        var suffix = shareTree ? "Shared" : "Distinct";
        var first = AnalyzerTestHost.CreateCompilation(librarySource, [], filePath: "Helper.cs")
            .WithAssemblyName("OwnedFirst_" + suffix);
        var second = AnalyzerTestHost.CreateCompilation(librarySource, [], filePath: "Helper.cs")
            .WithAssemblyName("OwnedSecond_" + suffix);
        if (shareTree)
        {
            second = second.RemoveAllSyntaxTrees().AddSyntaxTrees(first.SyntaxTrees.Single());
        }
        var firstReference = first.ToMetadataReference(aliases: ["first"]);
        var secondReference = second.ToMetadataReference(aliases: ["second"]);
        var caller = AnalyzerTestHost.CreateCompilation(
            """
            extern alias first;
            extern alias second;
            public static class Caller {
                public static int Root(int value) => first::Helper.Value(value) + second::Helper.Value(0);
            }
            """, [], additionalReferences: [firstReference, secondReference])
            .WithAssemblyName("OwnedCaller_" + suffix);
        foreach (var compilation in ImmutableArray.Create(first, second, caller))
        {
            Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        }
        Assert.That(ReferenceEquals(first.SyntaxTrees.Single(), second.SyntaxTrees.Single()), Is.EqualTo(shareTree));
        Assert.That(((IAssemblySymbol)caller.GetAssemblyOrModuleSymbol(firstReference)!).Name, Is.EqualTo(first.AssemblyName));
        Assert.That(((IAssemblySymbol)caller.GetAssemblyOrModuleSymbol(secondReference)!).Name, Is.EqualTo(second.AssemblyName));
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("OwnedReferences_" + suffix, isCollectible: true);
        try
        {
            using var firstImage = new MemoryStream((AnalyzerTestHost.EmitImage(first)).ToArray(), writable: false);
            using var secondImage = new MemoryStream((AnalyzerTestHost.EmitImage(second)).ToArray(), writable: false);
            using var callerImage = new MemoryStream((AnalyzerTestHost.EmitImage(caller)).ToArray(), writable: false);
            loadContext.LoadFromStream(firstImage);
            loadContext.LoadFromStream(secondImage);
            var runtimeCaller = loadContext.LoadFromStream(callerImage);
            Assert.That(runtimeCaller.GetType("Caller", throwOnError: true)!.GetMethod("Root")!
                .Invoke(null, [17]), Is.EqualTo(17));
        }
        finally
        {
            loadContext.Unload();
        }
        var mandatory = new ClaimManifestBuilder(caller).Build();
        var withShadow = new ClaimManifestBuilder(caller).Build(includePotentialCallShadow: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withShadow.Manifest.Callables.Select(callable => callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (callable.CallableId)),
                Is.EqualTo(mandatory.Manifest.Callables.Select(callable => callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (callable.CallableId))));
            Assert.That(withShadow.Manifest.Claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId)),
                Is.EqualTo(mandatory.Manifest.Claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId))));
        }
        var inventory = withShadow.PotentialCalls!;
        if (shareTree)
        {
            Assert.That(inventory.Owners, Is.Empty);
            Assert.That(inventory.Gaps, Has.Length.EqualTo(1));
            Assert.That(inventory.Gaps.Single().Reason, Is.EqualTo("ReferenceOwnership"));
            Assert.That(inventory.Gaps.Single().ReferenceAssemblyName, Is.EqualTo(second.AssemblyName));
        }
        else
        {
            Assert.That(inventory.Gaps, Is.Empty);
            Assert.That(inventory.Owners, Has.Length.EqualTo(1));
            Assert.That(inventory.Owners.Single().Method.Name, Is.EqualTo("Root"));
            Assert.That(inventory.Owners.Single().DiscoveryComplete, Is.True);
        }
    }
}
