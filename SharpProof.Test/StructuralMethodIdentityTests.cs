using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Identity;

namespace SharpProof.Test;

[TestFixture]
public sealed class StructuralMethodIdentityTests
{
    private const string FixtureSource = """
        namespace IdentityFixtures;

        public class Outer<T>
        {
            public class Inner<U>
            {
                public struct Cursor
                {
                }

                public int Value { get; set; }

                public V Generic<V>(V value) => value;

                public Inner<U> Self() => this;

                public Cursor GetCursor() => default;

                public void Mix(
                    ref int first,
                    out string second,
                    in U third,
                    int[,] matrix,
                    int[][] jagged,
                    (T Left, U Right) tuple)
                {
                    second = tuple.ToString();
                }

                public ref readonly int RefReturn(in int value) => ref value;

                public static explicit operator int(Inner<U> value) => value.Value;
            }
        }
        """;

    [Test]
    public void RoslynAndEcmaAdapters_ProduceIdenticalMethodIdentities()
    {
        var compilation = CreateCompilation(FixtureSource, "StructuralIdentityFixture");
        var bytes = Emit(compilation);
        var roslynMethods = GetFixtureMethods(compilation).ToArray();
        var peKeys = GetEcmaMethodKeys(bytes);

        Assert.That(roslynMethods, Is.Not.Empty);
        foreach (var method in roslynMethods)
        {
            var key = RoslynStructuralMethodIdentityAdapter.GetCanonicalKey(method);
            Assert.That(
                peKeys,
                Does.Contain(key),
                "ECMA identity did not match Roslyn for " + method.ToDisplayString());
        }
    }

