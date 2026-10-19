using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiPolicyBoundaryTests
{
    private const string ShaKey = "SharpProof.Attributes.SHA256";
    private const string MvidKey = "SharpProof.Attributes.MVID";
    private const string ResultMethod = "public static T Result<T>() => throw new System.InvalidOperationException();";
    private const string OldMethod = "public static T Old<T>(T value) => value;";
    private const string RequiresMethod = "[System.Diagnostics.Conditional(ConditionalSymbol)] public static void Requires(bool condition) { }";

    [Test]
    public void MissingShaMetadataRejectsThePayload()
    {
        AssertPayloadMetadataRejected([]);
    }

    [Test]
    public void ShortShaMetadataRejectsThePayload()
    {
        AssertPayloadMetadataRejected([(ShaKey, AttributesHash()[..63])]);
    }

    [Test]
    public void NonHexShaMetadataRejectsThePayload()
    {
        AssertPayloadMetadataRejected([(ShaKey, "GG" + AttributesHash()[2..])]);
    }

    [Test]
    public void ConflictingShaMetadataRejectsThePayload()
    {
        AssertPayloadMetadataRejected([(ShaKey, AttributesHash()), (ShaKey, OtherAssemblyHash())]);
    }

    [Test]
    public void IdenticalShaMetadataRemainOneExpectedValue()
    {
        var hash = AttributesHash();
        var policy = CreatePolicy((ShaKey, hash), (ShaKey, hash), (MvidKey, AttributesMvid()));
        AssertPayloadAccepted(policy);
    }

    [Test]
    public void LowercaseShaMetadataAcceptsTheActualPayload()
    {
        var lowercaseHash = string.Concat(Convert.FromHexString(AttributesHash())
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        var policy = CreatePolicy((ShaKey, lowercaseHash), (MvidKey, AttributesMvid()));
        AssertPayloadAccepted(policy);
    }

    [Test]
    public void MissingMvidMetadataRejectsTheImage()
    {
        AssertImageMetadataRejected([(ShaKey, AttributesHash())]);
    }

    [Test]
    public void InvalidMvidMetadataRejectsTheImage()
    {
        AssertImageMetadataRejected([(ShaKey, AttributesHash()), (MvidKey, "not-a-guid")]);
    }

    [Test]
    public void ConflictingMvidMetadataRejectsTheImage()
    {
        AssertImageMetadataRejected([(ShaKey, AttributesHash()), (MvidKey, AttributesMvid()), (MvidKey, OtherAssemblyMvid())]);
    }

    [Test]
    public void IdenticalMvidMetadataRemainOneExpectedValue()
    {
        var mvid = AttributesMvid();
        var policy = CreatePolicy((ShaKey, AttributesHash()), (MvidKey, mvid), (MvidKey, mvid));
        AssertPayloadAccepted(policy);
    }

    [Test]
    public void ActualRuntimeOwnerAcceptsItsFileAndImage()
    {
        var policy = ActualPolicy();
        Assert.That(policy.ExpectedAssemblyVersion, Is.EqualTo(typeof(ContractApiIdentityResolver).Assembly.GetName().Version));
        AssertPayloadAccepted(policy);
    }

    [Test]
    public void GenuineNetmoduleIsRejectedAsAnAssemblyImage()
    {
        var image = Emit("public static class StandaloneModule { }", OutputKind.NetModule);
        var reference = MetadataReference.CreateFromImage(image, properties: MetadataReferenceProperties.Module);
        Assert.That(reference.GetMetadata(), Is.InstanceOf<ModuleMetadata>());
        Assert.That(ActualPolicy().MatchesImagePayload(reference, out var reason), Is.False);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void GenuineNativePeIsAReadableUnsupportedPayload()
    {
        TestRepository.RequireCanonicalContainer();
        var nativePath = LockedNativeZ3Path();
        using (var stream = File.OpenRead(nativePath))
        using (var reader = new PEReader(stream))
        {
            Assert.That(reader.HasMetadata, Is.False);
        }
        var bound = AttributesReference();
        Assert.That(bound.FilePath, Is.Null);
        Assert.That(bound.GetMetadata(), Is.InstanceOf<AssemblyMetadata>());
        Assert.That(ActualPolicy().MatchesBoundFilePayload(nativePath, bound, out var reason), Is.False);
        Assert.That(reason, Is.Null, "A real native PE is readable and incompatible, rather than an I/O failure.");
    }

    [Test]
    public void DifferentRealBoundImageRejectsTheReadableFile()
    {
        var other = MetadataReference.CreateFromImage(File.ReadAllBytes(typeof(Compilation).Assembly.Location));
        Assert.That(AttributesMvid(), Is.Not.EqualTo(OtherAssemblyMvid()));
        Assert.That(ActualPolicy().MatchesBoundFilePayload(AttributesPath, other, out var reason), Is.False);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void DifferentRealExpectedHashRejectsTheSameBoundFile()
    {
        Assert.That(AttributesHash(), Is.Not.EqualTo(OtherAssemblyHash()));
        var policy = CreatePolicy((ShaKey, OtherAssemblyHash()), (MvidKey, AttributesMvid()));
        Assert.That(policy.MatchesBoundFilePayload(AttributesPath, AttributesReference(), out var reason), Is.False);
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void AbsentFilePreservesTheUnreadableReason()
    {
        using var temporary = new TempDirectory("SharpProofPayloadPolicy-");
        var missing = Path.Combine(temporary.FullName, "absent.dll");
        Assert.That(ActualPolicy().MatchesBoundFilePayload(missing, AttributesReference(), out var reason), Is.False);
        Assert.That(reason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GenuineCompatibleApiShapeIsAccepted()
    {
        AssertShape(ResultMethod, OldMethod, RequiresMethod, accepted: true);
    }

    [Test]
    public void NongenericResultShapeIsRejected()
    {
        AssertShape("public static int Result() => 0;", OldMethod, RequiresMethod, accepted: false);
    }

    [Test]
    public void NonIdentityReturnShapeIsRejected()
    {
        AssertShape("public static int Result<T>() => 0;", OldMethod, RequiresMethod, accepted: false);
    }

    [Test]
    public void ConstrainedIdentityShapeIsRejected()
    {
        AssertShape("public static T Result<T>() where T : class => throw new System.InvalidOperationException();",
            OldMethod, RequiresMethod, accepted: false);
    }

    [Test]
    public void RefIdentityParameterShapeIsRejected()
    {
        AssertShape(ResultMethod, "public static T Old<T>(ref T value) => value;", RequiresMethod, accepted: false);
    }

    [Test]
    public void InternalIdentityMethodShapeIsRejected()
    {
        AssertShape("internal static T Result<T>() => throw new System.InvalidOperationException();",
            OldMethod, RequiresMethod, accepted: false);
    }

    [Test]
    public void DistinctResultOverloadsAreRejected()
    {
        AssertShape(ResultMethod + " public static T Result<T>(int unused) => throw new System.InvalidOperationException();",
            OldMethod, RequiresMethod, accepted: false);
    }

    [Test]
    public void DistinctClauseOverloadsAreRejected()
    {
        AssertShape(ResultMethod, OldMethod,
            RequiresMethod + " [System.Diagnostics.Conditional(ConditionalSymbol)] public static void Requires(object condition) { }",
            accepted: false);
    }

    [Test]
    public void ShapeAcceptanceDoesNotGrantAssemblyTrust()
    {
        var consumer = ShapeConsumer(ShapeSource(ResultMethod, OldMethod, RequiresMethod, "SharpProof.Attributes"));
        var contract = consumer.GetTypeByMetadataName(ContractApiMetadata.Contract)!;
        Assert.That(contract, Is.Not.Null);
        Assert.That(ShapeValidator(consumer).TryResolve(contract), Is.Not.Null);
        Assert.That(contract.ContainingAssembly.Name, Does.StartWith("ContractPolicyEvidence_"));
        Assert.That(ContractApiIdentityResolver.ForCompilation(consumer).Contract, Is.Null);
    }

    [Test]
    public void ResolverStillAcceptsTheShippedAssembly()
    {
        var reference = AttributesReference();
        var consumer = Consumer(reference);
        var resolver = ContractApiIdentityResolver.ForCompilation(consumer);
        Assert.That(resolver.Contract, Is.Not.Null);
        Assert.That(resolver.ResolveAttribute(ContractApiMetadata.NotNull), Is.Not.Null);
        Assert.That(resolver.UnreadableContractApiReason, Is.Null);
        Assert.That(ContractApiIdentityResolver.ForCompilation(consumer), Is.SameAs(resolver));
    }

    private static string AttributesPath => typeof(SharpProof.Attributes.Contract).Assembly.Location;
    private static ContractApiPayloadPolicy ActualPolicy()
    {
        return ContractApiPayloadPolicy.FromRuntimeOwner(typeof(ContractApiIdentityResolver).Assembly);
    }

    private static PortableExecutableReference AttributesReference()
    {
        return MetadataReference.CreateFromImage(File.ReadAllBytes(AttributesPath));
    }

    private static string AttributesHash()
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AttributesPath)));
    }

    private static string OtherAssemblyHash()
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Compilation).Assembly.Location)));
    }

    private static string AttributesMvid()
    {
        return ModuleVersionId(AttributesPath);
    }

    private static string OtherAssemblyMvid()
    {
        return ModuleVersionId(typeof(Compilation).Assembly.Location);
    }

    private static string ModuleVersionId(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
    }

    private static CSharpCompilation Consumer(PortableExecutableReference reference)
    {
        System.ArgumentNullException.ThrowIfNull(reference);
        return CSharpCompilation.Create("ContractPolicyConsumer", references: TestMetadataReferences.WithoutSharpProof.Add(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertPayloadMetadataRejected((string Key, string Value)[] facts)
    {
        System.ArgumentNullException.ThrowIfNull(facts);
        var policy = CreatePolicy(facts);
        var consumer = Consumer(AttributesReference());
        var assembly = consumer.GetTypeByMetadataName(ContractApiMetadata.Contract)!.ContainingAssembly;
        Assert.That(policy.HasTrustedPayload(consumer, assembly, out var reason), Is.False);
        Assert.That(reason, Is.Null);
    }

    private static void AssertImageMetadataRejected((string Key, string Value)[] facts)
    {
        System.ArgumentNullException.ThrowIfNull(facts);
        var policy = CreatePolicy(facts);
        Assert.That(policy.MatchesImagePayload(AttributesReference(), out var reason), Is.False);
        Assert.That(reason, Is.Null);
    }

    private static void AssertPayloadAccepted(ContractApiPayloadPolicy policy)
    {
        System.ArgumentNullException.ThrowIfNull(policy);
        var image = AttributesReference();
        var consumer = Consumer(image);
        var assembly = consumer.GetTypeByMetadataName(ContractApiMetadata.Contract)!.ContainingAssembly;
        Assert.That(policy.HasTrustedPayload(consumer, assembly, out var reason), Is.True);
        Assert.That(reason, Is.Null);
        Assert.That(policy.MatchesBoundFilePayload(AttributesPath, image, out reason), Is.True);
        Assert.That(reason, Is.Null);
        var fileConsumer = Consumer(MetadataReference.CreateFromFile(AttributesPath));
        var fileAssembly = fileConsumer.GetTypeByMetadataName(ContractApiMetadata.Contract)!.ContainingAssembly;
        Assert.That(policy.HasTrustedPayload(fileConsumer, fileAssembly, out reason), Is.True);
        Assert.That(reason, Is.Null);
    }

    private static ContractApiPayloadPolicy CreatePolicy(params (string Key, string Value)[] facts)
    {
        System.ArgumentNullException.ThrowIfNull(facts);
        var attributes = facts.Select(fact =>
            "[assembly: System.Reflection.AssemblyMetadata(" +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(fact.Key, quote: true) + ", " +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(fact.Value, quote: true) + ")]");
        var source = "[assembly: System.Reflection.AssemblyVersion(\"3.2.1.0\")]\n" +
            string.Join("\n", attributes) + "\npublic static class RuntimePayloadOwner { }";
        var image = Emit(source);
        var runtime = new AssemblyLoadContext("ContractPolicyOwner", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image.ToArray(), writable: false);
            var owner = runtime.LoadFromStream(stream);
            var policy = ContractApiPayloadPolicy.FromRuntimeOwner(owner);
            Assert.That(policy.ExpectedAssemblyVersion, Is.EqualTo(owner.GetName().Version));
            return policy;
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static ImmutableArray<byte> Emit(string source, OutputKind kind = OutputKind.DynamicallyLinkedLibrary)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12),
            path: "contract-policy-evidence.cs");
        var compilation = CSharpCompilation.Create("ContractPolicyEvidence_" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(kind, optimizationLevel: OptimizationLevel.Release));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(emitted.Diagnostics.Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length)));
    }

    private static CSharpCompilation ShapeConsumer(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        return CSharpCompilation.Create("ContractShapeConsumer",
            references: TestMetadataReferences.WithoutSharpProof.Add(MetadataReference.CreateFromImage(Emit(source))),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ContractApiShapeValidator ShapeValidator(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return new(compilation.GetTypeByMetadataName(ContractApiMetadata.ConditionalAttribute), new ContractApiMetadataNameMatcher());
    }

    private static void AssertShape(string result, string old, string requires, bool accepted)
    {
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(old);
        System.ArgumentNullException.ThrowIfNull(result);
        var compilation = ShapeConsumer(ShapeSource(result, old, requires));
        var contract = compilation.GetTypeByMetadataName("Fixture.Contract")!;
        Assert.That(contract.Locations.All(location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInMetadata)), Is.True);
        var shape = ShapeValidator(compilation).TryResolve(contract);
        Assert.That(shape.HasValue, Is.EqualTo(accepted));
        if (accepted)
        {
            Assert.That(shape!.Value.Result.Name, Is.EqualTo("Result"));
            Assert.That(shape.Value.Old.Name, Is.EqualTo("Old"));
        }
    }

    private static string ShapeSource(string result, string old, string requires, string namespaceName = "Fixture")
    {
        System.ArgumentNullException.ThrowIfNull(namespaceName);
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(old);
        System.ArgumentNullException.ThrowIfNull(result);
        return $$"""
        namespace {{namespaceName}}
        {
            public static class Contract
            {
                public const string ConditionalSymbol = "SHARPPROOF_CONTRACTS";
                {{requires}}
                [System.Diagnostics.Conditional(ConditionalSymbol)] public static void Ensures(bool condition) { }
                [System.Diagnostics.Conditional(ConditionalSymbol)] public static void Assume(bool condition) { }
                {{result}}
                {{old}}
            }
        }
        """;
    }

    private static string LockedNativeZ3Path()
    {
        var root = TestRepository.FindRoot();
        using var locked = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/SharpProof.Smt", "packages.lock.json")));
        var identities = locked.RootElement.GetProperty("dependencies").EnumerateObject()
            .SelectMany(framework => framework.Value.ValueKind != JsonValueKind.Object ? throw new System.ArgumentException("Dependency framework must be an object.", nameof(framework)) : (framework.Value.EnumerateObject()))
            .Where(package => package.Value.ValueKind != JsonValueKind.Object ? throw new System.ArgumentException("Dependency package must be an object.", nameof(package)) : (package.Name == "Microsoft.Z3"))
            .Select(package => package.Value.ValueKind != JsonValueKind.Object ? throw new System.ArgumentException("Dependency package must be an object.", nameof(package)) : ((Version: package.Value.GetProperty("resolved").GetString()!,
                Hash: package.Value.GetProperty("contentHash").GetString()!)))
            .Distinct().ToImmutableArray();
        Assert.That(identities, Has.Length.EqualTo(1));
        var identity = identities.Single();
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Assert.That(packages, Is.Not.Null.And.Not.Empty);
        var packageRoot = Path.Combine(packages!, "microsoft.z3", identity.Version);
        var archivePath = Path.Combine(packageRoot, "microsoft.z3." + identity.Version + ".nupkg");
        Assert.That(File.Exists(archivePath), Is.True, "The canonical restored locked dependency must exist.");
        using var restored = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, ".nupkg.metadata")));
        Assert.That(restored.RootElement.GetProperty("contentHash").GetString(), Is.EqualTo(identity.Hash));
        var archiveHash = Convert.ToBase64String(SHA512.HashData(File.ReadAllBytes(archivePath)));
        Assert.That(archiveHash, Is.EqualTo(File.ReadAllText(archivePath + ".sha512").Trim()));
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry("runtimes/win-x64/native/libz3.dll");
        Assert.That(entry, Is.Not.Null);
        var nativePath = Path.Combine(packageRoot, "runtimes", "win-x64", "native", "libz3.dll");
        Assert.That(File.Exists(nativePath), Is.True);
        using var original = entry!.Open();
        Assert.That(SHA256.HashData(File.ReadAllBytes(nativePath)), Is.EqualTo(SHA256.HashData(original)));
        return nativePath;
    }
}
