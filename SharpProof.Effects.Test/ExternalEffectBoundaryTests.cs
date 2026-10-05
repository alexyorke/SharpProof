using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ExternalEffectBoundaryTests
{
    private static readonly string[] s_exceptionNames = ["System.InvalidOperationException"];

    [Test]
    public void ReviewedContractPreservesRegionsCapabilitiesAndExceptions()
    {
        var (resolver, method) = Boundary("[EffectContract(SharpProofEffect.ReadsReceiverState | " +
            "SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsAmbientState | " +
            "SharpProofEffect.WritesReceiverState | SharpProofEffect.WritesArgumentState | " +
            "SharpProofEffect.WritesAmbientState | SharpProofEffect.Allocates | SharpProofEffect.Throws | " +
            "SharpProofEffect.Synchronizes | SharpProofEffect.UsesNondeterminism | " +
            "SharpProofEffect.UsesNativeCode | SharpProofEffect.UsesReflection, Complete = true, " +
            "PreconditionFree = true, IsDeterministic = false, Capabilities = SharpProofCapability.IO, " +
            "ThrownExceptions = new[] { typeof(InvalidOperationException) })] public void M(object x, object y) { }");
        var resolution = resolver.ResolveContract(method);
        var summary = resolver.Resolve(method);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Kind, Is.EqualTo(EffectContractResolutionKind.Valid));
            Assert.That(summary, Is.EqualTo(resolution.Summary));
            Assert.That(summary.Reads.Regions, Is.EquivalentTo(new[] { EffectRegionId.Receiver,
                EffectRegionId.Parameter(0), EffectRegionId.Parameter(1), EffectRegionId.Ambient }));
            Assert.That(summary.Writes, Is.EqualTo(summary.Reads));
            Assert.That(summary.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(summary.Capabilities.Kinds, Is.EqualTo(EffectCapabilityKind.IO |
                EffectCapabilityKind.Synchronization | EffectCapabilityKind.Randomness |
                EffectCapabilityKind.NativeInterop | EffectCapabilityKind.Reflection));
            Assert.That(summary.Throws.Types.Select(type => type.ToDisplayString()),
                Is.EqualTo(s_exceptionNames));
            Assert.That(summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [TestCase("[EffectContract((SharpProofEffect)(1L << 60), Complete = true)]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.None, Capabilities = (SharpProofCapability)(1 << 29))]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.Throws)]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.None, ThrownExceptions = new[] { typeof(Exception) })]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.Throws, ThrownExceptions = new[] { typeof(string) })]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.Throws, ThrownExceptions = null)]", false, true)]
    [TestCase("[EffectContract(SharpProofEffect.ReadsReceiverState)]", true, true)]
    [TestCase("[EffectContract(SharpProofEffect.WritesArgumentState)]", false, false)]
    [TestCase("[EffectContract(SharpProofEffect.None)][EffectContract(SharpProofEffect.Allocates)]", false, true)]
    public void InconsistentDeclarationsCannotSupplyACompleteSummary(string attributes, bool isStatic, bool hasArgument)
    {
        var (resolver, method) = Boundary(attributes + " public " + (isStatic ? "static " : "") +
            "void M(" + (hasArgument ? "object x" : "") + ") { }");
        var resolution = resolver.ResolveContract(method);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Kind, Is.EqualTo(EffectContractResolutionKind.Invalid));
            Assert.That(resolution.InvalidAttributes, Is.Not.Empty);
            Assert.That(resolver.Resolve(method).Uncertainty, Is.EqualTo(EffectUncertainty.InvalidContract));
            Assert.That(resolution.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        }
    }

    [TestCase("", "Missing")]
    [TestCase("[EffectContract(SharpProofEffect.None)]", "Incomplete")]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)]" +
        "[EffectContract(SharpProofEffect.None, Complete = true)]", "Valid")]
    public void AbsencePartialityAndMatchingDuplicatesRemainDistinct(string attributes, string expected)
    {
        var (resolver, method) = Boundary(attributes + " public void M() { }");
        Assert.That(resolver.ResolveContract(method).Kind.ToString(), Is.EqualTo(expected));
        if (expected == "Incomplete")
        {
            Assert.That(resolver.Resolve(method), Is.EqualTo(EffectSummary.Top));
        }
    }

    [TestCase("")]
    [TestCase("[SharpProofTrusted(\" \")]")]
    public void UnreviewedContractsCannotOverrideAnUnknownBoundary(string trust)
    {
        var (resolver, method) = Boundary("[EffectContract(SharpProofEffect.None, Complete = true)] public void M() { }", trust);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver.ResolveContract(method).Kind, Is.EqualTo(EffectContractResolutionKind.Untrusted));
            Assert.That(resolver.Resolve(method).Uncertainty, Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(resolver.Resolve(method).Throws.IncludesUnknown, Is.True);
        }
    }

    [Test]
    public void PropertyDeclarationAppliesToItsAccessor()
    {
        var (resolver, getter) = Boundary("[EffectContract(SharpProofEffect.ReadsReceiverState, Complete = true)] " +
            "public int M { get { return 1; } }", property: true);
        Assert.That(resolver.ResolveContract(getter).Kind, Is.EqualTo(EffectContractResolutionKind.Valid));
        Assert.That(resolver.Resolve(getter).Reads.Regions, Is.EqualTo(new[] { EffectRegionId.Receiver }));
    }

    [TestCase("Missing.Exception")]
    [TestCase("System.String")]
    public void UnresolvedOrNonExceptionTypesDoNotNarrowTheThrowSet(string name)
    {
        var (resolver, _) = Boundary("public void M() { }");
        Assert.That(resolver.ResolveExceptionSet(["System.Exception", name]).IncludesUnknown, Is.True);
    }

    [Test]
    public void ResolvedSpecPreservesEveryEffectFacet()
    {
        var template = ApiSpecTable.Default.Templates[0];
        var facets = template.Facets with
        {
            Effects = template.Facets.Effects with
            {
                Effects = SpecEffect.ReadsReceiverState |
                SpecEffect.ReadsArgumentState | SpecEffect.ReadsAmbientState | SpecEffect.WritesReceiverState |
                SpecEffect.WritesArgumentState | SpecEffect.WritesAmbientState | SpecEffect.InputOutput |
                SpecEffect.Synchronization | SpecEffect.NativeCode | SpecEffect.Reflection | SpecEffect.Nondeterminism
            },
            Allocation = template.Facets.Allocation with { Behavior = SpecAllocationBehavior.MayAllocate },
            Throws = template.Facets.Throws with
            {
                Behavior = SpecThrowBehavior.MayThrow,
                ExceptionMetadataNames = ["System.InvalidOperationException"]
            },
            Termination = new(SpecTerminationBehavior.Terminates, template.Facets.Effects.Evidence)
        };
        var summary = ResolveFacets(facets);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Reads.Regions, Is.EquivalentTo(new[] { EffectRegionId.Receiver,
                EffectRegionId.Parameter(0), EffectRegionId.Ambient }));
            Assert.That(summary.Writes, Is.EqualTo(summary.Reads));
            Assert.That(summary.Capabilities.Kinds, Is.EqualTo(EffectCapabilityKind.IO |
                EffectCapabilityKind.Synchronization | EffectCapabilityKind.NativeInterop |
                EffectCapabilityKind.Reflection | EffectCapabilityKind.Randomness));
            Assert.That(summary.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(summary.Throws.Types.Select(type => type.ToDisplayString()), Is.EqualTo(s_exceptionNames));
            Assert.That(summary.Termination, Is.EqualTo(EffectTermination.Terminates));
            Assert.That(summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [TestCase(SpecThrowBehavior.Unknown, "")]
    [TestCase(SpecThrowBehavior.MayThrow, "Missing.Exception")]
    [TestCase(SpecThrowBehavior.MayThrow, "")]
    public void UnresolvedSpecFacetsCannotPromiseCompleteEffects(SpecThrowBehavior throws, string exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var template = ApiSpecTable.Default.Templates[0];
        var summary = ResolveFacets(template.Facets with
        {
            Effects = template.Facets.Effects with { Effects = SpecEffect.Unknown },
            Allocation = template.Facets.Allocation with { Behavior = SpecAllocationBehavior.Unknown },
            Throws = template.Facets.Throws with
            {
                Behavior = throws,
                ExceptionMetadataNames = exception.Length == 0 ? [] : [exception]
            },
            Termination = null
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Reads.IsUnknown, Is.True);
            Assert.That(summary.Writes.IsUnknown, Is.True);
            Assert.That(summary.Capabilities.IsUnknown, Is.True);
            Assert.That(summary.Throws.IncludesUnknown, Is.True);
            Assert.That(summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        }
    }

    [Test]
    public void AnUnspecifiedMethodDoesNotBorrowAFrameworkSpec()
    {
        var compilation = TestCompilation.Create("UnspecifiedSpec", "public class C { public void M() { } }");
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        var resolver = new ExternalEffectResolver(compilation, ApiSpecTable.Default);
        Assert.That(resolver.ApiSpecs.TryGet(method, out _), Is.False);
        Assert.That(resolver.Resolve(method).Uncertainty, Is.EqualTo(EffectUncertainty.UnmodeledCall));
    }

    [Test]
    public void UnknownThrowsCannotCarryAnApparentlyExactExceptionList()
    {
        var template = ApiSpecTable.Default.Templates[0];
        var facets = template.Facets with
        {
            Throws = template.Facets.Throws with
            {
                Behavior = SpecThrowBehavior.Unknown,
                ExceptionMetadataNames = ["System.Exception"]
            }
        };
        Assert.Throws<ArgumentException>(new Action(() => ResolveFacets(facets)));
    }

    private static EffectSummary ResolveFacets(ApiSpecFacets facets)
    {
        // This exercises translation of an already bound table, not reference-family approval.
        var compilation = TestCompilation.Create("BoundSpec", "public class C { public string M(object value) { return \"\"; } }");
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        var target = ApiSpecTable.Default.Templates[0].Target with
        {
            WitnessIdentifier = "SharpProof.Test.Boundary",
            DocumentationCommentId = "M:C.M(System.Object)",
            ContainingTypeMetadataName = "C",
            MemberKind = SpecTargetMemberKind.Method,
            MemberName = "M",
            IsStatic = false,
            ReceiverType = SharpProof.Ir.IrTypeKind.Reference,
            ParameterTypes = [SharpProof.Ir.IrTypeKind.Reference],
            ResultType = SharpProof.Ir.IrTypeKind.String
        };
        facets = facets with
        {
            Nullness = facets.Nullness with { Result = SpecNullness.Unknown },
            Cardinality = facets.Cardinality with { Result = SpecCardinality.Unknown, ExactCount = null }
        };
        var template = ApiSpecTable.Create([new ApiSpecDeclaration(target, facets, [])]).Templates[0];
        var specs = ImmutableDictionary.Create<ISymbol, ResolvedApiSpec>(SymbolEqualityComparer.Default)
            .Add(method, new(template, method));
        return new ExternalEffectResolver(compilation, new ResolvedApiSpecTable(specs, [])).Resolve(method);
    }

    private static (ExternalEffectResolver Resolver, IMethodSymbol Method) Boundary(string member,
        string trust = "[SharpProofTrusted(\"reviewed boundary\")]", bool property = false)
    {
        var compilation = TestCompilation.Create("EffectBoundary", "using System; using SharpProof.Attributes; " +
            trust + " public class C { " + member + " }");
        var symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").Single();
        var method = property ? ((IPropertySymbol)symbol).GetMethod! : (IMethodSymbol)symbol;
        var emptySpecs = new ResolvedApiSpecTable(ImmutableDictionary<ISymbol, ResolvedApiSpec>.Empty, []);
        return (new ExternalEffectResolver(compilation, emptySpecs), method);
    }
}
