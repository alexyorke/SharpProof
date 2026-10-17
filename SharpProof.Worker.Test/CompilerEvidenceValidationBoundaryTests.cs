using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerEvidenceValidationBoundaryTests
{
    private static readonly string[] ExpectedClaimIds = ["none", "argument", "effect"];
    [Test]
    public void ModuleEvidenceRequiresAnMvidBeforeMatchingLoadedAndFileIdentities()
    {
        var compilation = TestCompilation.Create("ModuleEvidence", ("module.cs", "public static class Subject { public static int Value() => 1; }"));
        using var image = Emit(compilation);
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var evidence = CompilerReferenceModuleEvidence.Capture(reader);
        Assert.That(evidence.Mvid.IsNil, Is.False);
        Assert.That(evidence.Name, Is.EqualTo("ModuleEvidence.dll"));
        var identity = CompilerReferenceModuleEvidence.Match(evidence, evidence);
        Assert.That(identity.Name, Is.EqualTo(evidence.Name));
        Assert.That(identity.Mvid, Is.EqualTo(reader.GetGuid(reader.GetModuleDefinition().Mvid)));

        var missingMvid = evidence with { Mvid = default };
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            CompilerReferenceModuleEvidence.Match(evidence, missingMvid)))!.Message,
            Is.EqualTo("A compiler reference module has no MVID."));
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            CompilerReferenceModuleEvidence.Match(missingMvid, evidence)))!.Message,
            Is.EqualTo("A compiler reference module has no MVID."));
        var otherName = evidence with { Name = "Other.netmodule" };
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            CompilerReferenceModuleEvidence.Match(otherName, missingMvid)))!.Message,
            Is.EqualTo("A compiler reference module has no MVID."));
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            CompilerReferenceModuleEvidence.Match(missingMvid, otherName)))!.Message,
            Is.EqualTo("A compiler reference path does not match its loaded metadata."));

        var changed = TestCompilation.Create("ModuleEvidence", ("module.cs", "public static class Subject { public static int Value() => 2; }"));
        using var changedImage = Emit(changed);
        using var changedPe = new PEReader(changedImage);
        var changedEvidence = CompilerReferenceModuleEvidence.Capture(changedPe.GetMetadataReader());
        Assert.That(changedEvidence.Name, Is.EqualTo(evidence.Name));
        Assert.That(changedEvidence.Reader.GetGuid(changedEvidence.Mvid), Is.Not.EqualTo(identity.Mvid));
        Assert.Throws<InvalidDataException>(new System.Action(() => CompilerReferenceModuleEvidence.Match(evidence, changedEvidence)));
    }

    [Test]
    public void SiblingLocationEvidenceRejectsForeignDirectoriesAndMissingDirectoryFacts()
    {
        var directory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.WorkDirectory, "module-evidence"));
        var manifest = Path.Combine(directory, "Assembly.dll");
        var sibling = Path.Combine(directory, "Module.netmodule");
        Assert.That(CompilerCompilationCapture.ResolveSiblingModule(manifest, "Module.netmodule"), Is.EqualTo(sibling));
        Assert.That(new CompilerReferenceSiblingLocation("Module.netmodule", directory, sibling).Resolve(), Is.EqualTo(sibling));
        var foreign = Path.Combine(Path.GetFullPath(Path.Combine(directory, "..", "other-module-evidence")), "Module.netmodule");
        Assert.That(Path.GetDirectoryName(foreign), Is.Not.EqualTo(directory));
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            new CompilerReferenceSiblingLocation("Module.netmodule", directory, foreign).Resolve()))!.Message,
            Is.EqualTo("A linked compiler module must be a safe sibling."));
        Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
            new CompilerReferenceSiblingLocation("Module.netmodule", null, sibling).Resolve()))!.Message,
            Is.EqualTo("A compiler reference directory is unavailable."));
        foreach (var unsafeName in new[] { "", ".", "..", "../Module.netmodule", "a\0b" })
        {
            Assert.That(Assert.Throws<InvalidDataException>(new System.Action(() =>
                CompilerCompilationCapture.ResolveSiblingModule("\0", unsafeName)))!.Message,
                Is.EqualTo("A linked compiler module must be a safe sibling."));
            Assert.Throws<InvalidDataException>(new System.Action(() =>
                new CompilerReferenceSiblingLocation(unsafeName, null, foreign).Resolve()));
        }
    }

    [Test]
    public void SpecificationBindingSeparatesAuditedTypeKindsFromTotalIntegerWidths()
    {
        var compilation = TestCompilation.Create("BindingEvidence", ("binding.cs", "public static class Subject { public static int Same(int left, int right) => left; public static long Mixed(int left, long right) => right; }"));
        TestCompilation.AssertNoErrors(compilation);
        using var image = Emit(compilation);
        Assert.That(image.Length, Is.GreaterThan(0));
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        var same = subject.GetMembers("Same").OfType<IMethodSymbol>().Single();
        var mixed = subject.GetMembers("Mixed").OfType<IMethodSymbol>().Single();
        var sameDefinition = Definition(same);
        var mixedDefinition = Definition(mixed);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition), Is.SameAs(sameDefinition));
        Assert.That(CompilerSpecificationMethodBinding.Admit(mixed, mixedDefinition), Is.SameAs(mixedDefinition));
        Assert.That(CompilerSpecificationMethodBinding.CreateTotal(factory, mixed, mixedDefinition), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(mixed, mixedDefinition), Is.SameAs(mixedDefinition));

        var bound = CompilerSpecificationMethodBinding.CreateTotal(factory, same, sameDefinition)!;
        Assert.That(bound, Is.Not.Null);
        Assert.That(bound.ParameterCount, Is.EqualTo(2));
        var integer = factory.GetOrCreateIntegerType(32, true);
        var rule = bound.Apply([factory.Integer(integer, 7), factory.Integer(integer, 11)]);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>()).Value!.Integer, Is.EqualTo(7));
        Assert.That(bound.Apply([]).Classification.IsExact, Is.False);
        Assert.That(bound.Apply([factory.Boolean(true), factory.Integer(integer, 11)]).Classification.IsExact, Is.False);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition with { ParameterTypes = [IrTypeKind.Boolean, IrTypeKind.Integer] }), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition with { ParameterTypes = [IrTypeKind.Integer, IrTypeKind.Boolean] }), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, null), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition with { Assemblies = [] }), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition with { ResultType = IrTypeKind.Boolean }), Is.Null);
        Assert.That(CompilerSpecificationMethodBinding.Admit(same, sameDefinition with { ParameterTypes = [] }), Is.Null);
        Assert.That(new CompilerSpecificationPackProvider(new IrFactory(), ["dotnet.scalar"]).ResolveTotal(null!), Is.Null);
    }

    [Test]
    public void OwnedSyntaxEncodingUsesTheOriginalOrdinalPathOrTheMissingOwnerSentinel()
    {
        var compilation = TestCompilation.Create("SyntaxEvidence", ("syntax.cs", "public static class Subject { public static int First() { int value = 1; return value; } public static int Second() => 2; }"));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var declaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var local = (ILocalSymbol)model.GetDeclaredSymbol(declaration)!;
        var owner = local.ContainingSymbol.DeclaringSyntaxReferences.Single().GetSyntax();
        var unrelated = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Second"));
        using var expected = new CanonicalHashWriter();
        expected.Add(4).Add(2).Add(0).Add(0).Add(1);
        Assert.That(EncodePath(declaration, owner), Is.EqualTo(expected.Finish()));
        using var empty = new CanonicalHashWriter();
        empty.Add(0);
        Assert.That(EncodePath(owner, owner), Is.EqualTo(empty.Finish()));
        using var missing = new CanonicalHashWriter();
        missing.Add(-1);
        var sentinel = missing.Finish();
        Assert.That(EncodePath(declaration, unrelated), Is.EqualTo(sentinel));
        Assert.That(EncodePath(root, owner), Is.EqualTo(sentinel));
        Assert.That(EncodePath(null, owner), Is.EqualTo(sentinel));
        Assert.That(EncodePath(declaration, null), Is.EqualTo(sentinel));
    }

    [Test]
    public void ExceptionProjectionRequiresTheCompleteRuntimeTypeUniverse()
    {
        var compilation = TestCompilation.Create("ExceptionEvidence", ("exception.cs", "public static class Subject { }"));
        TestCompilation.AssertNoErrors(compilation);
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        var argumentIdentity = CompilerExceptionTypeIdentity.Encode(core.GetTypeByMetadataName("System.ArgumentException")!);
        var claims = new[]
        {
            new CompilerEffectClaimArtifact { ClaimId = "none", ContractKind = WorkerEffectContractKind.DoesNotThrow },
            new CompilerEffectClaimArtifact { ClaimId = "argument", ContractKind = WorkerEffectContractKind.AllowedExceptions,
                Constraint = new() { AllowedExceptionTypes = [argumentIdentity] } },
            new CompilerEffectClaimArtifact { ClaimId = "effect", ContractKind = WorkerEffectContractKind.EffectContract,
                Constraint = new() { AllowedEffects = WorkerEffectSet.Throws, AllowedExceptionTypes = [argumentIdentity] } }
        };
        var projected = CompilerExceptionConstraintEvidence.Project(core, claims);
        Assert.That(projected.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.ClaimId)), Is.EqualTo(ExpectedClaimIds));
        Assert.That(projected[0].AllowedKinds, Is.Empty);
        Assert.That(projected[1].AllowedKinds, Is.EqualTo(new[] { IrExceptionKind.Argument }));
        Assert.That(projected[2].AllowedKinds, Is.EqualTo(new[] { IrExceptionKind.Argument }));
        Assert.That(compilation.Assembly.GetTypeByMetadataName("System.DivideByZeroException"), Is.Null);
        Assert.That(CompilerExceptionConstraintEvidence.Project(compilation.Assembly, claims), Is.Empty);
        Assert.That(CompilerExceptionConstraintEvidence.Project(compilation.Assembly, []), Is.Empty);
    }

    private static CompilerSpecificationMethodEvidence Definition(IMethodSymbol method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        return new(method.GetDocumentationCommentId()!, [new ApiSpecAssemblyIdentity(method.ContainingAssembly.Identity.Name, string.Empty)],
            [IrTypeKind.Integer, IrTypeKind.Integer], IrTypeKind.Integer,
            new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Integer));
    }

    private static string EncodePath(SyntaxNode? declaration, SyntaxNode? owner)
    {
        using var writer = new CanonicalHashWriter();
        CompilerOwnedSyntaxPath.Write(writer, declaration, owner);
        return writer.Finish();
    }

    private static MemoryStream Emit(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var image = new MemoryStream();
        var result = compilation.Emit(image);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        image.Position = 0;
        return image;
    }
}
