using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectBoundaryCoverageTests
{
    [Test]
    public void RegionSetValueSemanticsAndUnknownAbsorption()
    {
        var first = EffectRegionSet.Create(EffectRegionId.Parameter(1));
        var second = EffectRegionSet.Create(EffectRegionId.Parameter(2));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            EffectRegionSet.Create(default(ImmutableArray<EffectRegionId>))));
        Assert.That(EffectRegionSet.Create([EffectRegionId.Parameter(2), EffectRegionId.Parameter(1), EffectRegionId.Parameter(2)]).Regions,
            Is.EqualTo(ImmutableArray.Create(EffectRegionId.Parameter(1), EffectRegionId.Parameter(2))));
        var union = first.Union(second);
        Assert.That(union.Regions, Is.EqualTo(ImmutableArray.Create(EffectRegionId.Parameter(1), EffectRegionId.Parameter(2))));
        Assert.That(first.Union(EffectRegionSet.Empty), Is.EqualTo(first));
        Assert.That(EffectRegionSet.Empty.Union(first), Is.EqualTo(first));
        Assert.That(EffectRegionSet.Unknown.Union(first), Is.EqualTo(EffectRegionSet.Unknown));
        Assert.That(EffectRegionSet.Unknown.IsSubsetOf(first), Is.False);
        Assert.That(EffectRegionSet.Create((IEnumerable<EffectRegionId>)ImmutableArray.Create(EffectRegionId.Unknown, EffectRegionId.Parameter(1))), Is.EqualTo(EffectRegionSet.Unknown));
        Assert.That(EffectRegionSet.Create(ImmutableArray<EffectRegionId>.Empty).IsEmpty, Is.True);
        Assert.That(EffectRegionSet.Create(ImmutableArray.Create(EffectRegionId.Parameter(1))), Is.EqualTo(first));
        Assert.That(EffectRegionSet.Create(new[] { EffectRegionId.Parameter(1) }), Is.EqualTo(first));
        Assert.That(((object)first).Equals(first), Is.True);
        Assert.That(((object)first).Equals(second), Is.False);
        Assert.That(first == EffectRegionSet.Create(EffectRegionId.Parameter(1)), Is.True);
        Assert.That(first != second, Is.True);
        Assert.That(first.GetHashCode(), Is.EqualTo(EffectRegionSet.Create(EffectRegionId.Parameter(1)).GetHashCode()));
    }

    [Test]
    public void ExceptionSetObjectEqualityAndCapabilitySubsetRespectMembers()
    {
        var compilation = TestCompilation.Create("ValueBoundaries", "public class C { }");
        var type = compilation.GetTypeByMetadataName("System.Exception")!;
        var set = EffectThrowSet.Create([type]);
        Assert.That(((object)set).Equals(set), Is.True);
        Assert.That(((object)set).Equals(EffectThrowSet.Empty), Is.False);
        var io = new EffectCapabilitySet(EffectCapabilityKind.IO);
        Assert.That(io.Contains(EffectCapabilityKind.IO), Is.True);
        Assert.That(io.IsSubsetOf(EffectCapabilitySet.Unknown), Is.True);
        Assert.That(io.IsSubsetOf(EffectCapabilitySet.Empty), Is.False);
    }

    [Test]
    public void SymbolOrderingHandlesNullEqualAndDistinctSourceSymbols()
    {
        var first = TestCompilation.CreateExact("Ordering", [("a.cs", "public class C { }")]).GetTypeByMetadataName("C")!;
        var second = TestCompilation.CreateExact("Ordering", [("b.cs", "public class C { }")]).GetTypeByMetadataName("C")!;
        var comparer = EffectSymbolComparer<INamedTypeSymbol>.Instance;
        Assert.That(comparer.Compare(first, first), Is.Zero);
        Assert.That(comparer.Compare(null, first), Is.LessThan(0));
        Assert.That(comparer.Compare(first, null), Is.GreaterThan(0));
        Assert.That(comparer.Compare(first, second), Is.LessThan(0));
        var later = TestCompilation.CreateExact("Ordering", [("a.cs", "public class D { } public class C { }")]).GetTypeByMetadataName("C")!;
        Assert.That(comparer.Compare(first, later), Is.LessThan(0));
        var methodType = TestCompilation.Create("MethodOrdering", "class C { void A() {} void B() {} }").GetTypeByMetadataName("C")!;
        Assert.That(EffectSymbolComparer<ISymbol>.Instance.Compare(methodType.GetMembers("A").Single(), methodType.GetMembers("B").Single()), Is.LessThan(0));
        var nullable = first.WithNullableAnnotation(NullableAnnotation.Annotated);
        Assert.That(nullable, Is.Not.SameAs(first));
        Assert.That(SymbolEqualityComparer.Default.Equals(first, nullable), Is.True);
        Assert.That(comparer.Compare(first, (INamedTypeSymbol)nullable), Is.Zero);
    }

    [Test]
    public void AutoPropertyBackingFieldsDistinguishManualAndConstructedProperties()
    {
        var compilation = TestCompilation.Create("Properties", "public class C<T> { public T Auto { get; set; } public int Manual => 1; }");
        var type = compilation.GetTypeByMetadataName("C`1")!;
        var constructed = type.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var auto = constructed.GetMembers("Auto").OfType<IPropertySymbol>().Single();
        var manual = type.GetMembers("Manual").OfType<IPropertySymbol>().Single();
        Assert.That(AutoPropertyFacts.TryGetBackingField(auto, out var field), Is.True);
        Assert.That(field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(AutoPropertyFacts.TryGetBackingField(manual, out _), Is.False);
    }

    [Test]
    public void InitializerProjectionRecognizesFieldsPropertiesAndAbsentInitializers()
    {
        var root = CSharpSyntaxTree.ParseText("class C { int f = 1; int g; int P { get; } = 2; }").GetRoot();
        var variables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToImmutableArray();
        Assert.That(EffectProjections.GetInitializerExpression(variables[0])!.ToString(), Is.EqualTo("1"));
        Assert.That(EffectProjections.GetInitializerExpression(variables[1]), Is.Null);
        Assert.That(EffectProjections.GetInitializerExpression(root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single())!.ToString(), Is.EqualTo("2"));
        Assert.That(EffectProjections.GetInitializerExpression(root), Is.Null);
        Assert.That(EffectProjections.MapAllocation(SpecAllocationBehavior.Unknown), Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(EffectProjections.MapAllocation(SpecAllocationBehavior.None), Is.EqualTo(EffectAllocationKind.None));
        Assert.That(EffectProjections.MapAllocation((SpecAllocationBehavior)(-1)), Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(EffectProjections.MapNonResolvedThrowBehavior(SpecThrowBehavior.DoesNotThrow).IsEmpty, Is.True);
    }

    [Test]
    public void BottomAndIncompleteSummariesPreserveTheirMeaning()
    {
        var bottom = EffectSummaryProjector.Project(EffectSummary.Bottom);
        Assert.That(bottom.Effects, Is.EqualTo(EffectContractKind.None));
        Assert.That(bottom.IsComplete, Is.True);
        Assert.That(EffectSummaryDomain.Instance.Havoc(EffectSummary.Bottom), Is.EqualTo(EffectSummary.Bottom));
        Assert.That(EffectSummaryDomain.Instance.Havoc(EffectSummaryOperations.Read(EffectRegionSet.Empty)), Is.EqualTo(EffectSummary.Top));
        Assert.That(EffectSummaryOperations.Remap(EffectSummary.Bottom, EffectRegionSet.Empty, []), Is.EqualTo(EffectSummary.Bottom));
        var cyclic = EffectSummaryOperations.IncompleteAnalysis(EffectAnalysisIncompleteReason.CyclicControlFlow);
        Assert.That(cyclic.Termination, Is.EqualTo(EffectTermination.MayDiverge));
        var divergence = EffectSummaryOperations.IncompleteDivergence(cyclic);
        Assert.That(divergence.AnalysisIncompleteReason, Is.EqualTo(cyclic.AnalysisIncompleteReason));
        Assert.That(divergence.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(EffectSummaryOperations.Unsupported().Uncertainty, Is.EqualTo(EffectUncertainty.UnsupportedOperation));
        Assert.That(EffectSummaryOperations.MayDiverge().Termination, Is.EqualTo(EffectTermination.MayDiverge));
        Assert.That(EffectSummaryOperations.Remap(EffectSummaryOperations.Read(EffectRegionSet.Unknown),
            EffectRegionSet.Empty, []).Reads, Is.EqualTo(EffectRegionSet.Unknown));
    }

    [Test]
    public void InvalidEvidenceAndEventNamesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => EffectContractMappings.EvidenceName(null!)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => EffectContractMappings.EvidenceName(DayOfWeek.Monday)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => EffectDirectEventKinds.FromWireName("not-an-event")));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => EffectDirectEventKinds.ToWireName((EffectDirectEventKind)(-1))));
    }

    [Test]
    public void DirectWitnessRetainsCompilerOriginAndDeclaredException()
    {
        var compilation = TestCompilation.Create("Witness", "class C { int M() => 1; }");
        var tree = compilation.SyntaxTrees.Single();
        var literal = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        var origin = compilation.GetSemanticModel(tree).GetOperation(literal)!;
        var type = compilation.GetTypeByMetadataName("System.Exception")!;
        var eventKind = EffectDirectEventKinds.WireNames[0];
        var witness = new EffectDirectWitness(EffectContractKind.Throws, EffectContractCapabilityKind.IO,
            type, eventKind.Event, "boundary evidence", origin);
        Assert.That(witness.Effects, Is.EqualTo(EffectContractKind.Throws));
        Assert.That(witness.Capabilities, Is.EqualTo(EffectContractCapabilityKind.IO));
        Assert.That(witness.ExceptionType, Is.SameAs(type));
        Assert.That(witness.EventKind, Is.EqualTo(eventKind.Event));
        Assert.That(witness.Kind, Is.EqualTo(eventKind.WireName));
        Assert.That(witness.Detail, Is.EqualTo("boundary evidence"));
        Assert.That(witness.Origin, Is.SameAs(origin));
        Assert.That(witness.Location.SourceSpan, Is.EqualTo(literal.Span));
        Assert.That(EffectContractMappings.Violates(witness, EffectSummary.Empty), Is.True);
        Assert.That(EffectContractMappings.IsPurityViolation(witness), Is.True);
    }

    [Test]
    public void MissingContractAssemblyCannotAuthorizeTrust()
    {
        var compilation = CSharpCompilation.Create("NoContract", [CSharpSyntaxTree.ParseText("class C { void M() { } }")]);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        Assert.That(TrustedBoundaryPolicy.ForCompilation(compilation).AuthorizesDeclaredContracts(method), Is.False);
    }

    [Test]
    public void MissingSpecCannotCertifyPurityTerminationOrNonThrowingBehavior()
    {
        var compilation = TestCompilation.Create("MissingSpec", "public class C { public int Field; public void M() {} }");
        var type = compilation.GetTypeByMetadataName("C")!;
        var method = type.GetMembers("M").OfType<IMethodSymbol>().Single();
        var field = type.GetMembers("Field").OfType<IFieldSymbol>().Single();
        var table = new ResolvedApiSpecTable(ImmutableDictionary<ISymbol, ResolvedApiSpec>.Empty, []);
        Assert.That(table.IsSideEffectFree(method), Is.False);
        Assert.That(table.IsNonThrowingAndTerminating(method), Is.False);
        Assert.That(table.TryGet(field, out _), Is.False);
        Assert.That(table.Lookup(field).Status, Is.EqualTo(ApiSpecLookupStatus.Unknown));
    }

    [Test]
    public void ResolvedPublicSpecLookupReturnsItsBoundSymbol()
    {
        var compilation = TestCompilation.Create("ResolvedLookup", "public class C { }");
        var table = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        Assert.That(table.Specs, Is.Not.Empty);
        var spec = table.Specs[0];
        var lookup = table.Lookup(spec.Symbol);
        Assert.That(lookup.Status, Is.EqualTo(ApiSpecLookupStatus.Resolved));
        Assert.That(lookup.Spec, Is.SameAs(spec));
        Assert.That(lookup.Failure, Is.Null);
    }

    [Test]
    public void DuplicateSourceMethodsCannotResolveAnUnambiguousSpec()
    {
        const string name = "AmbiguousMethods";
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText("public class C { public static void Run() {} public static void Run() {} }")],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "CS0111")), Is.True);
        var evidence = new SpecEvidence(SpecEvidenceKind.Observed, "source-method-ambiguity");
        var target = new ApiSpecTarget("duplicate-source-method", "M:C.Run", "C", SpecTargetMemberKind.Method,
            "Run", true, 0, null, [], null, [new ApiSpecAssemblyIdentity(name, "")]);
        var table = ApiSpecTable.Create([new ApiSpecDeclaration(target,
            SharpProof.Testing.ApiSpecTestFacets.NeutralFacets(evidence), [])]);
        var resolved = new ApiSpecResolver(table).Resolve(compilation);
        Assert.That(resolved.Specs, Is.Empty);
        Assert.That(resolved.Failures.Single().Kind, Is.EqualTo(ApiSpecResolutionFailureKind.AmbiguousMember));
    }

    [Test]
    public void CompilerEmittedGenericAssemblyAttributesDoNotImplyReferenceAssemblyMetadata()
    {
        var compilation = TestCompilation.Create("GenericAssemblyAttribute",
            "using System; [assembly: Marker<int>] public class MarkerAttribute<T> : Attribute { }");
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var emittedImage = ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length)));
        var reference = MetadataReference.CreateFromImage(emittedImage);
        Assert.That(ApiSpecResolver.HasExpectedReferenceMetadata(reference, ApiSpecReferenceFamily.Unspecified), Is.False);
        Assert.That(ApiSpecResolver.HasExpectedReferenceMetadata(reference, ApiSpecReferenceFamily.MicrosoftNetCoreRuntime), Is.True);
        Assert.That(ApiSpecResolver.HasExpectedReferenceMetadata(reference, ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack), Is.False);
    }

    [TestCase("Complete = 1")]
    [TestCase("IsDeterministic = 1")]
    [TestCase("PreconditionFree = 1")]
    [TestCase("ThrownExceptions = null")]
    public void MalformedCompilerProducedContractArgumentsCannotAuthorizeEffects(string argument)
    {
        System.ArgumentNullException.ThrowIfNull(argument);
        var source = "using SharpProof.Attributes; [SharpProofTrusted(\"reviewed boundary\")] class C { " +
            "[EffectContract(SharpProofEffect.None, " + argument + ")] public void M() {} }";
        var compilation = CSharpCompilation.Create("MalformedContract", [CSharpSyntaxTree.ParseText(source)],
            TestMetadataReferences.WithSharpProof, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        var table = new ResolvedApiSpecTable(ImmutableDictionary<ISymbol, ResolvedApiSpec>.Empty, []);
        var resolver = new ExternalEffectResolver(compilation, table);
        Assert.That(resolver.ResolveContract(method).Kind, Is.EqualTo(EffectContractResolutionKind.Invalid));
        Assert.That(resolver.Resolve(method).Uncertainty, Is.EqualTo(EffectUncertainty.InvalidContract));
    }
}
