using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ExceptionSummaryCatalogValidationTests
    {
        private static readonly object EffectSummaryToolBuildLock = new object();
        private static string? s_effectSummaryToolDllPath;

        [Test]
        public async Task Ps0010_EffectSummary_WithMatchingAssemblyIdentity_IsTrusted()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)"));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithMismatchedAssemblyIdentity_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                "0000000000000000000000000000000000000000000000000000000000000000",
                "00000000-0000-0000-0000-000000000000"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithIncompleteAssemblyIdentity_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                string.Empty,
                coreLib.ModuleVersionId));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithMismatchedMetadataToken_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                metadataToken: "0x06000001"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithMismatchedMethodBodyHash_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var methodIdentity = GetMethodIdentity(coreLib.AssemblyPath, "System.ArgumentNullException.ThrowIfNull(object, string)");
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                metadataToken: methodIdentity.MetadataToken,
                methodBodySha256: new string('0', methodIdentity.MethodBodySha256!.Length)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithSuffixedSummaryFileName_IsTrusted()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                CreateLibraryCallSource(),
                CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"),
                "runtime.PurelySharp.EffectSummary.json");

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_MergesDirectAndTransitiveExceptionTypes()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                CreateLibraryCallSource(),
                CreateEffectSummaryJson(
                    coreLib,
                    "System.ArgumentNullException.ThrowIfNull(object, string)",
                    thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                    transitiveThrownExceptionTypesJson: """[ "System.ArgumentNullException" ]"""));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty],
                Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithMalformedMethodEntry_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                CreateLibraryCallSource(),
                CreateMalformedEffectSummaryJson(coreLib.AssemblyName, coreLib.AssemblySha256, coreLib.ModuleVersionId));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithWrongSymbol_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                CreateLibraryCallSource(),
                CreateEffectSummaryJson(
                    coreLib,
                    "System.ArgumentNullException.ThrowIfNull(object)",
                    actualMethodLookupSymbol: "System.ArgumentNullException.ThrowIfNull(object, string)"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_MergesAcrossMultipleSummaryFiles()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                CreateLibraryCallSource(),
                ("PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        coreLib,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                        transitiveThrownExceptionTypesJson: "[]")),
                ("runtime.PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        coreLib,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        thrownExceptionTypesJson: "[]",
                        transitiveThrownExceptionTypesJson: """[ "System.ArgumentNullException" ]""")));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty],
                Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_GenericMetadataMethodSummary_MatchesConstructedCall()
        {
            const string boundarySource = """
using System;

public static class GenericBoundary
{
    public static T EchoOrThrow<T>(T value) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GenericBoundarySummary", boundarySource);
            var boundaryCompilation = CSharpCompilation.Create(
                "GenericBoundaryInspection",
                Array.Empty<SyntaxTree>(),
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var boundaryType = boundaryCompilation.GetTypeByMetadataName("GenericBoundary")!;
            var methodSymbol = boundaryType.GetMembers("EchoOrThrow").OfType<IMethodSymbol>().Single();
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public class TestClass
{
    public string TestMethod(string value)
    {
        return GenericBoundary.EchoOrThrow<string>(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreateEffectSummaryJson(
                            identity,
                            methodSymbol.OriginalDefinition.ToDisplayString(),
                            actualMethodLookupSymbol: "GenericBoundary.EchoOrThrow(!!0)",
                            thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                            transitiveThrownExceptionTypesJson: "[]"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_MetadataConstructorSummary_MatchesCall()
        {
            const string boundarySource = """
using System;

public sealed class ConstructorBoundary
{
    public ConstructorBoundary(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("ConstructorBoundarySummary", boundarySource);
            var boundaryCompilation = CSharpCompilation.Create(
                "ConstructorBoundaryInspection",
                Array.Empty<SyntaxTree>(),
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var boundaryType = boundaryCompilation.GetTypeByMetadataName("ConstructorBoundary")!;
            var constructorSymbol = boundaryType.InstanceConstructors.Single(ctor => ctor.Parameters.Length == 1);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public ConstructorBoundary TestMethod(string value)
    {
        return new ConstructorBoundary(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreateEffectSummaryJson(
                            identity,
                            constructorSymbol.OriginalDefinition.ToDisplayString(),
                            actualMethodLookupSymbol: "ConstructorBoundary..ctor(string)",
                            thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                            transitiveThrownExceptionTypesJson: "[]"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_MetadataPropertyGetterSummary_MatchesCall()
        {
            const string boundarySource = """
using System;

public sealed class PropertyBoundary
{
    public PropertyBoundary(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public string DangerousValue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                throw new InvalidOperationException();
            }

            return Value;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("PropertyBoundarySummary", boundarySource);
            var boundaryCompilation = CSharpCompilation.Create(
                "PropertyBoundaryInspection",
                Array.Empty<SyntaxTree>(),
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var boundaryType = boundaryCompilation.GetTypeByMetadataName("PropertyBoundary")!;
            var getterSymbol = boundaryType.GetMembers("DangerousValue")
                .OfType<IPropertySymbol>()
                .Single()
                .GetMethod!;
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public string TestMethod(PropertyBoundary boundary)
    {
        return boundary.DangerousValue;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreateEffectSummaryJson(
                            identity,
                            getterSymbol.OriginalDefinition.ToDisplayString(),
                            actualMethodLookupSymbol: "PropertyBoundary.get_DangerousValue()",
                            thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                            transitiveThrownExceptionTypesJson: "[]"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_MetadataBinaryOperatorSummary_MatchesCall()
        {
            const string boundarySource = """
using System;

public readonly struct OperatorBoundary
{
    public OperatorBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static OperatorBoundary operator +(OperatorBoundary left, OperatorBoundary right)
    {
        throw new InvalidOperationException();
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("OperatorBoundarySummary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            var methodIdentity = GetMethodIdentity(
                fixture.AssemblyPath,
                "OperatorBoundary.op_Addition(OperatorBoundary, OperatorBoundary)");

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public OperatorBoundary TestMethod(OperatorBoundary left, OperatorBoundary right)
    {
        return left + right;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreateEffectSummaryJson(
                            identity,
                            methodIdentity.ExactSymbolKey,
                            actualMethodLookupSymbol: methodIdentity.Symbol,
                            thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                            transitiveThrownExceptionTypesJson: "[]"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            AssertEffectSummaryException(diagnostics, "TestMethod", "System.InvalidOperationException");
        }

        [Test]
        public async Task Ps0010_EffectSummary_MetadataOutParameterSummary_MatchesCall()
        {
            const string boundarySource = """
using System;

public static class OutBoundary
{
    public static void ParseOrThrow(string value, out int result)
    {
        throw new InvalidOperationException();
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("OutBoundarySummary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            var methodIdentity = GetMethodIdentity(
                fixture.AssemblyPath,
                "OutBoundary.ParseOrThrow(string, ref int)");

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public int TestMethod(string value)
    {
        OutBoundary.ParseOrThrow(value, out var parsed);
        return parsed;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreateEffectSummaryJson(
                            identity,
                            methodIdentity.ExactSymbolKey.Replace("ref int", "out int", StringComparison.Ordinal),
                            actualMethodLookupSymbol: methodIdentity.Symbol,
                            thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                            transitiveThrownExceptionTypesJson: "[]"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            AssertEffectSummaryException(diagnostics, "TestMethod", "System.InvalidOperationException");
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_PropagatesTransitiveMetadataMethodException()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static string Outer(string value)
    {
        return Inner(value);
    }

    public static string Inner(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryGenerated", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public string TestMethod(string value)
    {
        return SummaryBoundary.Outer(value);
    }
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_FilteredToolOutput_PreservesDeepMetadataSourceChain()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static string Outer(string value)
    {
        return Middle(value);
    }

    private static string Middle(string value)
    {
        return Inner(value);
    }

    private static string Inner(string value)
    {
        return Leaf(value);
    }

    private static string Leaf(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            const string callerSource = """
public class TestClass
{
    public string TestMethod(string value)
    {
        return SummaryBoundary.Outer(value);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryFilteredGenerated", boundarySource);
            var references = ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));

            var summaryJson = await RunFilteredEffectSummaryJsonAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                maxDepth: 1,
                "SummaryBoundary.Outer");
            var diagnostics = await GetAnalyzerDiagnosticsAsync(callerSource, summaryJson, references);

            var expectedSourceChain = "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

            var summaryDiagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));

            var siteDiagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(siteDiagnostic.GetMessage(), Does.Contain("SummaryBoundary.Outer"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("SummaryBoundary.Outer"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_TransitiveThrownExceptionEdges_RoundTripsWithoutChangingDiagnostics()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static string Outer(string value)
    {
        return Middle(value);
    }

    private static string Middle(string value)
    {
        return Inner(value);
    }

    private static string Inner(string value)
    {
        return Leaf(value);
    }

    private static string Leaf(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            const string callerSource = """
public class TestClass
{
    public string TestMethod(string value)
    {
        return SummaryBoundary.Outer(value);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryEdgeRoundTrip", boundarySource);
            var references = ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            const string expectedSourceChain = "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                callerSource,
                CreateEffectSummaryJson(
                    identity,
                    "SummaryBoundary.Outer(string)",
                    thrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionEdgesJson:
                        $$"""[ { "ExceptionType": "System.InvalidOperationException", "CallPath": "{{expectedSourceChain}}", "Depth": 3 } ]"""),
                references);

            var summaryDiagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));

            var siteDiagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(siteDiagnostic.GetMessage(), Does.Contain("SummaryBoundary.Outer"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("SummaryBoundary.Outer"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_TransitiveThrownExceptionEdges_WithSchemaFields_MatchLegacyDiagnosticProperties()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static string Outer(string value)
    {
        return Middle(value);
    }

    private static string Middle(string value)
    {
        return Inner(value);
    }

    private static string Inner(string value)
    {
        return Leaf(value);
    }

    private static string Leaf(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            const string callerSource = """
public class TestClass
{
    public string TestMethod(string value)
    {
        return SummaryBoundary.Outer(value);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryLegacyParity", boundarySource);
            var references = ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            const string expectedSourceChain = "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

            var legacyDiagnostics = await GetAnalyzerDiagnosticsAsync(
                callerSource,
                CreateEffectSummaryJson(
                    identity,
                    "SummaryBoundary.Outer(string)",
                    thrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                    transitiveThrownExceptionSourcePathsJson:
                        $$"""[ { "ExceptionType": "System.InvalidOperationException", "SourcePath": "{{expectedSourceChain}}" } ]"""),
                references);

            var edgeDiagnostics = await GetAnalyzerDiagnosticsAsync(
                callerSource,
                CreateEffectSummaryJson(
                    identity,
                    "SummaryBoundary.Outer(string)",
                    thrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionSourcePathsJson: "[]",
                    transitiveThrownExceptionEdgesJson:
                        $$"""[ { "ExceptionType": "System.InvalidOperationException", "SourcePath": "{{expectedSourceChain}}", "CalleeExactSymbolKey": "SummaryBoundary.Leaf(string)", "Depth": 3 } ]"""),
                references);

            AssertMatchingExceptionDiagnostics(legacyDiagnostics, edgeDiagnostics, PurelySharpDiagnostics.ExceptionSummaryId);
            AssertMatchingExceptionDiagnostics(legacyDiagnostics, edgeDiagnostics, PurelySharpDiagnostics.UncaughtExceptionSiteId);
        }

        [Test]
        public async Task Ps0010_EffectSummary_TransitiveThrownExceptionEdges_WithoutExceptionType_AreIgnored()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static string Outer(string value)
    {
        return Middle(value);
    }

    private static string Middle(string value)
    {
        return Inner(value);
    }

    private static string Inner(string value)
    {
        return Leaf(value);
    }

    private static string Leaf(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException();
        }

        return value;
    }
}
""";

            const string callerSource = """
public class TestClass
{
    public string TestMethod(string value)
    {
        return SummaryBoundary.Outer(value);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryMalformedEdge", boundarySource);
            var references = ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                callerSource,
                CreateEffectSummaryJson(
                    identity,
                    "SummaryBoundary.Outer(string)",
                    thrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionSourcePathsJson: "[]",
                    transitiveThrownExceptionEdgesJson:
                        """[ { "SourcePath": "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string)", "CalleeExactSymbolKey": "SummaryBoundary.Middle(string)", "Depth": 1 } ]"""),
                references);

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_PropagatesCommonMetadataExceptions()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static void ThrowIndexOutOfRange() => throw new IndexOutOfRangeException();
    public static void ThrowInvalidCast() => throw new InvalidCastException();
    public static void ThrowObjectDisposed() => throw new ObjectDisposedException("stream");
    public static void ThrowFormat() => throw new FormatException();
    public static void ThrowOverflow() => throw new OverflowException();
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryCommonExceptions", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public void IndexOutOfRange() => SummaryBoundary.ThrowIndexOutOfRange();
    public void InvalidCast() => SummaryBoundary.ThrowInvalidCast();
    public void ObjectDisposed() => SummaryBoundary.ThrowObjectDisposed();
    public void Format() => SummaryBoundary.ThrowFormat();
    public void Overflow() => SummaryBoundary.ThrowOverflow();
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            AssertEffectSummaryException(diagnostics, "IndexOutOfRange", "System.IndexOutOfRangeException");
            AssertEffectSummaryException(diagnostics, "InvalidCast", "System.InvalidCastException");
            AssertEffectSummaryException(diagnostics, "ObjectDisposed", "System.ObjectDisposedException");
            AssertEffectSummaryException(diagnostics, "Format", "System.FormatException");
            AssertEffectSummaryException(diagnostics, "Overflow", "System.OverflowException");
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_DoesNotReportLocallyCaughtMetadataThrow()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static int HandleLocally()
    {
        try
        {
            throw new FormatException();
        }
        catch (FormatException)
        {
            return 1;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryCaughtException", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public int TestMethod()
    {
        return SummaryBoundary.HandleLocally();
    }
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_DoesNotReportMetadataThrowCaughtByTrueFilter()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static int ThrowFormat()
    {
        throw new FormatException();
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryCaughtByTrueFilter", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            return SummaryBoundary.ThrowFormat();
        }
        catch (FormatException) when (true)
        {
            return 1;
        }
    }
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_PropagatesMetadataRethrow()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static void RethrowOverflow()
    {
        try
        {
            throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryRethrowException", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public void TestMethod()
    {
        SummaryBoundary.RethrowOverflow();
    }
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            AssertEffectSummaryException(diagnostics, "TestMethod", "System.OverflowException");
        }

        [Test]
        public async Task Ps0010_EffectSummary_ToolOutput_SuppressesMetadataRethrowCaughtByOuterHandler()
        {
            const string boundarySource = """
using System;

public static class SummaryBoundary
{
    public static int RethrowFormatCaughtByOuter()
    {
        try
        {
            try
            {
                throw new FormatException();
            }
            catch (Exception)
            {
                throw;
            }
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("SummaryBoundaryCaughtRethrowException", boundarySource);
            var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
public class TestClass
{
    public int TestMethod()
    {
        return SummaryBoundary.RethrowFormatCaughtByOuter();
    }
}
""",
                summaryJson,
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureClassification_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public static class PureBoundary
{
    public static int Double(int value) => value * 2;
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return PureBoundary.Double(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "PureBoundary.Double(int)",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureClassification_ReportsImpurity()
        {
            const string boundarySource = """
public static class ImpureBoundary
{
    private static int _state;

    public static int Next()
    {
        _state++;
        return _state;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return ImpureBoundary.Next();
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ImpureBoundary.Next()",
                            "impure",
                            """[ "global_state_write" ]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_OverridesKnownPureWebUtilityClassification_WhenGeneratedSummaryIsImpure()
        {
            var identity = GetAssemblyIdentity(typeof(System.Net.WebUtility).Assembly.Location);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;
using System.Net;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "System.Net.WebUtility.HtmlEncode(string)",
                            "impure",
                            """[ "global_state_write" ]"""))
                });

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_OverridesKnownImpureEnvironmentMethod_WhenGeneratedSummaryIsPure()
        {
            var identity = GetAssemblyIdentity(typeof(System.Environment).Assembly.Location);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.GetEnvironmentVariable("PATH");
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "System.Environment.GetEnvironmentVariable(string)",
                            "pure",
                            """[]"""))
                });

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should override the built-in known-impure member fallback for metadata methods.");
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureConstructorClassification_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public sealed class PureConstructorBoundary
{
    public PureConstructorBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureConstructorBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public PureConstructorBoundary TestMethod(int value)
    {
        return new PureConstructorBoundary(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "PureConstructorBoundary..ctor(int)",
                            "pure",
                            """[]""",
                            "PureConstructorBoundary.PureConstructorBoundary(int)"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_ConfigKnownImpureConstructor_OverridesGeneratedPureConstructorClassification()
        {
            const string boundarySource = """
public sealed class ConfiguredConstructorBoundary
{
    public ConfiguredConstructorBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("ConfiguredConstructorBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            var methodIdentity = GetMethodIdentity(
                fixture.AssemblyPath,
                "ConfiguredConstructorBoundary..ctor(int)");

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public ConfiguredConstructorBoundary TestMethod(int value)
    {
        return new ConfiguredConstructorBoundary(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ConfiguredConstructorBoundary..ctor(int)",
                            "pure",
                            """[]""",
                            methodIdentity.Symbol))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    string.Join(
                        ";",
                        methodIdentity.Symbol,
                        "ConfiguredConstructorBoundary..ctor",
                        "ConfiguredConstructorBoundary.ConfiguredConstructorBoundary(int)")));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureConstructorClassification_ReportsImpurity()
        {
            const string boundarySource = """
public sealed class ImpureConstructorBoundary
{
    private static int _state;

    public ImpureConstructorBoundary(int value)
    {
        _state += value;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureConstructorBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public ImpureConstructorBoundary TestMethod(int value)
    {
        return new ImpureConstructorBoundary(value);
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ImpureConstructorBoundary..ctor(int)",
                            "impure",
                            """[ "global_state_write" ]""",
                            "ImpureConstructorBoundary.ImpureConstructorBoundary(int)"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_OverridesReviewedPureStringBuilderConstructor_WhenGeneratedSummaryIsImpure()
        {
            var identity = GetAssemblyIdentity(typeof(System.Text.StringBuilder).Assembly.Location);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;
using System.Text;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public StringBuilder TestMethod()
    {
        return new StringBuilder();
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "System.Text.StringBuilder..ctor()",
                            "impure",
                            """[ "global_state_write" ]""",
                            "System.Text.StringBuilder.StringBuilder()"))
                });

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureGetterClassification_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public sealed class PureGetterBoundary
{
    public PureGetterBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureGetterBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod(PureGetterBoundary value)
    {
        return value.Value;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "PureGetterBoundary.get_Value()",
                            "pure",
                            """[]""",
                            "PureGetterBoundary.Value.get"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureGetterClassification_ReportsImpurity()
        {
            const string boundarySource = """
public sealed class ImpureGetterBoundary
{
    private static int _state;

    public int Value
    {
        get
        {
            _state++;
            return _state;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureGetterBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ImpureGetterBoundary value)
    {
        return value.Value;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ImpureGetterBoundary.get_Value()",
                            "impure",
                            """[ "global_state_write" ]""",
                            "ImpureGetterBoundary.Value.get"))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_OverridesKnownImpureThreadCurrentThreadProperty_WhenGeneratedSummaryIsPure()
        {
            var identity = GetAssemblyIdentity(typeof(System.Threading.Thread).Assembly.Location);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;
using System.Threading;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public Thread TestMethod()
    {
        return Thread.CurrentThread;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "System.Threading.Thread.get_CurrentThread()",
                            "pure",
                            """[]"""))
                });

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated getter purity should override the built-in known-impure property fallback for metadata properties.");
        }

        [Test]
        public async Task Ps0002_EffectSummary_ConfigKnownImpureInterpolationToString_OverridesGeneratedPureFormattingClassification()
        {
            const string boundarySource = """
public sealed class ConfiguredFormattingBoundary
{
    public override string ToString()
    {
        return "ok";
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("ConfiguredFormattingBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);
            var methodIdentity = GetMethodIdentity(
                fixture.AssemblyPath,
                "ConfiguredFormattingBoundary.ToString()");

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public string TestMethod(ConfiguredFormattingBoundary value)
    {
        return $"{value}";
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ConfiguredFormattingBoundary.ToString()",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    string.Join(
                        ";",
                        methodIdentity.Symbol,
                        "ConfiguredFormattingBoundary.ToString",
                        "ConfiguredFormattingBoundary.ToString()")));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureListPatternMembers_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public sealed class GeneratedListPatternBoundary
{
    public int Length => 2;

    public int this[int index] => index;
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureListPatternBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(GeneratedListPatternBoundary value)
    {
        return value is [0, 1];
    }
}
""",
                new[]
                {
                    ("length.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "GeneratedListPatternBoundary.get_Length()",
                            "pure",
                            """[]""")),
                    ("indexer.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "GeneratedListPatternBoundary.get_Item(int)",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureListPatternLength_ReportsImpurity()
        {
            const string boundarySource = """
public sealed class GeneratedListPatternBoundary
{
    public int Length => 2;

    public int this[int index] => index;
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureListPatternBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(GeneratedListPatternBoundary value)
    {
        return value is [0, 1];
    }
}
""",
                new[]
                {
                    ("length.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "GeneratedListPatternBoundary.get_Length()",
                            "impure",
                            """[ "global_state_write" ]""")),
                    ("indexer.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "GeneratedListPatternBoundary.get_Item(int)",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureBinaryOperatorClassification_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public readonly struct PureOperatorBoundary
{
    public PureOperatorBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static PureOperatorBoundary operator +(PureOperatorBoundary left, PureOperatorBoundary right)
    {
        return new PureOperatorBoundary(left.Value + right.Value);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureOperatorBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public PureOperatorBoundary TestMethod(PureOperatorBoundary left, PureOperatorBoundary right)
    {
        return left + right;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "PureOperatorBoundary.op_Addition(PureOperatorBoundary, PureOperatorBoundary)",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureBinaryOperatorClassification_ReportsImpurity()
        {
            const string boundarySource = """
public struct ImpureOperatorBoundary
{
    private static int _state;

    public ImpureOperatorBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static ImpureOperatorBoundary operator +(ImpureOperatorBoundary left, ImpureOperatorBoundary right)
    {
        _state++;
        return new ImpureOperatorBoundary(left.Value + right.Value + _state);
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureOperatorBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public ImpureOperatorBoundary TestMethod(ImpureOperatorBoundary left, ImpureOperatorBoundary right)
    {
        return left + right;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ImpureOperatorBoundary.op_Addition(ImpureOperatorBoundary, ImpureOperatorBoundary)",
                            "impure",
                            """[ "global_state_write" ]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedPureConversionClassification_SuppressesUnknownExternalCall()
        {
            const string boundarySource = """
public readonly struct PureConversionBoundary
{
    public PureConversionBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static explicit operator int(PureConversionBoundary value)
    {
        return value.Value;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureConversionBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod(PureConversionBoundary value)
    {
        return (int)value;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "PureConversionBoundary.op_Explicit(PureConversionBoundary)",
                            "pure",
                            """[]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_EffectSummary_WithTrustedGeneratedImpureConversionClassification_ReportsImpurity()
        {
            const string boundarySource = """
public struct ImpureConversionBoundary
{
    private static int _state;

    public ImpureConversionBoundary(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static explicit operator int(ImpureConversionBoundary value)
    {
        _state++;
        return value.Value + _state;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("GeneratedImpureConversionBoundary", boundarySource);
            var identity = GetAssemblyIdentity(fixture.AssemblyPath);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                """
using System;

public sealed class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ImpureConversionBoundary value)
    {
        return (int)value;
    }
}
""",
                new[]
                {
                    ("PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            identity,
                            "ImpureConversionBoundary.op_Explicit(ImpureConversionBoundary)",
                            "impure",
                            """[ "global_state_write" ]"""))
                },
                ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
        }

        [Test]
        public void ExceptionSummaryCatalog_RepeatedMetadataQueriesReuseAssemblyIdentityCache()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var compilation = CreateLibraryCallCompilation();
            var methodSymbol = compilation.GetTypeByMetadataName(typeof(ArgumentNullException).FullName!)!
                .GetMembers("ThrowIfNull")
                .OfType<IMethodSymbol>()
                .Single(method =>
                    method.Parameters.Length == 2 &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
                    method.Parameters[1].Type.SpecialType == SpecialType.System_String);

            var analyzerOptions = new AnalyzerOptions(
                ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"))),
                new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty));

            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.ExceptionSummaryCatalog", throwOnError: true)!;
            var fromOptionsMethod = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetExceptionsMethod = catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == "TryGetExceptions" &&
                    method.GetParameters().Length == 3 &&
                    method.GetParameters()[1].ParameterType == typeof(Compilation));
            var assemblyIdentityCacheField = catalogType.GetField("AssemblyIdentityCache", BindingFlags.Static | BindingFlags.NonPublic)!;
            var assemblyIdentityCache = assemblyIdentityCacheField.GetValue(null)!;
            assemblyIdentityCache.GetType().GetMethod("Clear")!.Invoke(assemblyIdentityCache, null);

            try
            {
                var catalog = fromOptionsMethod.Invoke(null, new object?[] { analyzerOptions, default(System.Threading.CancellationToken) })!;

                var firstArgs = new object?[] { methodSymbol, compilation, null };
                Assert.That((bool)tryGetExceptionsMethod.Invoke(catalog, firstArgs)!, Is.True);
                var firstExceptions = (ImmutableArray<string>)firstArgs[2]!;

                Assert.That(firstExceptions.ToArray(), Is.EqualTo(new[] { "System.ArgumentNullException" }));
                Assert.That(GetCount(assemblyIdentityCache), Is.EqualTo(1));

                var secondArgs = new object?[] { methodSymbol, compilation, null };
                Assert.That((bool)tryGetExceptionsMethod.Invoke(catalog, secondArgs)!, Is.True);
                var secondExceptions = (ImmutableArray<string>)secondArgs[2]!;

                Assert.That(secondExceptions.ToArray(), Is.EqualTo(new[] { "System.ArgumentNullException" }));
                Assert.That(GetCount(assemblyIdentityCache), Is.EqualTo(1));
            }
            finally
            {
                assemblyIdentityCache.GetType().GetMethod("Clear")!.Invoke(assemblyIdentityCache, null);
            }
        }

        private static string CreateLibraryCallSource()
        {
            return """
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(value));
    }
}
""";
        }

        private static CSharpCompilation CreateLibraryCallCompilation()
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(CreateLibraryCallSource(), new CSharpParseOptions(LanguageVersion.Preview));
            return CSharpCompilation.Create(
                "ExceptionSummaryCatalogValidationTests",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string CreateEffectSummaryJson(
            AssemblyIdentity assemblyIdentity,
            string symbol,
            string? assemblySha256 = null,
            string? moduleVersionId = null,
            string? metadataToken = null,
            string? methodBodySha256 = null,
            string? actualMethodLookupSymbol = null,
            string? thrownExceptionTypesJson = null,
            string? transitiveThrownExceptionTypesJson = null,
            string? thrownExceptionSourcePathsJson = null,
            string? transitiveThrownExceptionSourcePathsJson = null,
            string? thrownExceptionEdgesJson = null,
            string? transitiveThrownExceptionEdgesJson = null)
        {
            var methodIdentity = GetMethodIdentity(assemblyIdentity.AssemblyPath, actualMethodLookupSymbol ?? symbol);
            thrownExceptionTypesJson ??= "[]";
            transitiveThrownExceptionTypesJson ??= """[ "System.ArgumentNullException" ]""";
            thrownExceptionSourcePathsJson ??= "[]";
            transitiveThrownExceptionSourcePathsJson ??= "[]";
            thrownExceptionEdgesJson ??= "[]";
            transitiveThrownExceptionEdgesJson ??= "[]";
            assemblySha256 ??= assemblyIdentity.AssemblySha256;
            moduleVersionId ??= assemblyIdentity.ModuleVersionId;
            metadataToken ??= methodIdentity.MetadataToken;
            methodBodySha256 ??= methodIdentity.MethodBodySha256;
            var methodBodySha256Json = methodBodySha256 == null ? "null" : "\"" + methodBodySha256 + "\"";
            return $$"""
{
  "SchemaVersion": 1,
  "Assemblies": [
    {
      "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
      "AssemblyPath": "runtime",
      "AssemblySha256": "{{assemblySha256}}",
      "ModuleVersionId": "{{moduleVersionId}}",
      "MethodCount": 1,
      "EmittedMethodCount": 1,
      "Methods": [
        {
          "Symbol": "{{symbol}}",
          "MetadataToken": "{{metadataToken}}",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": {{methodBodySha256Json}},
          "CacheKey": "validation-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": {{thrownExceptionTypesJson}},
          "TransitiveThrownExceptionTypes": {{transitiveThrownExceptionTypesJson}},
          "ThrownExceptionSourcePaths": {{thrownExceptionSourcePathsJson}},
          "TransitiveThrownExceptionSourcePaths": {{transitiveThrownExceptionSourcePathsJson}},
          "ThrownExceptionEdges": {{thrownExceptionEdgesJson}},
          "TransitiveThrownExceptionEdges": {{transitiveThrownExceptionEdgesJson}},
          "Calls": [],
          "Fields": []
        }
      ]
    }
  ]
}
""";
        }

        private static void AssertMatchingExceptionDiagnostics(
            ImmutableArray<Diagnostic> expectedDiagnostics,
            ImmutableArray<Diagnostic> actualDiagnostics,
            string diagnosticId)
        {
            var expected = expectedDiagnostics.Single(d => d.Id == diagnosticId);
            var actual = actualDiagnostics.Single(d => d.Id == diagnosticId);

            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionTypesProperty]));
            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty]));
            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty]));

            var expectedHasSymbol = expected.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionSymbolProperty, out var expectedSymbol);
            var actualHasSymbol = actual.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionSymbolProperty, out var actualSymbol);
            Assert.That(actualHasSymbol, Is.EqualTo(expectedHasSymbol));
            if (expectedHasSymbol)
            {
                Assert.That(actualSymbol, Is.EqualTo(expectedSymbol));
            }

            var expectedHasEdges = expected.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var expectedEdges);
            var actualHasEdges = actual.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var actualEdges);
            Assert.That(actualHasEdges, Is.EqualTo(expectedHasEdges));
            if (expectedHasEdges)
            {
                Assert.That(actualEdges, Is.EqualTo(expectedEdges));
            }
        }

        private static string CreateMalformedEffectSummaryJson(string assemblyName, string assemblySha256, string moduleVersionId)
        {
            return $$"""
{
  "SchemaVersion": 1,
  "Assemblies": [
    {
      "AssemblyName": "{{assemblyName}}",
      "AssemblyPath": "runtime",
      "AssemblySha256": "{{assemblySha256}}",
      "ModuleVersionId": "{{moduleVersionId}}",
      "MethodCount": 1,
      "EmittedMethodCount": 1,
      "Methods": [
        {
          "Symbol": "System.ArgumentNullException.ThrowIfNull(object, string)",
          "MetadataToken": "0x06000001",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": null,
          "CacheKey": "validation-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": "System.ArgumentNullException",
          "TransitiveThrownExceptionTypes": [],
          "Calls": [],
          "Fields": []
        }
      ]
    }
  ]
}
""";
        }

        private static string CreatePuritySummaryJson(
            AssemblyIdentity assemblyIdentity,
            string actualMethodLookupSymbol,
            string classification,
            string categoriesJson,
            string? symbolOverride = null)
        {
            var methodIdentity = GetMethodIdentity(assemblyIdentity.AssemblyPath, actualMethodLookupSymbol);
            var symbol = symbolOverride ?? actualMethodLookupSymbol;

            return $$"""
{
  "SchemaVersion": 2,
  "GeneratedPurityCatalog": {
    "SchemaVersion": 1,
    "Entries": [
      {
        "Symbol": "{{symbol}}",
        "ExactSymbolKey": "{{methodIdentity.ExactSymbolKey}}",
        "CacheKey": "validation-test",
        "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
        "AssemblyPath": "{{assemblyIdentity.AssemblyPath.Replace("\\", "\\\\")}}",
        "AssemblySha256": "{{assemblyIdentity.AssemblySha256}}",
        "ModuleVersionId": "{{assemblyIdentity.ModuleVersionId}}",
        "MetadataToken": "{{methodIdentity.MetadataToken}}",
        "MethodBodySha256": {{FormatJsonStringOrNull(methodIdentity.MethodBodySha256)}},
        "Classification": "{{classification}}",
        "Categories": {{categoriesJson}},
        "FirstBlockingCallChain": [],
        "HasFreshArrayAllocationEvidence": false,
        "HasFreshObjectAllocationEvidence": false,
        "HasUnsupportedEffects": false,
        "FreshnessClassification": "none"
      }
    ]
  },
  "Assemblies": [
    {
      "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
      "AssemblyPath": "{{assemblyIdentity.AssemblyPath.Replace("\\", "\\\\")}}",
      "AssemblySha256": "{{assemblyIdentity.AssemblySha256}}",
      "ModuleVersionId": "{{assemblyIdentity.ModuleVersionId}}",
      "MethodCount": 1,
      "EmittedMethodCount": 1,
      "Methods": [
        {
          "Symbol": "{{symbol}}",
          "ExactSymbolKey": "{{methodIdentity.ExactSymbolKey}}",
          "MetadataToken": "{{methodIdentity.MetadataToken}}",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": {{FormatJsonStringOrNull(methodIdentity.MethodBodySha256)}},
          "CacheKey": "validation-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": [],
          "TransitiveThrownExceptionTypes": [],
          "Calls": [],
          "Fields": [],
          "PurityClassification": {
            "Classification": "{{classification}}",
            "Categories": {{categoriesJson}},
            "FirstBlockingCallChain": [],
            "HasFreshArrayAllocationEvidence": false,
            "HasFreshObjectAllocationEvidence": false,
            "HasUnsupportedEffects": false,
            "FreshnessClassification": "none"
          }
        }
      ]
    }
  ]
}
""";
        }

        private static MethodIdentity GetMethodIdentity(string assemblyPath, string symbol)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var handle in metadataReader.MethodDefinitions)
            {
                var methodSymbol = GetMethodSymbol(metadataReader, handle);
                if (!string.Equals(methodSymbol, symbol, StringComparison.Ordinal))
                {
                    continue;
                }

                var definition = metadataReader.GetMethodDefinition(handle);
                string? methodBodySha256 = null;
                if (definition.RelativeVirtualAddress != 0)
                {
                    var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
                    var il = body.GetILBytes();
                    if (il != null)
                    {
                        using var sha256 = SHA256.Create();
                        methodBodySha256 = Convert.ToHexString(sha256.ComputeHash(il)).ToLowerInvariant();
                    }
                }

                return new MethodIdentity(
                    $"0x{MetadataTokens.GetToken(handle):X8}",
                    methodBodySha256,
                    GetMethodExactSymbolKey(metadataReader, handle),
                    methodSymbol);
            }

            throw new AssertionException("Method symbol did not resolve in assembly: " + symbol);
        }

        private static AssemblyIdentity GetAssemblyIdentity(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var assemblyName = metadataReader.IsAssembly
                ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name)
                : Path.GetFileNameWithoutExtension(assemblyPath);
            var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid).ToString("D");
            stream.Position = 0;
            using var sha256 = SHA256.Create();
            var assemblySha256 = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();

            return new AssemblyIdentity(assemblyPath, assemblyName, assemblySha256, moduleVersionId);
        }

        private static string GetMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
        }

        private static string GetMethodExactSymbolKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeExactMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
        }

        private static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            if (handle.IsNil)
            {
                return "<module>";
            }

            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
            {
                return GetTypeName(reader, declaringType) + "+" + name;
            }

            var ns = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            var ns = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string DecodeMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string DecodeExactMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string NormalizeExactTypeName(string typeName)
        {
            return typeName switch
            {
                "System.Boolean" => "bool",
                "System.Byte" => "byte",
                "System.Char" => "char",
                "System.Decimal" => "decimal",
                "System.Double" => "double",
                "System.Int16" => "short",
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.IntPtr" => "nint",
                "System.Object" => "object",
                "System.SByte" => "sbyte",
                "System.Single" => "float",
                "System.String" => "string",
                "System.UInt16" => "ushort",
                "System.UInt32" => "uint",
                "System.UInt64" => "ulong",
                "System.UIntPtr" => "nuint",
                "System.Void" => "void",
                _ => typeName
            };
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            string effectSummaryJson,
            ImmutableArray<MetadataReference> additionalReferences,
            string additionalFilePath = "PurelySharp.EffectSummary.json")
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                new[] { (additionalFilePath, effectSummaryJson) },
                additionalReferences);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            string effectSummaryJson,
            string additionalFilePath = "PurelySharp.EffectSummary.json")
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryJson,
                ImmutableArray<MetadataReference>.Empty,
                additionalFilePath);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            params (string Path, string Text)[] effectSummaryFiles)
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryFiles,
                ImmutableArray<MetadataReference>.Empty);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            (string Path, string Text)[] effectSummaryFiles,
            ImmutableArray<MetadataReference> additionalReferences)
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryFiles,
                additionalReferences,
                null);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            (string Path, string Text)[] effectSummaryFiles,
            ImmutableArray<MetadataReference> additionalReferences,
            ImmutableDictionary<string, string>? globalOptions)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ExceptionSummaryCatalogValidationTests",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().AddRange(additionalReferences),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
            if (!analyzerGlobalOptions.ContainsKey("purelysharp_report_exceptions"))
            {
                analyzerGlobalOptions = analyzerGlobalOptions.Add(
                    "purelysharp_report_exceptions",
                    "true");
            }

            var analyzerOptions = new AnalyzerOptions(
                effectSummaryFiles
                    .Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))
                    .ToImmutableArray(),
                new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static async Task<FixtureAssembly> CreateFixtureAssemblyAsync(string assemblyName, string source)
        {
            var tempDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "exception-summary-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            await using var stream = File.Create(assemblyPath);
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            }

            return new FixtureAssembly(tempDirectory, assemblyPath);
        }

        private static async Task<string> RunEffectSummaryJsonAsync(string assemblyPath, bool includeTransitiveRoots)
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

            return await File.ReadAllTextAsync(outputPath);
        }

        private static async Task<string> RunFilteredEffectSummaryJsonAsync(
            string assemblyPath,
            bool includeTransitiveRoots,
            int maxDepth,
            params string[] symbolPrefixes)
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            foreach (var symbolPrefix in symbolPrefixes)
            {
                startInfo.ArgumentList.Add("--symbol-prefix");
                startInfo.ArgumentList.Add(symbolPrefix);
            }
            startInfo.ArgumentList.Add("--include-callees");
            startInfo.ArgumentList.Add("--max-depth");
            startInfo.ArgumentList.Add(maxDepth.ToString());
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

            return await File.ReadAllTextAsync(outputPath);
        }

        private static string GetEffectSummaryToolDllPath()
        {
            lock (EffectSummaryToolBuildLock)
            {
                if (!string.IsNullOrWhiteSpace(s_effectSummaryToolDllPath) && File.Exists(s_effectSummaryToolDllPath))
                {
                    return s_effectSummaryToolDllPath;
                }

                var repositoryRoot = GetRepositoryRoot();
                var projectPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.EffectSummary", "PurelySharp.EffectSummary.csproj");
                var dllPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.EffectSummary", "bin", "Debug", "net8.0", "PurelySharp.EffectSummary.dll");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("build");
                startInfo.ArgumentList.Add(projectPath);
                startInfo.ArgumentList.Add("-m:20");
                startInfo.ArgumentList.Add("--no-restore");

                using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to build effect summary tool.");
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(dllPath))
                {
                    throw new AssertionException(
                        "Effect summary tool build failed." + Environment.NewLine +
                        standardOutput + Environment.NewLine +
                        standardError);
                }

                s_effectSummaryToolDllPath = dllPath;
                return s_effectSummaryToolDllPath;
            }
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private static void AssertEffectSummaryException(
            ImmutableArray<Diagnostic> diagnostics,
            string methodName,
            string exceptionType)
        {
            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .Single(d => d.GetMessage().Contains("'" + methodName + "'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        private sealed class EffectSummaryTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            private readonly MetadataReader _reader;

            public EffectSummaryTypeNameProvider(MetadataReader reader)
            {
                _reader = reader;
            }

            public string GetArrayType(string elementType, ArrayShape shape)
            {
                var rank = Math.Max(shape.Rank, 1);
                return elementType + "[" + new string(',', rank - 1) + "]";
            }

            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Byte => "byte",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.Double => "double",
                PrimitiveTypeCode.Int16 => "short",
                PrimitiveTypeCode.Int32 => "int",
                PrimitiveTypeCode.Int64 => "long",
                PrimitiveTypeCode.IntPtr => "nint",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.SByte => "sbyte",
                PrimitiveTypeCode.Single => "float",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.TypedReference => "typedref",
                PrimitiveTypeCode.UInt16 => "ushort",
                PrimitiveTypeCode.UInt32 => "uint",
                PrimitiveTypeCode.UInt64 => "ulong",
                PrimitiveTypeCode.UIntPtr => "nuint",
                PrimitiveTypeCode.Void => "void",
                _ => typeCode.ToString(),
            };
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
                => NormalizeExactTypeName(GetTypeName(metadataReader, handle));
            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => NormalizeExactTypeName(GetTypeReferenceName(metadataReader, handle));
            public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        private sealed record AssemblyIdentity(string AssemblyPath, string AssemblyName, string AssemblySha256, string ModuleVersionId);
        private sealed record MethodIdentity(string MetadataToken, string? MethodBodySha256, string ExactSymbolKey, string Symbol);

        private static string FormatJsonStringOrNull(string? value)
        {
            return value == null ? "null" : "\"" + value + "\"";
        }

        private static int GetCount(object instance)
        {
            return (int)instance.GetType().GetProperty("Count")!.GetValue(instance)!;
        }

        private sealed class InMemoryAdditionalText : AdditionalText
        {
            private readonly SourceText _text;

            public InMemoryAdditionalText(string path, string text)
            {
                Path = path;
                _text = SourceText.From(text);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            {
                return _text;
            }
        }

        private sealed class FixtureAssembly : IAsyncDisposable
        {
            public FixtureAssembly(string directoryPath, string assemblyPath)
            {
                DirectoryPath = directoryPath;
                AssemblyPath = assemblyPath;
            }

            public string DirectoryPath { get; }

            public string AssemblyPath { get; }

            public ValueTask DisposeAsync()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return ValueTask.CompletedTask;
            }
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
