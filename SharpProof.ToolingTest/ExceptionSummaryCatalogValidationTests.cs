using System.Collections;
using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.TestReflectionFacts;

namespace SharpProof.Test;

[TestFixture]
[Explicit("Effect summary JSON ingestion is dormant during active analyzer development.")]
public partial class ExceptionSummaryCatalogValidationTests
{
    [Test]
    public async Task Sp0010_EffectSummary_WithMatchingAssemblyIdentity_IsTrusted()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)"));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.ArgumentNullException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_WhenEffectSummaryJsonDisabled_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsAsync(
            CreateLibraryCallSource(),
            new[]
            {
                ("SharpProof.EffectSummary.json",
                    CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"))
            },
            ImmutableArray<MetadataReference>.Empty,
            ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_enable_effect_summary_json",
                "false"));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_DefaultOff_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            CreateLibraryCallSource(),
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(
                    "SharpProof.EffectSummary.json",
                    CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"))));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithMismatchedAssemblyIdentity_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                "0000000000000000000000000000000000000000000000000000000000000000",
                "00000000-0000-0000-0000-000000000000"));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithIncompleteAssemblyIdentity_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                string.Empty,
                coreLib.ModuleVersionId));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithMismatchedMetadataToken_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                metadataToken: "0x06000001"));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithMismatchedMethodBodyHash_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var methodIdentity = GetMethodIdentity(coreLib.AssemblyPath,
            "System.ArgumentNullException.ThrowIfNull(object, string)");
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                metadataToken: methodIdentity.MetadataToken,
                methodBodySha256: new string('0', methodIdentity.MethodBodySha256!.Length)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithSuffixedSummaryFileName_IsTrusted()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            CreateLibraryCallSource(),
            CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"),
            "runtime.SharpProof.EffectSummary.json");

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.ArgumentNullException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_MergesDirectAndTransitiveExceptionTypes()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                transitiveThrownExceptionTypesJson: """[ "System.ArgumentNullException" ]"""));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithMalformedMethodEntry_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            CreateLibraryCallSource(),
            CreateMalformedEffectSummaryJson(coreLib.AssemblyName, coreLib.AssemblySha256, coreLib.ModuleVersionId));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_WithWrongSymbol_IsIgnored()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            CreateLibraryCallSource(),
            CreateEffectSummaryJson(
                coreLib,
                "System.ArgumentNullException.ThrowIfNull(object)",
                actualMethodLookupSymbol: "System.ArgumentNullException.ThrowIfNull(object, string)"));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_MergesAcrossMultipleSummaryFiles()
    {
        var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            CreateLibraryCallSource(),
            ("SharpProof.EffectSummary.json",
                CreateEffectSummaryJson(
                    coreLib,
                    "System.ArgumentNullException.ThrowIfNull(object, string)",
                    thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                    transitiveThrownExceptionTypesJson: "[]")),
            ("runtime.SharpProof.EffectSummary.json",
                CreateEffectSummaryJson(
                    coreLib,
                    "System.ArgumentNullException.ThrowIfNull(object, string)",
                    thrownExceptionTypesJson: "[]",
                    transitiveThrownExceptionTypesJson: """[ "System.ArgumentNullException" ]""")));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_GenericMetadataMethodSummary_MatchesConstructedCall()
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
            AnalyzerTestHost.GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var boundaryType = boundaryCompilation.GetTypeByMetadataName("GenericBoundary")!;
        var methodSymbol = boundaryType.GetMembers("EchoOrThrow").OfType<IMethodSymbol>().Single();
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    CreateEffectSummaryJson(
                        identity,
                        methodSymbol.OriginalDefinition.ToDisplayString(),
                        actualMethodLookupSymbol: "GenericBoundary.EchoOrThrow(!!0)",
                        thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                        transitiveThrownExceptionTypesJson: "[]"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_MetadataConstructorSummary_MatchesCall()
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
            AnalyzerTestHost.GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var boundaryType = boundaryCompilation.GetTypeByMetadataName("ConstructorBoundary")!;
        var constructorSymbol = boundaryType.InstanceConstructors.Single(ctor => ctor.Parameters.Length == 1);
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    CreateEffectSummaryJson(
                        identity,
                        constructorSymbol.OriginalDefinition.ToDisplayString(),
                        actualMethodLookupSymbol: "ConstructorBoundary..ctor(string)",
                        thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                        transitiveThrownExceptionTypesJson: "[]"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_MetadataPropertyGetterSummary_MatchesCall()
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
            AnalyzerTestHost.GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var boundaryType = boundaryCompilation.GetTypeByMetadataName("PropertyBoundary")!;
        var getterSymbol = boundaryType.GetMembers("DangerousValue")
            .OfType<IPropertySymbol>()
            .Single()
            .GetMethod!;
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    CreateEffectSummaryJson(
                        identity,
                        getterSymbol.OriginalDefinition.ToDisplayString(),
                        actualMethodLookupSymbol: "PropertyBoundary.get_DangerousValue()",
                        thrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                        transitiveThrownExceptionTypesJson: "[]"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_MetadataBinaryOperatorSummary_MatchesCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
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
    public async Task Sp0010_EffectSummary_MetadataOutParameterSummary_MatchesCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
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
    public async Task Sp0010_EffectSummary_ToolOutput_PropagatesTransitiveMetadataMethodException()
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
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_FilteredToolOutput_PreservesDeepMetadataSourceChain()
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
        var references =
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));

        var summaryJson = await RunFilteredEffectSummaryJsonAsync(
            fixture.AssemblyPath,
            true,
            1,
            "SummaryBoundary.Outer");
        var diagnostics =
            await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(callerSource, summaryJson, references);

        var expectedSourceChain =
            "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

        var summaryDiagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));

        var siteDiagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
        Assert.That(siteDiagnostic.GetMessage(), Does.Contain("SummaryBoundary.Outer"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSymbolProperty],
            Does.Contain("SummaryBoundary.Outer"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_TransitiveThrownExceptionEdges_RoundTripsWithoutChangingDiagnostics()
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
        var references =
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);
        const string expectedSourceChain =
            "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            callerSource,
            CreateEffectSummaryJson(
                identity,
                "SummaryBoundary.Outer(string)",
                thrownExceptionTypesJson: "[]",
                transitiveThrownExceptionTypesJson: "[]",
                transitiveThrownExceptionEdgesJson:
                $$"""[ { "ExceptionType": "System.InvalidOperationException", "SourcePath": "{{expectedSourceChain}}", "Depth": 3 } ]"""),
            references);

        var summaryDiagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId);
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));

        var siteDiagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId);
        Assert.That(siteDiagnostic.GetMessage(), Does.Contain("SummaryBoundary.Outer"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Does.Contain("System.InvalidOperationException=effect_summary:" + expectedSourceChain));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSymbolProperty],
            Does.Contain("SummaryBoundary.Outer"));
    }

    [Test]
    public async Task
        Sp0010_EffectSummary_TransitiveThrownExceptionEdges_WithSchemaFields_MatchLegacyDiagnosticProperties()
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
        var references =
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);
        const string expectedSourceChain =
            "SummaryBoundary.Outer(string) -> SummaryBoundary.Middle(string) -> SummaryBoundary.Inner(string) -> SummaryBoundary.Leaf(string)";

        var legacyDiagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            callerSource,
            CreateEffectSummaryJson(
                identity,
                "SummaryBoundary.Outer(string)",
                thrownExceptionTypesJson: "[]",
                transitiveThrownExceptionTypesJson: """[ "System.InvalidOperationException" ]""",
                transitiveThrownExceptionSourcePathsJson:
                $$"""[ { "ExceptionType": "System.InvalidOperationException", "SourcePath": "{{expectedSourceChain}}" } ]"""),
            references);

        var edgeDiagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        AssertMatchingExceptionDiagnostics(legacyDiagnostics, edgeDiagnostics,
            SharpProofDiagnostics.ExceptionSummaryId);
        AssertMatchingExceptionDiagnostics(legacyDiagnostics, edgeDiagnostics,
            SharpProofDiagnostics.UncaughtExceptionSiteId);
    }

    [Test]
    public async Task Sp0010_EffectSummary_TransitiveThrownExceptionEdges_WithoutExceptionType_AreIgnored()
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
        var references =
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath));
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_PropagatesCommonMetadataExceptions()
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
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
    public async Task Sp0010_EffectSummary_ToolOutput_DoesNotReportLocallyCaughtMetadataThrow()
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
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_DoesNotReportMetadataThrowCaughtByTrueFilter()
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
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_CatchFilterContradiction_DoesNotSuppressMetadataThrow()
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

        await using var fixture =
            await CreateFixtureAssemblyAsync("SummaryBoundaryContradictoryFilter", boundarySource);
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            """
            using System;

            public class TestClass
            {
                public int TestMethod(int x)
                {
                    try
                    {
                        return SummaryBoundary.ThrowFormat();
                    }
                    catch (FormatException) when (x != x)
                    {
                        return 1;
                    }
                }
            }
            """,
            summaryJson,
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        AssertEffectSummaryException(diagnostics, "TestMethod", "System.FormatException");
        var siteDiagnostic = diagnostics.Single(d =>
            d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId &&
            d.GetMessage().Contains("SummaryBoundary.ThrowFormat()", StringComparison.Ordinal));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.FormatException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_PropagatesMetadataRethrow()
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
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
    public async Task Sp0010_EffectSummary_ToolOutput_SuppressesMetadataRethrowCaughtByOuterHandler()
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

        await using var fixture =
            await CreateFixtureAssemblyAsync("SummaryBoundaryCaughtRethrowException", boundarySource);
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);
        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.False);
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_FinallyThrow_ShadowsEarlierDirectMetadataThrow()
    {
        const string boundarySource = """
                                      using System;

                                      public static class SummaryBoundary
                                      {
                                          public static void DirectThrowShadowedByFinally()
                                          {
                                              try
                                              {
                                                  throw new InvalidOperationException();
                                              }
                                              finally
                                              {
                                                  throw new FormatException();
                                              }
                                          }
                                      }
                                      """;

        await using var fixture =
            await CreateFixtureAssemblyAsync("SummaryBoundaryFinallyShadowedDirect", boundarySource);
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            """
            public class TestClass
            {
                public void TestMethod()
                {
                    SummaryBoundary.DirectThrowShadowedByFinally();
                }
            }
            """,
            summaryJson,
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        AssertEffectSummaryException(diagnostics, "TestMethod", "System.FormatException");
    }

    [Test]
    public async Task Sp0010_EffectSummary_ToolOutput_FinallyThrow_ShadowsEarlierTransitiveMetadataThrow()
    {
        const string boundarySource = """
                                      using System;

                                      public static class SummaryBoundary
                                      {
                                          private static void ThrowDirect()
                                          {
                                              throw new InvalidOperationException();
                                          }

                                          public static void TransitiveCallShadowedByFinally()
                                          {
                                              try
                                              {
                                                  ThrowDirect();
                                              }
                                              finally
                                              {
                                                  throw new FormatException();
                                              }
                                          }
                                      }
                                      """;

        await using var fixture =
            await CreateFixtureAssemblyAsync("SummaryBoundaryFinallyShadowedTransitive", boundarySource);
        var summaryJson = await RunEffectSummaryJsonAsync(fixture.AssemblyPath, true);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            """
            public class TestClass
            {
                public void TestMethod()
                {
                    SummaryBoundary.TransitiveCallShadowedByFinally();
                }
            }
            """,
            summaryJson,
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        AssertEffectSummaryException(diagnostics, "TestMethod", "System.FormatException");
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedPureClassification_SuppressesUnknownExternalCall()
    {
        const string boundarySource = """
                                      public static class PureBoundary
                                      {
                                          public static int Double(int value) => value * 2;
                                      }
                                      """;

        await using var fixture = await CreateFixtureAssemblyAsync("GeneratedPureBoundary", boundarySource);
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "PureBoundary.Double(int)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureClassification_ReportsImpurity()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureBoundary.Next()",
                        "impure",
                        """[ "global_state_write" ]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_WithTrustedGeneratedImpureClassification_AffineContradictoryGuard_SuppressesImpurity()
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

        await using var fixture =
            await CreateFixtureAssemblyAsync("GeneratedImpureBoundaryAffineGuard", boundarySource);
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            """
            using System;

            public sealed class EnforcePureAttribute : Attribute { }

            public class TestClass
            {
                [EnforcePure]
                public int TestMethod(int x)
                {
                    if (x + 1 <= 0 && x >= 0)
                    {
                        return ImpureBoundary.Next();
                    }

                    return 0;
                }
            }
            """,
            new[]
            {
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureBoundary.Next()",
                        "impure",
                        """[ "global_state_write" ]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(
            diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False,
            "Contradictory affine guards should suppress generated-summary impurity diagnostics.");
    }

    [Test]
    public async Task Sp0002_EffectSummary_OverridesKnownPureWebUtilityClassification_WhenGeneratedSummaryIsImpure()
    {
        var identity = GetAssemblyIdentity(typeof(WebUtility).Assembly.Location);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
            """, ("SharpProof.EffectSummary.json",
                GeneratedPurityTestSupport.CreatePuritySummaryJson(
                    identity.AssemblyPath,
                    "System.Net.WebUtility.HtmlEncode(string)",
                    "impure",
                    """[ "global_state_write" ]""")));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_OverridesKnownImpureEnvironmentMethod_WhenGeneratedSummaryIsPure()
    {
        var identity = GetAssemblyIdentity(typeof(Environment).Assembly.Location);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
            """, ("SharpProof.EffectSummary.json",
                GeneratedPurityTestSupport.CreatePuritySummaryJson(
                    identity.AssemblyPath,
                    "System.Environment.GetEnvironmentVariable(string)",
                    "pure",
                    """[]""")));

        Assert.That(
            diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False,
            "Trusted generated purity should override the built-in known-impure member fallback for metadata methods.");
    }

    [Test]
    public async Task Sp0002_EffectSummary_WhenEffectSummaryJsonDisabled_DoesNotOverrideKnownImpureEnvironmentMethod()
    {
        var identity = GetAssemblyIdentity(typeof(Environment).Assembly.Location);

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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "System.Environment.GetEnvironmentVariable(string)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray<MetadataReference>.Empty,
            ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_enable_effect_summary_json",
                "false"));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.Not.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_DefaultOff_DoesNotOverrideKnownImpureEnvironmentMethod()
    {
        var identity = GetAssemblyIdentity(typeof(Environment).Assembly.Location);

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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "System.Environment.GetEnvironmentVariable(string)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray<MetadataReference>.Empty,
            ImmutableDictionary<string, string>.Empty);

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.Not.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_WithTrustedGeneratedPureConstructorClassification_SuppressesUnknownExternalCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "PureConstructorBoundary..ctor(int)",
                        "pure",
                        """[]""",
                        "PureConstructorBoundary.PureConstructorBoundary(int)"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_ConfigKnownImpureConstructor_OverridesGeneratedPureConstructorClassification()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ConfiguredConstructorBoundary..ctor(int)",
                        "pure",
                        """[]""",
                        methodIdentity.Symbol))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_known_impure_methods",
                methodIdentity.CanonicalKey));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("config_known_impure"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureConstructorClassification_ReportsImpurity()
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

        await using var fixture =
            await CreateFixtureAssemblyAsync("GeneratedImpureConstructorBoundary", boundarySource);
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureConstructorBoundary..ctor(int)",
                        "impure",
                        """[ "global_state_write" ]""",
                        "ImpureConstructorBoundary.ImpureConstructorBoundary(int)"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_OverridesReviewedPureStringBuilderConstructor_WhenGeneratedSummaryIsImpure()
    {
        var identity = GetAssemblyIdentity(typeof(StringBuilder).Assembly.Location);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
            """, ("SharpProof.EffectSummary.json",
                GeneratedPurityTestSupport.CreatePuritySummaryJson(
                    identity.AssemblyPath,
                    "System.Text.StringBuilder..ctor()",
                    "impure",
                    """[ "global_state_write" ]""",
                    "System.Text.StringBuilder.StringBuilder()")));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedPureGetterClassification_SuppressesUnknownExternalCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "PureGetterBoundary.get_Value()",
                        "pure",
                        """[]""",
                        "PureGetterBoundary.Value.get"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureGetterClassification_ReportsImpurity()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureGetterBoundary.get_Value()",
                        "impure",
                        """[ "global_state_write" ]""",
                        "ImpureGetterBoundary.Value.get"))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_OverridesKnownImpureThreadCurrentThreadProperty_WhenGeneratedSummaryIsPure()
    {
        var identity = GetAssemblyIdentity(typeof(Thread).Assembly.Location);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
            """, ("SharpProof.EffectSummary.json",
                GeneratedPurityTestSupport.CreatePuritySummaryJson(
                    identity.AssemblyPath,
                    "System.Threading.Thread.get_CurrentThread()",
                    "pure",
                    """[]""")));

        Assert.That(
            diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False,
            "Trusted generated getter purity should override the built-in known-impure property fallback for metadata properties.");
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_ConfigKnownImpureInterpolationToString_OverridesGeneratedPureFormattingClassification()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ConfiguredFormattingBoundary.ToString()",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)),
            ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_known_impure_methods",
                methodIdentity.CanonicalKey));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("config_known_impure"));
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedPureListPatternMembers_SuppressesUnknownExternalCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("length.SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "GeneratedListPatternBoundary.get_Length()",
                        "pure",
                        """[]""")),
                ("indexer.SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "GeneratedListPatternBoundary.get_Item(int)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureListPatternLength_ReportsImpurity()
    {
        const string boundarySource = """
                                      public sealed class GeneratedListPatternBoundary
                                      {
                                          public int Length => 2;

                                          public int this[int index] => index;
                                      }
                                      """;

        await using var fixture =
            await CreateFixtureAssemblyAsync("GeneratedImpureListPatternBoundary", boundarySource);
        var identity = GetAssemblyIdentity(fixture.AssemblyPath);

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("length.SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "GeneratedListPatternBoundary.get_Length()",
                        "impure",
                        """[ "global_state_write" ]""")),
                ("indexer.SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "GeneratedListPatternBoundary.get_Item(int)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_WithTrustedGeneratedPureBinaryOperatorClassification_SuppressesUnknownExternalCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "PureOperatorBoundary.op_Addition(PureOperatorBoundary, PureOperatorBoundary)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureBinaryOperatorClassification_ReportsImpurity()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureOperatorBoundary.op_Addition(ImpureOperatorBoundary, ImpureOperatorBoundary)",
                        "impure",
                        """[ "global_state_write" ]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
    }

    [Test]
    public async Task
        Sp0002_EffectSummary_WithTrustedGeneratedPureConversionClassification_SuppressesUnknownExternalCall()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "PureConversionBoundary.op_Explicit(PureConversionBoundary)",
                        "pure",
                        """[]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
    }

    [Test]
    public async Task Sp0002_EffectSummary_WithTrustedGeneratedImpureConversionClassification_ReportsImpurity()
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

        var diagnostics = await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
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
                ("SharpProof.EffectSummary.json",
                    GeneratedPurityTestSupport.CreatePuritySummaryJson(
                        identity.AssemblyPath,
                        "ImpureConversionBoundary.op_Explicit(ImpureConversionBoundary)",
                        "impure",
                        """[ "global_state_write" ]"""))
            },
            ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(fixture.AssemblyPath)));

        var diagnostic = diagnostics.Single(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty],
            Is.EqualTo("global_state_write"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
            Is.EqualTo("generated_purity_summary"));
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
                "SharpProof.EffectSummary.json",
                CreateEffectSummaryJson(coreLib, "System.ArgumentNullException.ThrowIfNull(object, string)"))),
            new TestAnalyzerConfigOptionsProvider(CreateEffectSummaryJsonEnabledGlobalOptions()));

        var catalogType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.EffectSummaryCatalog", true)!;
        var fromOptionsMethod = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
        var tryGetExceptionInfosMethod = catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetExceptionInfos" &&
                              method.GetParameters().Length == 3 &&
                              method.GetParameters()[1].ParameterType == typeof(Compilation));
        var assemblyIdentityCacheField =
            catalogType.GetField("AssemblyIdentityCache", BindingFlags.Static | BindingFlags.NonPublic)!;
        var assemblyIdentityCache = assemblyIdentityCacheField.GetValue(null)!;
        assemblyIdentityCache.GetType().GetMethod("Clear")!.Invoke(assemblyIdentityCache, null);

        try
        {
            var catalog =
                fromOptionsMethod.Invoke(null, new object?[] { analyzerOptions, default(CancellationToken) })!;

            var firstArgs = new object?[] { methodSymbol, compilation, null };
            Assert.That((bool)tryGetExceptionInfosMethod.Invoke(catalog, firstArgs)!, Is.True);
            var firstExceptions = ((IEnumerable)firstArgs[2]!)
                .Cast<object>()
                .Select(info => (string)info.GetType().GetProperty("ExceptionType")!.GetValue(info)!)
                .ToArray();

            Assert.That(firstExceptions, Is.EqualTo(new[] { "System.ArgumentNullException" }));
            Assert.That(GetCount(assemblyIdentityCache), Is.EqualTo(1));

            var secondArgs = new object?[] { methodSymbol, compilation, null };
            Assert.That((bool)tryGetExceptionInfosMethod.Invoke(catalog, secondArgs)!, Is.True);
            var secondExceptions = ((IEnumerable)secondArgs[2]!)
                .Cast<object>()
                .Select(info => (string)info.GetType().GetProperty("ExceptionType")!.GetValue(info)!)
                .ToArray();

            Assert.That(secondExceptions, Is.EqualTo(new[] { "System.ArgumentNullException" }));
            Assert.That(GetCount(assemblyIdentityCache), Is.EqualTo(1));
        }
        finally
        {
            assemblyIdentityCache.GetType().GetMethod("Clear")!.Invoke(assemblyIdentityCache, null);
        }
    }

    [Test]
    public void ExceptionSummaryCatalog_TryGetExceptionInfos_PreservesSourcesAndEdges()
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
                "SharpProof.EffectSummary.json",
                CreateEffectSummaryJson(
                    coreLib,
                    "System.ArgumentNullException.ThrowIfNull(object, string)",
                    thrownExceptionTypesJson: """[ "System.ArgumentException" ]""",
                    transitiveThrownExceptionTypesJson:
                    """[ "System.ArgumentException", "System.InvalidOperationException" ]""",
                    thrownExceptionSourcePathsJson: """
                                                    [
                                                      {
                                                        "ExceptionType": "System.ArgumentException",
                                                        "SourcePath": "throw new System.ArgumentException()"
                                                      }
                                                    ]
                                                    """,
                    transitiveThrownExceptionSourcePathsJson: """
                                                              [
                                                                {
                                                                  "ExceptionType": "System.ArgumentException",
                                                                  "SourcePath": "throw new System.ArgumentException()"
                                                                },
                                                                {
                                                                  "ExceptionType": "System.InvalidOperationException",
                                                                  "SourcePath": "TestMethod() -> Helper()"
                                                                }
                                                              ]
                                                              """,
                    transitiveThrownExceptionEdgesJson: """
                                                        [
                                                          {
                                                            "ExceptionType": "System.ArgumentException",
                                                            "SourcePath": "throw new System.ArgumentException()",
                                                            "Depth": 0
                                                          },
                                                          {
                                                            "ExceptionType": "System.InvalidOperationException",
                                                            "SourcePath": "TestMethod() -> Helper()",
                                                            "CalleeExactSymbolKey": "TestClass.Helper()->void",
                                                            "Depth": 1
                                                          }
                                                        ]
                                                        """))),
            new TestAnalyzerConfigOptionsProvider(CreateEffectSummaryJsonEnabledGlobalOptions()));

        var catalogType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.EffectSummaryCatalog", true)!;
        var fromOptionsMethod = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
        var tryGetExceptionInfosMethod = catalogType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "TryGetExceptionInfos" &&
                              method.GetParameters().Length == 3 &&
                              method.GetParameters()[1].ParameterType == typeof(Compilation));
        var catalog = fromOptionsMethod.Invoke(null, new object?[] { analyzerOptions, default(CancellationToken) })!;

        var args = new object?[] { methodSymbol, compilation, null };
        Assert.That((bool)tryGetExceptionInfosMethod.Invoke(catalog, args)!, Is.True);

        var facts = ((IEnumerable)args[2]!)
            .Cast<object>()
            .SelectMany(info =>
            {
                var infoType = info.GetType();
                var exceptionType = (string)infoType.GetProperty("ExceptionType")!.GetValue(info)!;
                var sources = ((IEnumerable)infoType.GetProperty("Sources")!.GetValue(info)!)
                    .Cast<string>()
                    .Select(source => (ExceptionType: exceptionType, SourcePath: source,
                        CalleeExactSymbolKey: (string?)null, Depth: (int?)null));
                var edges = ((IEnumerable)infoType.GetProperty("Edges")!.GetValue(info)!)
                    .Cast<object>()
                    .Select(edge =>
                    {
                        var edgeType = edge.GetType();
                        var calleeIdentity = edgeType.GetProperty("CalleeIdentity")!.GetValue(edge);
                        return (
                            ExceptionType: exceptionType,
                            SourcePath: (string)edgeType.GetProperty("SourcePath")!.GetValue(edge)!,
                            CalleeExactSymbolKey: (string?)calleeIdentity?.GetType()
                                .GetProperty("Name")!.GetValue(calleeIdentity),
                            Depth: (int?)edgeType.GetProperty("Depth")!.GetValue(edge));
                    });
                return sources.Concat(edges);
            })
            .ToArray();

        Assert.That(facts, Is.EqualTo(new[]
        {
            ("System.ArgumentException", "throw new System.ArgumentException()", null, null),
            ("System.ArgumentException", "throw new System.ArgumentException()", null, 0),
            ("System.InvalidOperationException", "TestMethod() -> Helper()", null, null),
            ("System.InvalidOperationException", "TestMethod() -> Helper()", "TestClass.Helper()->void", (int?)1)
        }));
    }
}
