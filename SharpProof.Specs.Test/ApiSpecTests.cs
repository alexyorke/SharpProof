using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecTests {
    [Test]
    public void TablesAssignDeterministicLocalIdsButKeepScopesDistinct() {
        var first = ApiSpecTable.Create([
            Declaration("z-row", "M:Missing.Z.Run", "Missing.Z"),
            Declaration("a-row", "M:Missing.A.Run", "Missing.A")
        ]);
        var second = ApiSpecTable.Create([
            Declaration("a-row", "M:Missing.A.Run", "Missing.A"),
            Declaration("z-row", "M:Missing.Z.Run", "Missing.Z")
        ]);
        var firstA = first.Templates.Single(static row => row.Target.WitnessIdentifier == "a-row");
        var secondA = second.Templates.Single(static row => row.Target.WitnessIdentifier == "a-row");

        Assert.Multiple(() => {
            Assert.That(firstA.Id.Value, Is.Zero);
            Assert.That(secondA.Id.Value, Is.Zero);
            Assert.That(firstA.Id, Is.Not.EqualTo(secondA.Id));
            Assert.That(firstA.Parameters.Single().Value, Is.Zero);
            Assert.That(firstA.Result!.Value.Value, Is.EqualTo(1));
            Assert.That(firstA.Parameters.Single().Spec, Is.EqualTo(firstA.Id));
        });
    }

    [Test]
    public void TemplatesInstantiateIntoIndependentIrFactoriesBySpecVariableIdentity() {
        var template = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.string.length");
        var firstFactory = new IrFactory();
        var secondFactory = new IrFactory();
        var first = InstantiateStringLength(template, firstFactory);
        var second = InstantiateStringLength(template, secondFactory);

        Assert.Multiple(() => {
            Assert.That(first.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(first.Postconditions.Single().Id, Is.Not.EqualTo(second.Postconditions.Single().Id));
            Assert.That(
                new IrPrinter(firstFactory).Print(first.Postconditions.Single()),
                Is.EqualTo("(v1 == len(v0))"));
            Assert.That(
                new IrPrinter(secondFactory).Print(second.Postconditions.Single()),
                Is.EqualTo("(v1 == len(v0))"));
            Assert.That(
                typeof(ApiSpecTemplate).GetProperties()
                    .Any(static property => property.PropertyType.Assembly == typeof(IrTerm).Assembly),
                Is.False);
        });
    }

    [Test]
    public void InstantiationFailsClosedWhenAReferencedVariableIsMissing() {
        var template = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.math.abs.int32");
        var result = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            new IrFactory(),
            ImmutableDictionary<SpecVarId, IrTerm>.Empty);

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Failed));
            Assert.That(result.Failure!.Kind, Is.EqualTo(SpecInstantiationFailureKind.MissingSubstitution));
            Assert.That(result.Postconditions, Is.Empty);
        });
    }

    [Test]
    public void DefaultRowsResolveOnceToOriginalFrameworkDefinitions() {
        var compilation = CreatePlatformCompilation();
        var resolver = new ApiSpecResolver(ApiSpecTable.Default);
        var first = resolver.Resolve(compilation);
        var second = resolver.Resolve(compilation);
        var length = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("Length")
            .OfType<IPropertySymbol>()
            .Single()
            .GetMethod!;

        Assert.Multiple(() => {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.IsComplete, Is.True, string.Join(
                Environment.NewLine,
                first.Failures.Select(static failure => failure.WitnessIdentifier + ": " + failure.Detail)));
            Assert.That(first.Specs.Length, Is.EqualTo(ApiSpecTable.Default.Templates.Length));
            Assert.That(first.TryGet(length, out var spec), Is.True);
            Assert.That(spec!.Template.Target.WitnessIdentifier, Is.EqualTo("bcl.string.length"));
            Assert.That(spec.Symbol, Is.EqualTo(length.OriginalDefinition).Using(SymbolEqualityComparer.Default));
        });
    }

    [TestCase("netstandard2.0")]
    [TestCase("net8.0")]
    [TestCase("net472")]
    public void DefaultRowsResolveAgainstEverySupportedReferenceSurface(
        string targetFramework) {
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(
            CreateTargetFrameworkCompilation(targetFramework));

        Assert.Multiple(() => {
            Assert.That(
                resolved.Failures,
                Is.Empty,
                targetFramework + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    resolved.Failures.Select(static failure =>
                        failure.WitnessIdentifier + ": " + failure.Detail)));
            Assert.That(
                resolved.Specs.Length,
                Is.EqualTo(ApiSpecTable.Default.Templates.Length),
                targetFramework);
        });
    }

    [Test]
    public void MissingTypesAndMembersProduceTypedResolutionFailures() {
        var compilation = CreatePlatformCompilation();
        var missingType = new ApiSpecResolver(ApiSpecTable.Create([
            Declaration("missing-type", "M:Missing.Widget.Run", "Missing.Widget")
        ])).Resolve(compilation);
        var missingMember = new ApiSpecResolver(ApiSpecTable.Create([
            Declaration("missing-member", "M:System.Object.NotReal", "System.Object")
        ])).Resolve(compilation);

        Assert.Multiple(() => {
            Assert.That(
                missingType.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.MissingContainingType));
            Assert.That(
                missingMember.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.MissingMember));
            Assert.That(missingType.Specs, Is.Empty);
            Assert.That(missingMember.Specs, Is.Empty);
        });
    }

    [Test]
    public void DuplicateMetadataTypesProduceAnAmbiguousFailure() {
        var first = CreateReference(
            "DuplicateOne",
            "namespace Duplicate { public static class Widget { public static int Run() => 1; } }");
        var second = CreateReference(
            "DuplicateTwo",
            "namespace Duplicate { public static class Widget { public static int Run() => 2; } }");
        var compilation = CSharpCompilation.Create(
            "AmbiguousConsumer",
            references: [CoreReference, first, second],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var table = ApiSpecTable.Create([
            Declaration("ambiguous", "M:Duplicate.Widget.Run", "Duplicate.Widget", parameterTypes: [])
        ]);
        var resolved = new ApiSpecResolver(table).Resolve(compilation);

        Assert.Multiple(() => {
            Assert.That(resolved.Specs, Is.Empty);
            Assert.That(
                resolved.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.AmbiguousContainingType));
        });
    }

    [Test]
    public void DuplicateRowsForOneSymbolAreRemovedRatherThanTrusted() {
        var table = ApiSpecTable.Create([
            Declaration(
                "abs-first",
                "M:System.Math.Abs(System.Int32)",
                "System.Math",
                memberName: "Abs"),
            Declaration(
                "abs-second",
                "M:System.Math.Abs(System.Int32)",
                "System.Math",
                memberName: "Abs")
        ]);
        var resolved = new ApiSpecResolver(table).Resolve(CreatePlatformCompilation());

        Assert.Multiple(() => {
            Assert.That(resolved.Specs, Is.Empty);
            Assert.That(resolved.Failures.Length, Is.EqualTo(2));
            Assert.That(
                resolved.Failures,
                Has.All.Property(nameof(ApiSpecResolutionFailure.Kind))
                    .EqualTo(ApiSpecResolutionFailureKind.DuplicateResolvedSymbol));
        });
    }

    [Test]
    public void UnspecifiedMembersAndUncertainFacetsRemainConservativeUnknowns() {
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        var toUpper = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("ToUpper")
            .OfType<IMethodSymbol>()
            .Single(static method => method.Parameters.Length == 0);
        var lookup = resolved.Lookup(toUpper);
        var empty = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.enumerable.empty");
        var cachedEmptyRows = ApiSpecTable.Default.Templates.Where(static row =>
            row.Target.WitnessIdentifier is "bcl.array.empty" or
                "bcl.enumerable.empty");

        Assert.Multiple(() => {
            Assert.That(lookup.Status, Is.EqualTo(ApiSpecLookupStatus.Unknown));
            Assert.That(lookup.Failure!.Kind, Is.EqualTo(ApiSpecLookupFailureKind.UnspecifiedMember));
            Assert.That(
                empty.Facets.Allocation.Behavior,
                Is.EqualTo(SpecAllocationBehavior.Unknown));
            Assert.That(
                cachedEmptyRows.Select(static row =>
                    row.Facets.Effects.Effects),
                Is.All.EqualTo(SpecEffect.Unknown));
        });
    }

    [Test]
    public void PureOpaqueEligibilityComesOnlyFromResolvedSpecFacets() {
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default)
            .Resolve(compilation);
        var abs = compilation.GetTypeByMetadataName("System.Math")!
            .GetMembers("Abs")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType ==
                    SpecialType.System_Int32);
        var concat = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("Concat")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.Parameters.Length == 2 &&
                method.Parameters.All(parameter =>
                    parameter.Type.SpecialType ==
                        SpecialType.System_String));
        var arrayEmpty = compilation.GetTypeByMetadataName("System.Array")!
            .GetMembers("Empty")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.IsGenericMethod &&
                method.Parameters.Length == 0);
        var enumerableEmpty = compilation
            .GetTypeByMetadataName("System.Linq.Enumerable")!
            .GetMembers("Empty")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.IsGenericMethod &&
                method.Parameters.Length == 0);
        var toUpper = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("ToUpper")
            .OfType<IMethodSymbol>()
            .Single(static method => method.Parameters.Length == 0);

        Assert.Multiple(() => {
            Assert.That(resolved.IsPureAndAllocationFree(abs), Is.True);
            Assert.That(resolved.IsPureAndAllocationFree(concat), Is.False);
            Assert.That(
                resolved.IsPureAndAllocationFree(arrayEmpty),
                Is.False);
            Assert.That(
                resolved.IsPureAndAllocationFree(enumerableEmpty),
                Is.False);
            Assert.That(resolved.IsPureAndAllocationFree(toUpper), Is.False);
            Assert.That(resolved.IsSideEffectFree(abs), Is.True);
            Assert.That(resolved.IsSideEffectFree(concat), Is.True);
            Assert.That(resolved.IsSideEffectFree(arrayEmpty), Is.False);
            Assert.That(resolved.IsSideEffectFree(enumerableEmpty), Is.False);
            Assert.That(resolved.IsSideEffectFree(toUpper), Is.False);
        });
    }

    [Test]
    public void EverySeedHasAUniqueWitnessAndResolvableDocumentationIdentifier() {
        var templates = ApiSpecTable.Default.Templates;
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);

        Assert.Multiple(() => {
            Assert.That(templates.Length, Is.EqualTo(12));
            Assert.That(
                templates.Select(static row => row.Target.WitnessIdentifier),
                Is.Unique.And.All.Not.Empty);
            Assert.That(
                templates.Select(static row => row.Target.DocumentationCommentId),
                Has.All.Not.Empty);
            Assert.That(resolved.Failures, Is.Empty);
        });
    }

    [Test]
    public void SeedFacetsRetainDocumentedAndObservedEvidence() {
        var evidence = ApiSpecTable.Default.Templates.SelectMany(static row => new[] {
            row.Facets.Effects.Evidence,
            row.Facets.Allocation.Evidence,
            row.Facets.Throws.Evidence,
            row.Facets.Nullness.Evidence,
            row.Facets.Cardinality.Evidence
        }).Concat(ApiSpecTable.Default.Templates.SelectMany(
            static row => row.Postconditions.Select(static postcondition => postcondition.Evidence)));

        Assert.Multiple(() => {
            Assert.That(evidence.Select(static item => item.Kind), Does.Contain(SpecEvidenceKind.Documented));
            Assert.That(evidence.Select(static item => item.Kind), Does.Contain(SpecEvidenceKind.Observed));
            Assert.That(evidence.Select(static item => item.Source), Has.All.Not.Empty);
        });
    }

    [Test]
    public void LegacyCoverageManifestClassifiesAllFormerFamiliesExactlyOnce() {
        var families = LegacyApiCoverageManifest.Families;
        var ported = families
            .Where(static family =>
                family.Disposition == LegacyApiDisposition.Ported)
            .ToArray();

        Assert.Multiple(() => {
            Assert.That(
                families.Length,
                Is.EqualTo(
                    LegacyApiCoverageManifest.ExpectedLegacyFamilyCount));
            Assert.That(
                families.Select(static family => family.FamilyId),
                Is.Unique.And.All.Not.Empty);
            Assert.That(
                families.Select(static family =>
                    family.SourceTypePattern + "::" +
                    family.SourceMemberPattern),
                Is.Unique);
            Assert.That(
                families.Select(static family => family.SourceTypePattern),
                Has.All.Not.Empty);
            Assert.That(
                families.Select(static family => family.SourceMemberPattern),
                Has.All.Not.Empty);
            Assert.That(
                families.Select(static family => family.Rationale),
                Has.All.Not.Empty);
            Assert.That(
                families.Select(static family => family.Disposition),
                Has.All.Matches<LegacyApiDisposition>(
                    static disposition => Enum.IsDefined(disposition)));
            Assert.That(
                Enum.GetNames<LegacyApiDisposition>(),
                Does.Not.Contain("Pending"));
            Assert.That(
                ported.Select(static family =>
                    family.MappedApiSpecWitness),
                Has.All.Not.Null.And.All.Not.Empty);
            Assert.That(
                families.Except(ported).Select(static family =>
                    family.MappedApiSpecWitness),
                Has.All.Null);
        });
    }

    [Test]
    public void CurrentBclRowsHaveCompleteUniqueAndTruthfulCoverageLineage() {
        var currentBclWitnesses = ApiSpecTable.Default.Templates
            .Select(static row => row.Target.WitnessIdentifier)
            .Where(static witness =>
                witness.StartsWith("bcl.", StringComparison.Ordinal))
            .OrderBy(static witness => witness, StringComparer.Ordinal)
            .ToArray();
        var coverage = LegacyApiCoverageManifest.CurrentBclRows;
        var coveredWitnesses = coverage
            .Select(static row => row.ApiSpecWitness)
            .OrderBy(static witness => witness, StringComparer.Ordinal)
            .ToArray();
        var familyById = LegacyApiCoverageManifest.Families
            .ToDictionary(
                static family => family.FamilyId,
                StringComparer.Ordinal);

        Assert.Multiple(() => {
            Assert.That(coveredWitnesses, Is.EqualTo(currentBclWitnesses));
            Assert.That(
                coverage.Select(static row => row.ApiSpecWitness),
                Is.Unique.And.All.Not.Empty);
            Assert.That(
                coverage.Select(static row => row.Rationale),
                Has.All.Not.Empty);
            Assert.That(
                coverage.Select(static row => row.Origin),
                Has.All.Matches<CurrentApiSpecOrigin>(
                    static origin => Enum.IsDefined(origin)));
            Assert.That(
                Enum.GetNames<CurrentApiSpecOrigin>(),
                Does.Not.Contain("Pending"));
        });

        foreach (var row in coverage) {
            if (row.Origin == CurrentApiSpecOrigin.NewSoundSeed) {
                Assert.That(
                    row.LegacyFamilyId,
                    Is.Null,
                    row.ApiSpecWitness);
                continue;
            }

            Assert.That(
                row.LegacyFamilyId,
                Is.Not.Null.And.Not.Empty,
                row.ApiSpecWitness);
            Assert.That(
                familyById.TryGetValue(
                    row.LegacyFamilyId!,
                    out var family),
                Is.True,
                row.ApiSpecWitness);
            Assert.Multiple(() => {
                Assert.That(
                    family!.Disposition,
                    Is.EqualTo(LegacyApiDisposition.Ported),
                    row.ApiSpecWitness);
                Assert.That(
                    family.MappedApiSpecWitness,
                    Is.EqualTo(row.ApiSpecWitness),
                    row.ApiSpecWitness);
            });
        }

        var portedWitnesses = LegacyApiCoverageManifest.Families
            .Where(static family =>
                family.Disposition == LegacyApiDisposition.Ported)
            .Select(static family => family.MappedApiSpecWitness)
            .OrderBy(static witness => witness, StringComparer.Ordinal)
            .ToArray();
        var legacyCoverageWitnesses = coverage
            .Where(static row =>
                row.Origin == CurrentApiSpecOrigin.LegacyPort)
            .Select(static row => row.ApiSpecWitness)
            .OrderBy(static witness => witness, StringComparer.Ordinal)
            .ToArray();
        Assert.That(
            portedWitnesses,
            Is.EqualTo(legacyCoverageWitnesses));
    }

    private static SpecInstantiationResult InstantiateStringLength(
        ApiSpecTemplate template,
        IrFactory factory) {
        var receiver = factory.CreateVariable("receiver", factory.StringType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        return ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm> {
                [template.Receiver!.Value] = factory.Variable(receiver),
                [template.Result!.Value] = factory.Variable(result)
            });
    }

    private static ApiSpecDeclaration Declaration(
        string witness,
        string documentationId,
        string containingType,
        string memberName = "Run",
        ImmutableArray<SpecValueType>? parameterTypes = null) {
        var evidence = new SpecEvidence(SpecEvidenceKind.Observed, "test-witness");
        return new ApiSpecDeclaration(
            new ApiSpecTarget(
                witness,
                documentationId,
                containingType,
                SpecTargetMemberKind.Method,
                memberName,
                true,
                0,
                null,
                parameterTypes ?? [SpecValueType.Integer],
                SpecValueType.Integer),
            new ApiSpecFacets(
                new SpecEffectFacet(SpecEffect.Unknown, evidence),
                new SpecAllocationFacet(SpecAllocationBehavior.Unknown, evidence),
                new SpecThrowFacet(SpecThrowBehavior.Unknown, [], evidence),
                new SpecNullnessFacet(SpecNullness.Unknown, evidence),
                new SpecCardinalityFacet(SpecCardinality.Unknown, null, evidence)),
            []);
    }

    private static CSharpCompilation CreatePlatformCompilation() {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Contract).Assembly.Location));
        return CSharpCompilation.Create(
            "ApiSpecTests",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpCompilation CreateTargetFrameworkCompilation(
        string targetFramework) {
        var referenceDirectory = targetFramework switch {
            "netstandard2.0" or "net472" => Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "ReferencePacks",
                targetFramework),
            "net8.0" => FindNet8ReferenceDirectory(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(targetFramework),
                targetFramework,
                "Unsupported target framework.")
        };
        if (!Directory.Exists(referenceDirectory))
            throw new InvalidOperationException(
                "Reference directory was not found: " + referenceDirectory);
        var references = Directory
            .EnumerateFiles(
                referenceDirectory,
                "*.dll",
                SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(
                typeof(Contract).Assembly.Location));
        return CSharpCompilation.Create(
            "ApiSpec." + targetFramework,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static string FindNet8ReferenceDirectory() {
        var runtimeDirectory = new DirectoryInfo(
            RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent ??
            throw new InvalidOperationException(
                "The dotnet installation root could not be located.");
        var packRoot = Path.Combine(
            dotnetRoot.FullName,
            "packs",
            "Microsoft.NETCore.App.Ref");
        var candidates = Directory
            .EnumerateDirectories(packRoot)
            .Select(path => new {
                Path = path,
                Version = Version.TryParse(
                    Path.GetFileName(path),
                    out var version)
                    ? version
                    : null
            })
            .Where(static candidate =>
                candidate.Version?.Major == 8)
            .OrderByDescending(static candidate => candidate.Version)
            .Select(static candidate => Path.Combine(
                candidate.Path,
                "ref",
                "net8.0"))
            .Where(Directory.Exists)
            .FirstOrDefault();
        return candidates ??
            throw new InvalidOperationException(
                "A .NET 8 reference pack could not be located.");
    }

    private static MetadataReference CreateReference(string assemblyName, string source) {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [CoreReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference CoreReference { get; } =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
}