    [Test]
    public void CanonicalIdentity_IsIndependentOfCultureAndCompilationAllocationOrder()
    {
        var first = CreateCompilation(FixtureSource, "FirstIdentityFixture");
        var second = CreateCompilation("\n" + FixtureSource, "SecondIdentityFixture");
        var firstMethod = GetFixtureMethods(first).Single(method => method.Name == "Mix");
        var secondMethod = GetFixtureMethods(second).Single(method => method.Name == "Mix");
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkishKey = RoslynStructuralMethodIdentityAdapter.GetCanonicalKey(firstMethod);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var frenchKey = RoslynStructuralMethodIdentityAdapter.GetCanonicalKey(secondMethod);

            Assert.That(frenchKey, Is.EqualTo(turkishKey));
            Assert.That(StructuralMethodIdentity.TryParseCanonicalKey(turkishKey, out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(RoslynStructuralMethodIdentityAdapter.Create(firstMethod)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public void AlternateContainingType_RewritesTheStructuralFieldForEverySignatureShape()
    {
        var compilation = CreateCompilation(FixtureSource, "AlternateIdentityFixture");
        foreach (var method in GetFixtureMethods(compilation))
        {
            var identity = RoslynStructuralMethodIdentityAdapter.Create(method);
            var alternate = identity.WithContainingMetadataType("System.RuntimeType");
            var key = alternate.ToCanonicalKey();

            Assert.That(StructuralMethodIdentity.TryParseCanonicalKey(key, out var parsed), Is.True);
            Assert.That(parsed.ContainingMetadataType, Is.EqualTo("System.RuntimeType"));
            Assert.That(parsed.MethodKind, Is.EqualTo(identity.MethodKind));
            Assert.That(parsed.Name, Is.EqualTo(identity.Name));
            Assert.That(parsed.GenericArity, Is.EqualTo(identity.GenericArity));
            Assert.That(parsed.Parameters, Is.EqualTo(identity.Parameters));
            Assert.That(parsed.ReturnType, Is.EqualTo(identity.ReturnType));
            Assert.That(parsed.ReturnRefKind, Is.EqualTo(identity.ReturnRefKind));
        }
    }

    [Test]
    public void ImplicitGenericConstructor_HasStableMetadataName()
    {
        var compilation = CreateCompilation(
            "namespace IdentityFixtures; public sealed class Box<T> { }",
            "ImplicitConstructorIdentityFixture");
        var type = compilation.GetTypeByMetadataName("IdentityFixtures.Box`1");
        Assert.That(type, Is.Not.Null);
        var constructor = type!.InstanceConstructors.Single();

        var identity = RoslynStructuralMethodIdentityAdapter.Create(constructor);

        Assert.That(identity.MethodKind, Is.EqualTo("constructor"));
        Assert.That(identity.Name, Is.EqualTo(".ctor"));
        Assert.That(identity.ToCanonicalKey(), Does.StartWith("spm1|"));
    }

    [Test]
    public void RuntimeNestedGenericReturnIdentity_MatchesEmbeddedEffectSummary()
    {
        var compilation = CreateCompilation("public sealed class Probe { }", "RuntimeNestedIdentityFixture");
        var hashSet = compilation.GetTypeByMetadataName("System.Collections.Generic.HashSet`1");
        Assert.That(hashSet, Is.Not.Null);
        var getEnumerator = hashSet!.GetMembers("GetEnumerator")
            .OfType<IMethodSymbol>()
            .Single(method => method.Parameters.Length == 0);
        var roslynKey = RoslynStructuralMethodIdentityAdapter.GetCanonicalKey(getEnumerator.OriginalDefinition);

        var analyzerAssembly = typeof(SharpProofAnalyzer).Assembly;
        const string resourceName =
            "SharpProof.Analyzer.GeneratedPurity.runtime-core-bcl.SharpProof.EffectSummary.json";
        using var stream = analyzerAssembly.GetManifestResourceStream(resourceName);
        Assert.That(stream, Is.Not.Null);
        using var document = JsonDocument.Parse(stream!);
        var entry = document.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("DisplayName").GetString() ==
                "System.Collections.Generic.HashSet`1.GetEnumerator()");

        Assert.That(roslynKey, Is.EqualTo(entry.GetProperty("CanonicalKey").GetString()));
        Assert.That(entry.GetProperty("Classification").GetString(), Is.EqualTo("pure"));

        var catalogType = analyzerAssembly.GetType("SharpProof.Analyzer.GeneratedPurityCatalog", true)!;
        var catalog = catalogType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
        var arguments = new object?[] { getEnumerator.OriginalDefinition, compilation, null };
        Assert.That((bool)tryGetPurity.Invoke(catalog, arguments)!, Is.True);
        var classification = arguments[2]!;
        Assert.That(
            classification.GetType().GetProperty("Classification")!.GetValue(classification),
            Is.EqualTo("pure"));
    }

    private static IEnumerable<IMethodSymbol> GetFixtureMethods(Compilation compilation)
    {
        var type = compilation.GetTypeByMetadataName("IdentityFixtures.Outer`1+Inner`1");
        Assert.That(type, Is.Not.Null);

        foreach (var method in type!.GetMembers().OfType<IMethodSymbol>())
            if (!method.IsImplicitlyDeclared)
                yield return method;

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.GetMethod != null) yield return property.GetMethod;
            if (property.SetMethod != null) yield return property.SetMethod;
        }
    }

    private static HashSet<string> GetEcmaMethodKeys(byte[] assemblyBytes)
    {
        using var stream = new MemoryStream(assemblyBytes, writable: false);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var adapterType = LoadEffectSummaryAssembly()
            .GetType("SharpProof.Identity.EcmaStructuralMethodIdentityAdapter", throwOnError: true)!;
        var getCanonicalKey = adapterType.GetMethod(
            "GetCanonicalKey",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return reader.MethodDefinitions
            .Select(handle => (string)getCanonicalKey.Invoke(null, new object[] { reader, handle })!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Assembly LoadEffectSummaryAssembly()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var path = Path.Combine(
                repositoryRoot,
                "Tools",
                "SharpProof.EffectSummary",
                "bin",
                configuration,
                "net8.0",
                "SharpProof.EffectSummary.dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }

        throw new FileNotFoundException("SharpProof.EffectSummary.dll was not built.");
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)) },
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return ImmutableArray.Create<MetadataReference>(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
