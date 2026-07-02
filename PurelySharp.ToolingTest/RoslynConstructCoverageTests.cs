using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Attributes;
using PurelySharp.Tools.CorpusReport;
using PurelySharp.Tools.Fuzz;

namespace PurelySharp.Test
{
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

            Assert.That(missing, Is.Empty, "OperationKind values without coverage decisions: " + string.Join(", ", missing));
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
                [OperationKind.Binary] = ImmutableHashSet.Create(StringComparer.Ordinal, "BinaryOperationPurityRule", "IsNullPurityRule"),
                [OperationKind.LocalFunction] = ImmutableHashSet.Create(StringComparer.Ordinal, "LocalFunctionOperationPurityRule", "StructuralPurityRule")
            };

            var unexpectedDuplicates = registeredKinds
                .GroupBy(item => item.OperationKind)
                .Where(group => group.Select(item => item.RuleName).Distinct(StringComparer.Ordinal).Count() > 1)
                .Where(group =>
                    !allowedDuplicateOwners.TryGetValue(group.Key, out var expectedOwners) ||
                    !expectedOwners.SetEquals(group.Select(item => item.RuleName)))
                .Select(group => group.Key + ":" + string.Join(",", group.Select(item => item.RuleName).OrderBy(name => name, StringComparer.Ordinal)))
                .ToArray();

            Assert.That(unexpectedDuplicates, Is.Empty, "Duplicate OperationKind rule owners must be explicitly allowlisted.");
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
            var surfaces = RoslynShapeManifest.ActionSurfaceEntries.ToImmutableDictionary(surface => surface.Name, StringComparer.Ordinal);
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
            Assert.That(surfaces["SyntaxNode"].Decision.ToString(), Is.EqualTo("Used"));
            Assert.That(surfaces["Operation"].Decision.ToString(), Is.EqualTo("NotUsed"));
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
using PurelySharp.Attributes;

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
                .Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Where(diagnostic =>
                    diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpurityCategoryProperty, out var category) &&
                    string.Equals(category, "unsupported_operation", StringComparison.Ordinal))
                .ToArray();

            Assert.That(unsupportedDiagnostics, Is.Empty);
        }

        [Test]
        public async Task ImpureCorpusEmitsEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync("""
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine("impure");
    }
}
""");

            var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.Not.Null.And.Not.Empty);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Is.Not.Null.And.Not.Empty);
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
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.rule": "UnsupportedOperationRule",
            "purelysharp.impurity.operation_kind": "FunctionPointerInvocation",
            "purelysharp.impurity.symbol": "delegate*<void>"
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "catalog_hit",
            "purelysharp.impurity.rule": "MethodInvocationPurityRule",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "System.Console.WriteLine(string)"
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

        private static ImmutableDictionary<OperationKind, OperationKindCoverageDecision> GetCompleteOperationKindCoverageDecisions()
        {
            var builder = ExplicitOperationKindCoverageDecisions.ToBuilder();

            foreach (var operationKind in GetRegisteredRuleOperationKinds().Select(item => item.OperationKind).Distinct())
            {
                builder[operationKind] = new OperationKindCoverageDecision(
                    OperationKindCoverageClassification.Handled,
                    "Registered by RuleRegistry.");
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<RegisteredRuleOperationKind> GetRegisteredRuleOperationKinds()
        {
            var analyzerAssembly = typeof(PurelySharpAnalyzer).Assembly;
            var registryType = analyzerAssembly.GetType("PurelySharp.Analyzer.Engine.Rules.RuleRegistry", throwOnError: true)!;
            var getDefaultRulesMethod = registryType.GetMethod("GetDefaultRules", BindingFlags.Public | BindingFlags.Static)!;
            var rules = (IEnumerable)getDefaultRulesMethod.Invoke(null, null)!;
            var builder = ImmutableArray.CreateBuilder<RegisteredRuleOperationKind>();

            foreach (var rule in rules)
            {
                var applicableOperationKindsProperty = rule.GetType().GetProperty(
                    "ApplicableOperationKinds",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                var operationKinds = (IEnumerable)applicableOperationKindsProperty.GetValue(rule)!;

                foreach (OperationKind operationKind in operationKinds)
                {
                    builder.Add(new RegisteredRuleOperationKind(rule.GetType().Name, operationKind));
                }
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
                if (operation is null)
                {
                    continue;
                }

                foreach (var descendant in operation.DescendantsAndSelf())
                {
                    builder.Add(descendant.Kind);
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableHashSet<SyntaxKind> GetSyntaxKinds(string source)
        {
            var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();
            var builder = ImmutableHashSet.CreateBuilder<SyntaxKind>();

            builder.Add((SyntaxKind)root.RawKind);
            foreach (var nodeOrToken in root.DescendantNodesAndTokens(descendIntoTrivia: true))
            {
                builder.Add((SyntaxKind)nodeOrToken.RawKind);
            }

            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                builder.Add((SyntaxKind)trivia.RawKind);
                var structure = trivia.GetStructure();
                if (structure is null)
                {
                    continue;
                }

                builder.Add((SyntaxKind)structure.RawKind);
                foreach (var nodeOrToken in structure.DescendantNodesAndTokens(descendIntoTrivia: true))
                {
                    builder.Add((SyntaxKind)nodeOrToken.RawKind);
                }
            }

            return builder.ToImmutable();
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, bool allowUnsafe = false)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
            var compilation = CreateCompilation("RoslynConstructAnalyzerCorpus", syntaxTree, allowUnsafe);
            AssertNoCompilationErrors(compilation);

            var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    options,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

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

            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString())));
        }

#pragma warning disable CS0619
        private static readonly ImmutableDictionary<OperationKind, OperationKindCoverageDecision> ExplicitOperationKindCoverageDecisions =
            new Dictionary<OperationKind, OperationKindCoverageDecision>
            {
                [OperationKind.None] = NotApplicable("Sentinel value; no executable operation exists."),
                [OperationKind.Invalid] = Conservative("Invalid code is not a purity proof target."),
                [OperationKind.YieldBreak] = ParentHandled("Iterator control flow is handled at the containing method/yield boundary."),
                [OperationKind.Stop] = NotApplicable("Visual Basic-only debugging statement."),
                [OperationKind.End] = NotApplicable("Visual Basic-only process termination statement."),
                [OperationKind.RaiseEvent] = NotApplicable("Visual Basic-only event raise operation."),
                [OperationKind.MethodReference] = ParentHandled("Method group operations are consumed by invocation or delegate-creation operations."),
                [OperationKind.UnaryOperator] = ParentHandled("Legacy/operator-only shape; current C# executable unary expressions use Unary."),
                [OperationKind.BinaryOperator] = ParentHandled("Legacy/operator-only shape; current C# executable binary expressions use Binary."),
                [OperationKind.Parenthesized] = ParentHandled("Parentheses are transparent and analyzed through their contained operation."),
                [OperationKind.ConditionalAccessInstance] = ParentHandled("Analyzed as part of the containing conditional-access operation."),
                [OperationKind.MemberInitializer] = ParentHandled("Analyzed through object or collection initializer handling."),
                [OperationKindValue("CollectionElementInitializer")] = ParentHandled("Analyzed through object or collection initializer handling."),
                [OperationKind.TranslatedQuery] = ParentHandled("Query syntax is analyzed through translated invocation and expression operations."),
                [OperationKind.AddressOf] = Conservative("Unsafe pointer address capture remains conservative."),
                [OperationKind.DeclarationExpression] = ParentHandled("Declaration expressions are analyzed through the containing assignment, pattern, or argument."),
                [OperationKind.OmittedArgument] = ParentHandled("Omitted arguments are analyzed through the containing invocation/default parameter handling."),
                [OperationKind.ParameterInitializer] = ParentHandled("Default parameter values are declaration metadata, not a method-body side effect."),
                [OperationKind.SwitchCase] = ParentHandled("Switch case structure is covered by switch and case-clause handling."),
                [OperationKind.InterpolatedStringText] = ParentHandled("Text fragments are covered by the containing interpolated string operation."),
                [OperationKind.Interpolation] = ParentHandled("Formatted holes are covered by the containing interpolated string operation."),
                [OperationKind.TupleBinary] = ParentHandled("Tuple comparisons are analyzed through their child binary operations."),
                [OperationKind.TupleBinaryOperator] = ParentHandled("Legacy/operator-only tuple comparison shape."),
                [OperationKind.MethodBody] = ParentHandled("Legacy method-body shape; current C# operation trees use MethodBodyOperation."),
                [OperationKind.ConstructorBody] = ParentHandled("Legacy constructor-body shape; current C# operation trees use ConstructorBodyOperation."),
                [OperationKind.Discard] = ParentHandled("Discard expressions are analyzed through their containing assignment or pattern."),
                [OperationKind.FlowCapture] = ParentHandled("Control-flow graph temporary captured value."),
                [OperationKind.FlowCaptureReference] = ParentHandled("Control-flow graph temporary capture reference."),
                [OperationKind.IsNull] = ParentHandled("Null checks are covered through binary/null-pattern handling."),
                [OperationKind.CaughtException] = ParentHandled("Caught exception values are covered by catch-clause and local-reference handling."),
                [OperationKind.StaticLocalInitializationSemaphore] = ParentHandled("Compiler-generated synchronization around static local initialization."),
                [OperationKind.ReDim] = NotApplicable("Visual Basic-only array resizing operation."),
                [OperationKind.ReDimClause] = NotApplicable("Visual Basic-only array resizing clause."),
                [OperationKind.SwitchExpressionArm] = ParentHandled("Switch expression arms are covered by switch-expression handling."),
                [OperationKind.InterpolatedStringHandlerCreation] = Conservative("Custom interpolated-string handlers can execute user code."),
                [OperationKind.InterpolatedStringAddition] = Conservative("Custom interpolated-string handler append/addition can execute user code."),
                [OperationKind.InterpolatedStringAppendLiteral] = Conservative("Custom interpolated-string handler AppendLiteral can execute user code."),
                [OperationKind.InterpolatedStringAppendFormatted] = Conservative("Custom interpolated-string handler AppendFormatted can execute user code."),
                [OperationKind.InterpolatedStringAppendInvalid] = Conservative("Invalid handler append shape is not a purity proof target."),
                [OperationKind.InterpolatedStringHandlerArgumentPlaceholder] = Conservative("Handler argument placeholders are tied to custom handler execution."),
                [OperationKind.FunctionPointerInvocation] = Conservative("Unsafe function pointer invocation remains conservative."),
                [OperationKind.Attribute] = SyntaxOnly("Attribute analysis is handled by declaration/syntax placement checks."),
                
            }.ToImmutableDictionary();
#pragma warning restore CS0619

        private static readonly ImmutableArray<AnalyzerActionSurface> AnalyzerActionSurfaceManifest =
            ImmutableArray.Create(
                new AnalyzerActionSurface("CompilationStart", AnalyzerActionSurfaceDecision.Used, "Creates per-compilation purity, configuration, baseline, and exception-summary services."),
                new AnalyzerActionSurface("CompilationEnd", AnalyzerActionSurfaceDecision.NotUsed, "No end-of-compilation aggregation is required for the current analyzer behavior."),
                new AnalyzerActionSurface("Operation", AnalyzerActionSurfaceDecision.NotUsed, "PurelySharp walks IOperation trees from selected declarations instead of registering per-operation actions."),
                new AnalyzerActionSurface("OperationBlock", AnalyzerActionSurfaceDecision.NotUsed, "Method/accessor/local-function syntax actions are the current executable-code entry point."),
                new AnalyzerActionSurface("OperationBlockStart", AnalyzerActionSurfaceDecision.NotUsed, "The engine owns operation traversal and state rather than Roslyn operation-block callbacks."),
                new AnalyzerActionSurface("SemanticModel", AnalyzerActionSurfaceDecision.NotUsed, "Semantic models are reached from syntax-node contexts only where needed."),
                new AnalyzerActionSurface("Symbol", AnalyzerActionSurfaceDecision.NotUsed, "Symbol checks are driven from declaration syntax to preserve placement and source-span behavior."),
                new AnalyzerActionSurface("SyntaxNode", AnalyzerActionSurfaceDecision.Used, "Registers method, accessor, constructor, operator, local-function, and attribute-list entry points."),
                new AnalyzerActionSurface("SyntaxTree", AnalyzerActionSurfaceDecision.NotUsed, "No file-wide syntax-only analysis is currently needed."));

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
                allowUnsafe: false,
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
                allowUnsafe: true,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: false,
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
                allowUnsafe: true,
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

        private static OperationKindCoverageDecision NotApplicable(string rationale)
        {
            return new OperationKindCoverageDecision(OperationKindCoverageClassification.CSharpNotApplicable, rationale);
        }

        private static OperationKindCoverageDecision ParentHandled(string rationale)
        {
            return new OperationKindCoverageDecision(OperationKindCoverageClassification.ParentHandled, rationale);
        }

        private static OperationKindCoverageDecision Conservative(string rationale)
        {
            return new OperationKindCoverageDecision(OperationKindCoverageClassification.IntentionallyConservative, rationale);
        }

        private static OperationKindCoverageDecision SyntaxOnly(string rationale)
        {
            return new OperationKindCoverageDecision(OperationKindCoverageClassification.SyntaxOnlyFallback, rationale);
        }

        private static OperationKind OperationKindValue(string name)
        {
            return (OperationKind)Enum.Parse(typeof(OperationKind), name);
        }

        private sealed record OperationKindCoverageDecision(OperationKindCoverageClassification Classification, string Rationale);

        private enum OperationKindCoverageClassification
        {
            Handled,
            ParentHandled,
            IntentionallyConservative,
            CSharpNotApplicable,
            SyntaxOnlyFallback
        }

        private sealed record RegisteredRuleOperationKind(string RuleName, OperationKind OperationKind);

        private sealed record AnalyzerActionSurface(string Name, AnalyzerActionSurfaceDecision Decision, string Rationale);

        private enum AnalyzerActionSurfaceDecision
        {
            Used,
            NotUsed
        }

        public sealed record OperationCorpusSnippet(
            string Name,
            string Source,
            bool AllowUnsafe,
            ImmutableHashSet<OperationKind> ExpectedOperationKinds)
        {
            public OperationCorpusSnippet(string name, string source, bool allowUnsafe, params OperationKind[] expectedOperationKinds)
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
}
