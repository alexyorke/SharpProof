using System.Numerics;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NestedMetadataParameterRequiresApiTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualNestedParameterClaimStaysOutsideTheMetadataSourceBoundary(bool innerHasClaim)
    {
        var innerParameter = innerHasClaim ? "[Positive] int value" : "int value";
        var library = "using SharpProof.Attributes; public static class Library { " +
            "public static int Target([Positive] int value) => Inner(value); " +
            "public static int Inner(" + innerParameter + ") => value + 1; }";
        const string source = "public static class Subject { public static int Target(int value) => Library.Target(value); }";
        using var subject = new MetadataTestSubject(library, source);
        var owner = subject.Compilation.GetTypeByMetadataName("Library")!;
        var target = owner.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var inner = owner.GetMembers("Inner").OfType<IMethodSymbol>().Single();
        Assert.That(target.IsStatic && inner.IsStatic, Is.True);
        Assert.That(target.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(inner.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingModule, inner.ContainingModule), Is.True);
        Assert.That(target.Parameters.Single().GetAttributes(), Has.Length.EqualTo(1));
        Assert.That(target.Parameters.Single().GetAttributes().Single().AttributeClass!.ToDisplayString(),
            Is.EqualTo("SharpProof.Attributes.PositiveAttribute"));
        Assert.That(inner.Parameters.Single().GetAttributes(), Has.Length.EqualTo(innerHasClaim ? 1 : 0));
        if (innerHasClaim)
        {
            Assert.That(inner.Parameters.Single().GetAttributes().Single().AttributeClass!.ToDisplayString(),
                Is.EqualTo("SharpProof.Attributes.PositiveAttribute"));
        }
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        var outerBody = provider.Resolve(target, TestContext.CurrentContext.CancellationToken);
        var innerBody = provider.Resolve(inner, TestContext.CurrentContext.CancellationToken);
        Assert.That(outerBody, Is.Not.Null);
        Assert.That(innerBody, Is.Not.Null);
        Assert.That(outerBody!.ParameterAttributes, Has.Length.EqualTo(1));
        Assert.That(outerBody.ParameterAttributes.Single().Kind, Is.EqualTo("Positive"));
        Assert.That(outerBody.ParameterAttributes.Single().ParameterOrdinal, Is.EqualTo(0));
        Assert.That(innerBody!.ParameterAttributes, Has.Length.EqualTo(innerHasClaim ? 1 : 0));
        if (innerHasClaim)
        {
            Assert.That(innerBody.ParameterAttributes.Single().Kind, Is.EqualTo("Positive"));
            Assert.That(innerBody.ParameterAttributes.Single().ParameterOrdinal, Is.EqualTo(0));
        }
        Assert.That(outerBody.ImageSha256, Has.Length.EqualTo(64));
        Assert.That(outerBody.ModuleMvid, Is.Not.Empty);
        Assert.That(outerBody.ImageSha256, Is.EqualTo(innerBody.ImageSha256));
        Assert.That(outerBody.ModuleMvid, Is.EqualTo(innerBody.ModuleMvid));
        var nestedCall = outerBody.Instructions.Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Code == "Call"));
        Assert.That(nestedCall.Method, Is.Not.Null);
        var dependency = nestedCall.Method!;
        var allMembers = subject.Compilation.WithOptions(subject.Compilation.Options.WithMetadataImportOptions(Microsoft.CodeAnalysis.MetadataImportOptions.All));
        var allMembersInner = allMembers.GetTypeByMetadataName("Library")!.GetMembers("Inner").OfType<IMethodSymbol>().Single();
        TestContext.Out.WriteLine($"root inner: token={inner.MetadataToken}; doc={inner.GetDocumentationCommentId()}; assembly={inner.ContainingAssembly.Identity}; module={inner.ContainingModule.Name}");
        TestContext.Out.WriteLine($"decoded inner: token={dependency.MetadataToken}; doc={dependency.GetDocumentationCommentId()}; assembly={dependency.ContainingAssembly.Identity}; module={dependency.ContainingModule.Name}");
        TestContext.Out.WriteLine($"all-members inner: token={allMembersInner.MetadataToken}; doc={allMembersInner.GetDocumentationCommentId()}; assembly={allMembersInner.ContainingAssembly.Identity}; module={allMembersInner.ContainingModule.Name}; rootEquality={SymbolEqualityComparer.Default.Equals(dependency, inner)}; allMembersEquality={SymbolEqualityComparer.Default.Equals(dependency, allMembersInner)}");
        var dependencyBody = provider.Resolve(dependency, TestContext.CurrentContext.CancellationToken);
        TestContext.Out.WriteLine($"resolved dependency: present={dependencyBody != null}; rootMvid={innerBody.ModuleMvid}; dependencyMvid={dependencyBody?.ModuleMvid}; rootImage={innerBody.ImageSha256}; dependencyImage={dependencyBody?.ImageSha256}; bodyMethodEquality={SymbolEqualityComparer.Default.Equals(dependencyBody?.Method, dependency)}");
        // The provider resolves dependencies in its all-members compilation, whose symbols are distinct.
        Assert.That(SymbolEqualityComparer.Default.Equals(dependency, inner), Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(dependency, allMembersInner), Is.True);
        Assert.That(dependency.MetadataToken, Is.EqualTo(inner.MetadataToken));
        Assert.That(dependency.GetDocumentationCommentId(), Is.EqualTo(inner.GetDocumentationCommentId()));
        Assert.That(dependency.ContainingAssembly.Identity, Is.EqualTo(inner.ContainingAssembly.Identity));
        Assert.That(dependency.ContainingModule.Name, Is.EqualTo(inner.ContainingModule.Name));
        Assert.That(dependencyBody, Is.Not.Null);
        Assert.That(dependencyBody!.ModuleMvid, Is.EqualTo(innerBody.ModuleMvid));
        Assert.That(dependencyBody.ImageSha256, Is.EqualTo(innerBody.ImageSha256));
        Assert.That(subject.Method.Invoke(null, [17]), Is.EqualTo(18));
        Assert.That(subject.RootMethod.Invoke(null, [17]), Is.EqualTo(18));
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, TestContext.CurrentContext.CancellationToken), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
        if (innerHasClaim)
        {
            Assert.That(batch.Callers, Is.Empty);
            Assert.That(batch.Gaps, Has.Length.EqualTo(1));
            Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
            return;
        }
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Callers, Has.Length.EqualTo(1));
        var body = batch.Callers.Single().Body;
        Assert.That(body.Clauses, Is.Empty);
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
        var factory = body.Program.Factory;
        var entry = body.Parameters.Single().Entry;
        var replay = new IrProgramInterpreter(factory).Execute(body.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 17)));
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(18)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
