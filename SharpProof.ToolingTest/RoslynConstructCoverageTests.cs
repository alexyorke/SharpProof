using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.Tools.CorpusReport;
using SharpProof.Tools.Fuzz;

namespace SharpProof.Test;

[TestFixture]
public class RoslynConstructCoverageTests
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    [Test]
    public void AllOperationKindsHaveCoverageDecision()
    {
        var operationShapeIds = RoslynShapeManifest.OperationEntries
            .Select(entry => entry.ShapeId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var missing = Enum.GetValues<OperationKind>()
            .Where(kind => !operationShapeIds.Contains(RoslynShapeManifest.OperationShapeId(kind)))
            .Select(kind => kind.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(missing, Is.Empty,
            "OperationKind values without coverage decisions: " + string.Join(", ", missing));
    }

    [Test]
    public void RuleRegistryKindsAreKnown()
    {
        var registeredKinds = GetRegisteredRuleOperationKinds();
        var enumKinds = Enum.GetValues<OperationKind>().ToImmutableHashSet();
        var unknownRegisteredKinds = registeredKinds
            .Where(item => !enumKinds.Contains(item.OperationKind))
            .Select(item => item.RuleName + ":" + item.OperationKind)
            .ToArray();

        Assert.That(unknownRegisteredKinds, Is.Empty);

        var allowedDuplicateOwners = new Dictionary<OperationKind, ImmutableHashSet<string>>
        {
            [OperationKind.Binary] =
                ImmutableHashSet.Create(StringComparer.Ordinal, "BinaryOperationPurityRule", "IsNullPurityRule"),
            [OperationKind.LocalFunction] = ImmutableHashSet.Create(StringComparer.Ordinal,
                "LocalFunctionOperationPurityRule", "StructuralPurityRule")
        };

        var unexpectedDuplicates = registeredKinds
            .GroupBy(item => item.OperationKind)
            .Where(group => group.Select(item => item.RuleName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Where(group =>
                !allowedDuplicateOwners.TryGetValue(group.Key, out var expectedOwners) ||
                !expectedOwners.SetEquals(group.Select(item => item.RuleName)))
            .Select(group => group.Key + ":" + string.Join(",",
                group.Select(item => item.RuleName).OrderBy(name => name, StringComparer.Ordinal)))
            .ToArray();

        Assert.That(unexpectedDuplicates, Is.Empty,
            "Duplicate OperationKind rule owners must be explicitly allowlisted.");
    }

    [Test]
    public void RuleRegistry_AlwaysPureOperationsUseDeclarativeDescriptors()
    {
        var expectedDeclarativeKinds = new[]
        {
            OperationKind.ParameterReference,
            OperationKind.LocalReference,
            OperationKind.InstanceReference,
            OperationKind.DefaultValue,
            OperationKind.Literal,
            OperationKind.TypeOf,
            OperationKind.NameOf,
            OperationKind.Utf8String,
            OperationKind.SizeOf,
            OperationKind.ConstantPattern,
            OperationKind.DeclarationPattern,
            OperationKind.DiscardPattern,
            OperationKind.Branch
        };

        var declarativeKinds = GetRegisteredRuleOperationKinds()
            .Where(static item => item.RuleName == "DeclarativePureOperationRule")
            .Select(static item => item.OperationKind)
            .ToArray();

        Assert.That(declarativeKinds, Is.EquivalentTo(expectedDeclarativeKinds));
    }

    [Test]
    public void AnalyzerActionSurfaceCoverageTests()
    {
        var surfaces =
            RoslynShapeManifest.ActionSurfaceEntries.ToImmutableDictionary(surface => surface.Name,
                StringComparer.Ordinal);
        var expectedSurfaces = new[]
        {
            "CompilationStart",
            "CompilationEnd",
            "Operation",
            "OperationBlock",
            "OperationBlockStart",
            "SemanticModel",
            "Symbol",
            "SyntaxNode",
            "SyntaxTree"
        };

        Assert.That(surfaces.Keys, Is.EquivalentTo(expectedSurfaces));
        Assert.That(surfaces["CompilationStart"].Decision.ToString(), Is.EqualTo("Used"));
        Assert.That(surfaces["CompilationEnd"].Decision.ToString(), Is.EqualTo("Used"));
        Assert.That(surfaces["SyntaxNode"].Decision.ToString(), Is.EqualTo("Used"));
        Assert.That(surfaces["SyntaxTree"].Decision.ToString(), Is.EqualTo("Used"));
        Assert.That(surfaces["OperationBlock"].Decision.ToString(), Is.EqualTo("Used"));
        Assert.That(surfaces["Operation"].Decision.ToString(), Is.EqualTo("NotUsed"));
        Assert.That(surfaces["OperationBlockStart"].Decision.ToString(), Is.EqualTo("NotUsed"));
        Assert.That(surfaces["SemanticModel"].Decision.ToString(), Is.EqualTo("NotUsed"));
        Assert.That(surfaces["Symbol"].Decision.ToString(), Is.EqualTo("NotUsed"));
        Assert.That(surfaces.Values.Select(surface => surface.Rationale), Has.All.Not.Empty);
    }

    [TestCaseSource(nameof(OperationCorpusSnippets))]
    public void CorpusSnippetsProduceExpectedOperationKinds(OperationCorpusSnippet snippet)
    {
        var observedKinds = GetOperationKinds(snippet.Source, snippet.AllowUnsafe);
        var missingKinds = snippet.ExpectedOperationKinds
            .Where(kind => !observedKinds.Contains(kind))
            .Select(kind => kind.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(missingKinds, Is.Empty, snippet.Name + " did not produce expected operation kinds.");
    }

    [Test]
    public async Task PureCorpusDoesNotEmitUnsupportedOperation()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync("""
                                                            using SharpProof.Attributes;

                                                            public class TestClass
                                                            {
                                                                [EnforcePure]
                                                                public int Match(int[] values)
                                                                {
                                                                    return values is [1, .., 3] ? 1 : 0;
                                                                }
                                                            }
                                                            """);

        var unsupportedDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
            .Where(diagnostic =>
                diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpurityCategoryProperty, out var category) &&
                string.Equals(category, "unsupported_operation", StringComparison.Ordinal))
            .ToArray();

        Assert.That(unsupportedDiagnostics, Is.Empty);
    }

    [Test]
    public async Task ImpureCorpusEmitsEvidence()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync("""
                                                            using System;
                                                            using SharpProof.Attributes;

                                                            public class TestClass
                                                            {
                                                                [EnforcePure]
                                                                public void TestMethod()
                                                                {
                                                                    Console.WriteLine("impure");
                                                                }
                                                            }
                                                            """);

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityOperationKindProperty],
            Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Is.Not.Null.And.Not.Empty);
    }

    [TestCaseSource(nameof(SyntaxShadowCorpusSnippets))]
    public void SyntaxShadowCorpusParsesExpectedSyntaxKinds(SyntaxShadowCorpusSnippet snippet)
    {
        var observedKinds = GetSyntaxKinds(snippet.Source);
        var missingKinds = snippet.ExpectedSyntaxKinds
            .Where(kind => !observedKinds.Contains(kind))
            .Select(kind => kind.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(missingKinds, Is.Empty, snippet.Name + " did not produce expected syntax kinds.");
    }

    [Test]
    public void CorpusReportAggregatesCoverage()
    {
        var report = SarifCorpusReport.CreateFromSarifJson("coverage.sarif", """
                                                                             {
                                                                               "version": "2.1.0",
                                                                               "runs": [
                                                                                 {
                                                                                   "results": [
                                                                                     {
                                                                                       "ruleId": "SP0002",
                                                                                       "properties": {
                                                                                         "sharpproof.impurity.category": "unsupported_operation",
                                                                                         "sharpproof.impurity.rule": "UnsupportedOperationRule",
                                                                                         "sharpproof.impurity.operation_kind": "FunctionPointerInvocation",
                                                                                         "sharpproof.impurity.symbol": "delegate*<void>"
                                                                                       }
                                                                                     },
                                                                                     {
                                                                                       "ruleId": "SP0002",
                                                                                       "properties": {
                                                                                         "sharpproof.impurity.category": "catalog_hit",
                                                                                         "sharpproof.impurity.rule": "MethodInvocationPurityRule",
                                                                                         "sharpproof.impurity.operation_kind": "Invocation",
                                                                                         "sharpproof.impurity.symbol": "System.Console.WriteLine(string)"
                                                                                       }
                                                                                     }
                                                                                   ]
                                                                                 }
                                                                               ]
                                                                             }
                                                                             """);

        Assert.That(report.SchemaVersion, Is.EqualTo(CorpusReportSummary.CurrentSchemaVersion));
        Assert.That(report.OperationKinds["FunctionPointerInvocation"], Is.EqualTo(1));
        Assert.That(report.OperationKinds["Invocation"], Is.EqualTo(1));
        Assert.That(report.UnknownOperationKinds["FunctionPointerInvocation"], Is.EqualTo(1));
    }

    private static ImmutableArray<RegisteredRuleOperationKind> GetRegisteredRuleOperationKinds()
    {
        var analyzerAssembly = typeof(SharpProofAnalyzer).Assembly;
        var registryType = analyzerAssembly.GetType("SharpProof.Analyzer.Engine.Rules.RuleRegistry", true)!;
        var getDefaultRulesMethod =
            registryType.GetMethod("GetDefaultRules", BindingFlags.Public | BindingFlags.Static)!;
        var rules = (IEnumerable)getDefaultRulesMethod.Invoke(null, null)!;
        var builder = ImmutableArray.CreateBuilder<RegisteredRuleOperationKind>();

        foreach (var rule in rules)
        {
            var applicableOperationKindsProperty = rule.GetType().GetProperty(
                "ApplicableOperationKinds",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var operationKinds = (IEnumerable)applicableOperationKindsProperty.GetValue(rule)!;

            foreach (OperationKind operationKind in operationKinds)
                builder.Add(new RegisteredRuleOperationKind(rule.GetType().Name, operationKind));
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<OperationKind> GetOperationKinds(string source, bool allowUnsafe)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var compilation = CreateCompilation("RoslynConstructCorpus", syntaxTree, allowUnsafe);
        AssertNoCompilationErrors(compilation);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var builder = ImmutableHashSet.CreateBuilder<OperationKind>();

        foreach (var node in syntaxTree.GetRoot().DescendantNodes())
        {
            var operation = semanticModel.GetOperation(node, CancellationToken.None);
            if (operation is null) continue;

            foreach (var descendant in operation.DescendantsAndSelf()) builder.Add(descendant.Kind);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<SyntaxKind> GetSyntaxKinds(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
        var builder = ImmutableHashSet.CreateBuilder<SyntaxKind>();

        builder.Add((SyntaxKind)root.RawKind);
        foreach (var nodeOrToken in root.DescendantNodesAndTokens(descendIntoTrivia: true))
            builder.Add((SyntaxKind)nodeOrToken.RawKind);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            builder.Add((SyntaxKind)trivia.RawKind);
            var structure = trivia.GetStructure();
            if (structure is null) continue;

            builder.Add((SyntaxKind)structure.RawKind);
            foreach (var nodeOrToken in structure.DescendantNodesAndTokens(descendIntoTrivia: true))
                builder.Add((SyntaxKind)nodeOrToken.RawKind);
        }

        return builder.ToImmutable();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source,
        bool allowUnsafe = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        var compilation = CreateCompilation("RoslynConstructAnalyzerCorpus", syntaxTree, allowUnsafe);
        AssertNoCompilationErrors(compilation);

        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer()),
            new CompilationWithAnalyzersOptions(
                options,
                null,
                true,
                false,
                false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, SyntaxTree syntaxTree, bool allowUnsafe)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.That(trustedPlatformAssemblies, Is.Not.Null.And.Not.Empty);

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Append(typeof(EnforcePureAttribute).Assembly.Location)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (MetadataReference)MetadataReference.CreateFromFile(group.Key))
            .ToImmutableArray();
    }

    private static void AssertNoCompilationErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Is.Empty,
            string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
    }

    private static IEnumerable<OperationCorpusSnippet> OperationCorpusSnippets()
    {
        yield return new OperationCorpusSnippet(
            "InterpolatedStringHandler",
            """
            using System.Runtime.CompilerServices;

            [InterpolatedStringHandler]
            public ref struct PureHandler
            {
                public PureHandler(int literalLength, int formattedCount, int value) { }
                public void AppendLiteral(string value) { }
                public void AppendFormatted<T>(T value) { }
            }

            public class C
            {
                public void Log(int value, [InterpolatedStringHandlerArgument("value")] PureHandler handler) { }
                public void M(int value) => Log(value, $"left={value}" + $"right={value}");
            }
            """,
            false,
            OperationKind.InterpolatedStringHandlerCreation,
            OperationKind.InterpolatedStringAddition,
            OperationKind.InterpolatedStringAppendLiteral,
            OperationKind.InterpolatedStringAppendFormatted,
            OperationKind.InterpolatedStringHandlerArgumentPlaceholder);

        yield return new OperationCorpusSnippet(
            "FunctionPointerInvocation",
            """
            public unsafe class C
            {
                public int M(delegate*<int, int> pointer)
                {
                    return pointer(1);
                }
            }
            """,
            true,
            OperationKind.FunctionPointerInvocation);

        yield return new OperationCorpusSnippet(
            "ListAndSlicePatterns",
            """
            public class C
            {
                public int M(int[] values)
                {
                    return values is [1, .., 3] ? 1 : 0;
                }
            }
            """,
            false,
            OperationKind.ListPattern,
            OperationKind.SlicePattern);

        yield return new OperationCorpusSnippet(
            "InlineArrayAccess",
            """
            using System.Runtime.CompilerServices;

            [InlineArray(4)]
            public struct Buffer
            {
                private int _element0;
            }

            public class C
            {
                public int M()
                {
                    Buffer buffer = default;
                    return buffer[0];
                }
            }
            """,
            false,
            OperationKind.InlineArrayAccess);

        yield return new OperationCorpusSnippet(
            "ImplicitIndexerReference",
            """
            public sealed class Bag
            {
                public int Length => 3;
                public int this[int index] => index;
            }

            public class C
            {
                public int M(Bag bag)
                {
                    return bag[^1];
                }
            }
            """,
            false,
            OperationKind.ImplicitIndexerReference);

        yield return new OperationCorpusSnippet(
            "Utf8StringLiteral",
            """
            using System;

            public class C
            {
                public ReadOnlySpan<byte> M()
                {
                    return "abc"u8;
                }
            }
            """,
            false,
            OperationKind.Utf8String);

        yield return new OperationCorpusSnippet(
            "CollectionExpressionAndSpread",
            """
            public class C
            {
                public int[] M(int[] values)
                {
                    return [1, ..values, 4];
                }
            }
            """,
            false,
            OperationKind.CollectionExpression,
            OperationKind.Spread);

        yield return new OperationCorpusSnippet(
            "PrimaryConstructor",
            """
            public class C(int value)
            {
                public int M()
                {
                    return value;
                }
            }
            """,
            false,
            OperationKind.ParameterReference);

        yield return new OperationCorpusSnippet(
            "StaticAbstractInterfaceMember",
            """
            public interface IHasZero<TSelf>
                where TSelf : IHasZero<TSelf>
            {
                static abstract TSelf Zero { get; }
            }

            public class C
            {
                public T M<T>()
                    where T : IHasZero<T>
                {
                    return T.Zero;
                }
            }
            """,
            false,
            OperationKind.PropertyReference);

        yield return new OperationCorpusSnippet(
            "UnsafeAddressOf",
            """
            public unsafe class C
            {
                public int M(int value)
                {
                    int* pointer = &value;
                    return *pointer;
                }
            }
            """,
            true,
            OperationKind.AddressOf);
    }

    private static IEnumerable<SyntaxShadowCorpusSnippet> SyntaxShadowCorpusSnippets()
    {
        yield return new SyntaxShadowCorpusSnippet(
            "AttributesAndDeclarations",
            """
            [System.Obsolete]
            public record R(int Value);

            public struct S { }
            public interface I { }
            public enum E { A }
            public delegate void D();
            """,
            SyntaxKind.Attribute,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.EnumDeclaration,
            SyntaxKind.DelegateDeclaration);

        yield return new SyntaxShadowCorpusSnippet(
            "UsingAliasAndFileScopedNamespace",
            """
            using TextBuilder = System.Text.StringBuilder;

            namespace N;

            public class C { }
            """,
            SyntaxKind.UsingDirective,
            SyntaxKind.FileScopedNamespaceDeclaration);

        yield return new SyntaxShadowCorpusSnippet(
            "PreprocessorDirectives",
            """
            #define FLAG
            #if FLAG
            public class Active { }
            #else
            public class Inactive { }
            #endif
            """,
            SyntaxKind.DefineDirectiveTrivia,
            SyntaxKind.IfDirectiveTrivia,
            SyntaxKind.ElseDirectiveTrivia,
            SyntaxKind.EndIfDirectiveTrivia);

        yield return new SyntaxShadowCorpusSnippet(
            "DocumentationTrivia",
            """
            /// <summary>Documents C.</summary>
            public class C { }
            """,
            SyntaxKind.SingleLineDocumentationCommentTrivia,
            SyntaxKind.XmlElement,
            SyntaxKind.XmlText);

        yield return new SyntaxShadowCorpusSnippet(
            "MalformedTokens",
            """
            public class C
            {
                public void M()
                {
                    @
                }
            }
            """,
            SyntaxKind.BadToken);

        yield return new SyntaxShadowCorpusSnippet(
            "PrimaryConstructorSyntax",
            """
            public class Base(int value) { }
            public class Derived(int value) : Base(value) { }
            """,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.ParameterList,
            SyntaxKind.PrimaryConstructorBaseType);
    }

    private sealed record RegisteredRuleOperationKind(string RuleName, OperationKind OperationKind);

    public sealed record OperationCorpusSnippet(
        string Name,
        string Source,
        bool AllowUnsafe,
        ImmutableHashSet<OperationKind> ExpectedOperationKinds)
    {
        public OperationCorpusSnippet(string name, string source, bool allowUnsafe,
            params OperationKind[] expectedOperationKinds)
            : this(name, source, allowUnsafe, expectedOperationKinds.ToImmutableHashSet())
        {
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed record SyntaxShadowCorpusSnippet(
        string Name,
        string Source,
        ImmutableHashSet<SyntaxKind> ExpectedSyntaxKinds)
    {
        public SyntaxShadowCorpusSnippet(string name, string source, params SyntaxKind[] expectedSyntaxKinds)
            : this(name, source, expectedSyntaxKinds.ToImmutableHashSet())
        {
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
