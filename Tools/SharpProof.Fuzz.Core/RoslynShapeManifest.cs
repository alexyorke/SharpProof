using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Analyzer;

namespace SharpProof.Tools.Fuzz;

public enum RoslynShapeSurface
{
    OperationKind,
    SyntaxKind
}

public enum ShapeClassification
{
    Handled,
    GeneratorBacked,
    ParentHandled,
    SyntaxShadow,
    TokenOnly,
    TriviaOnly,
    IntentionallyConservative,
    CSharpNotApplicable
}

public enum AnalyzerActionSurfaceDecision
{
    Used,
    NotUsed
}

public sealed record RoslynShapeManifestEntry(
    RoslynShapeSurface Surface,
    string ShapeId,
    string DisplayName,
    ShapeClassification Classification,
    string Rationale);

public sealed record AnalyzerActionSurfaceManifestEntry(
    string Name,
    AnalyzerActionSurfaceDecision Decision,
    string Rationale);

public static class RoslynShapeManifest
{
    private static readonly ImmutableHashSet<OperationKind> ParentHandledOperationKinds =
        ImmutableHashSet.Create(
            OperationKind.None,
            OperationKind.MethodReference,
            OperationKind.UnaryOperator,
            OperationKind.BinaryOperator,
            OperationKind.BinaryPattern,
            OperationKind.Branch,
            OperationKind.Parenthesized,
            OperationKind.ConditionalAccessInstance,
            OperationKind.Empty,
            OperationKind.FlowAnonymousFunction,
            OperationKind.Labeled,
            OperationKind.Loop,
            OperationKind.MemberInitializer,
            OperationKind.PropertyInitializer,
            OperationKind.TranslatedQuery,
            OperationKind.DeclarationExpression,
            OperationKind.OmittedArgument,
            OperationKind.ParameterInitializer,
            OperationKind.SwitchCase,
            OperationKind.InterpolatedStringText,
            OperationKind.Interpolation,
            OperationKind.TupleBinary,
            OperationKind.TupleBinaryOperator,
            OperationKind.MethodBody,
            OperationKind.ConstructorBody,
            OperationKind.Discard,
            OperationKind.FlowCapture,
            OperationKind.FlowCaptureReference,
            OperationKind.IsNull,
            OperationKind.CaughtException,
            OperationKind.StaticLocalInitializationSemaphore,
            OperationKind.SwitchExpressionArm,
            OperationKind.YieldBreak,
            OperationKindValue("CollectionElementInitializer"));

    private static readonly ImmutableHashSet<OperationKind> CSharpNotApplicableOperationKinds =
        ImmutableHashSet.Create(
            OperationKind.Stop,
            OperationKind.End,
            OperationKind.RaiseEvent,
            OperationKind.ReDim,
            OperationKind.ReDimClause);

    private static readonly ImmutableHashSet<OperationKind> SyntaxShadowOperationKinds =
        ImmutableHashSet.Create(OperationKind.Attribute);

    private static readonly ImmutableHashSet<OperationKind> ConservativeOperationKinds =
        ImmutableHashSet.Create(
            OperationKind.Invalid,
            OperationKind.AddressOf,
            OperationKind.InterpolatedStringHandlerCreation,
            OperationKind.InterpolatedStringAddition,
            OperationKind.InterpolatedStringAppendLiteral,
            OperationKind.InterpolatedStringAppendFormatted,
            OperationKind.InterpolatedStringAppendInvalid,
            OperationKind.InterpolatedStringHandlerArgumentPlaceholder,
            OperationKind.FunctionPointerInvocation);

    private static readonly ImmutableHashSet<string> SyntaxShadowKindNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            nameof(SyntaxKind.Attribute),
            nameof(SyntaxKind.AttributeList),
            nameof(SyntaxKind.AttributeArgument),
            nameof(SyntaxKind.AttributeArgumentList),
            nameof(SyntaxKind.ClassDeclaration),
            nameof(SyntaxKind.RecordDeclaration),
            nameof(SyntaxKind.RecordStructDeclaration),
            nameof(SyntaxKind.StructDeclaration),
            nameof(SyntaxKind.InterfaceDeclaration),
            nameof(SyntaxKind.EnumDeclaration),
            nameof(SyntaxKind.DelegateDeclaration),
            nameof(SyntaxKind.NamespaceDeclaration),
            nameof(SyntaxKind.FileScopedNamespaceDeclaration),
            nameof(SyntaxKind.UsingDirective),
            nameof(SyntaxKind.ExternAliasDirective),
            nameof(SyntaxKind.GlobalStatement),
            nameof(SyntaxKind.SingleLineDocumentationCommentTrivia),
            nameof(SyntaxKind.MultiLineDocumentationCommentTrivia),
            nameof(SyntaxKind.DocumentationCommentExteriorTrivia),
            nameof(SyntaxKind.XmlElement),
            nameof(SyntaxKind.XmlEmptyElement),
            nameof(SyntaxKind.XmlText),
            nameof(SyntaxKind.XmlTextLiteralToken),
            nameof(SyntaxKind.XmlName),
            nameof(SyntaxKind.XmlPrefix),
            nameof(SyntaxKind.XmlCDataSection),
            nameof(SyntaxKind.XmlComment),
            nameof(SyntaxKind.XmlProcessingInstruction),
            nameof(SyntaxKind.XmlElementStartTag),
            nameof(SyntaxKind.XmlElementEndTag),
            nameof(SyntaxKind.DefineDirectiveTrivia),
            nameof(SyntaxKind.UndefDirectiveTrivia),
            nameof(SyntaxKind.IfDirectiveTrivia),
            nameof(SyntaxKind.ElifDirectiveTrivia),
            nameof(SyntaxKind.ElseDirectiveTrivia),
            nameof(SyntaxKind.EndIfDirectiveTrivia),
            nameof(SyntaxKind.RegionDirectiveTrivia),
            nameof(SyntaxKind.EndRegionDirectiveTrivia),
            nameof(SyntaxKind.ErrorDirectiveTrivia),
            nameof(SyntaxKind.WarningDirectiveTrivia),
            nameof(SyntaxKind.LineDirectiveTrivia),
            nameof(SyntaxKind.PragmaWarningDirectiveTrivia),
            nameof(SyntaxKind.PragmaChecksumDirectiveTrivia),
            nameof(SyntaxKind.ReferenceDirectiveTrivia),
            nameof(SyntaxKind.LoadDirectiveTrivia),
            nameof(SyntaxKind.ShebangDirectiveTrivia),
            nameof(SyntaxKind.NullableDirectiveTrivia),
            nameof(SyntaxKind.BadDirectiveTrivia),
            nameof(SyntaxKind.SkippedTokensTrivia),
            nameof(SyntaxKind.PrimaryConstructorBaseType));

    public static ImmutableArray<RoslynShapeManifestEntry> OperationEntries { get; } = BuildOperationEntries();

    public static ImmutableArray<RoslynShapeManifestEntry> SyntaxEntries { get; } = BuildSyntaxEntries();

    public static ImmutableDictionary<string, RoslynShapeManifestEntry> EntriesByShapeId { get; } =
        OperationEntries
            .Concat(SyntaxEntries)
            .ToImmutableDictionary(entry => entry.ShapeId, StringComparer.Ordinal);

    public static ImmutableArray<AnalyzerActionSurfaceManifestEntry> ActionSurfaceEntries { get; } =
        ImmutableArray.Create(
            new AnalyzerActionSurfaceManifestEntry("CompilationStart", AnalyzerActionSurfaceDecision.Used,
                "Analyzer configuration and shared state are initialized at compilation start."),
            new AnalyzerActionSurfaceManifestEntry("CompilationEnd", AnalyzerActionSurfaceDecision.Used,
                "Compilation-wide configuration and additional-file issues are reported before shared state is disposed."),
            new AnalyzerActionSurfaceManifestEntry("Operation", AnalyzerActionSurfaceDecision.NotUsed,
                "Feature checks consume one cached method-body snapshot instead of registering independent operation callbacks."),
            new AnalyzerActionSurfaceManifestEntry("OperationBlock", AnalyzerActionSurfaceDecision.Used,
                "Executable method-like bodies create one shared root, semantic-fact snapshot, and symbolic-query cache."),
            new AnalyzerActionSurfaceManifestEntry("OperationBlockStart", AnalyzerActionSurfaceDecision.NotUsed,
                "A one-shot operation-block action is sufficient; features do not need separate incremental operation callbacks."),
            new AnalyzerActionSurfaceManifestEntry("SemanticModel", AnalyzerActionSurfaceDecision.NotUsed,
                "Semantic-model actions are not directly registered; semantic models are consumed from other action contexts."),
            new AnalyzerActionSurfaceManifestEntry("Symbol", AnalyzerActionSurfaceDecision.NotUsed,
                "Method ownership comes from operation blocks; declarations without executable blocks use syntax fallbacks."),
            new AnalyzerActionSurfaceManifestEntry("SyntaxNode", AnalyzerActionSurfaceDecision.Used,
                "Attribute placement, requires call sites, and bodyless declaration fallbacks remain syntax-based."),
            new AnalyzerActionSurfaceManifestEntry("SyntaxTree", AnalyzerActionSurfaceDecision.Used,
                "Per-tree analyzer configuration is validated through a syntax-tree action."));

    public static ImmutableArray<string> GeneratorBackedShapeIds { get; } =
        EntriesByShapeId.Values
            .Where(entry => entry.Classification == ShapeClassification.GeneratorBacked)
            .Select(entry => entry.ShapeId)
            .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
            .ToImmutableArray();

    public static string OperationShapeId(OperationKind operationKind)
    {
        return "operation:" + operationKind;
    }

    public static string SyntaxShapeId(SyntaxKind syntaxKind)
    {
        return "syntax:" + syntaxKind;
    }

    private static ImmutableArray<RoslynShapeManifestEntry> BuildOperationEntries()
    {
        var registeredRuleKinds = GetRegisteredRuleOperationKinds();
        var generatorBackedShapeIds = FuzzCaseGenerator.RegistryEntries
            .SelectMany(entry => entry.PrimaryShapeIds)
            .Where(shapeId => shapeId.StartsWith("operation:", StringComparison.Ordinal))
            .ToImmutableHashSet(StringComparer.Ordinal);

        var builder = ImmutableArray.CreateBuilder<RoslynShapeManifestEntry>();
        foreach (var operationKind in Enum.GetValues<OperationKind>())
        {
            var shapeId = OperationShapeId(operationKind);
            if (generatorBackedShapeIds.Contains(shapeId))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.GeneratorBacked,
                    "Deterministically targetable through the manifest-backed fuzz registry."));
                continue;
            }

            if (registeredRuleKinds.Contains(operationKind))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.Handled,
                    "Handled by a registered analyzer rule, but not a primary fuzz target."));
                continue;
            }

            if (ParentHandledOperationKinds.Contains(operationKind))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.ParentHandled,
                    "Covered through the containing operation or control-flow structure."));
                continue;
            }

            if (SyntaxShadowOperationKinds.Contains(operationKind))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.SyntaxShadow,
                    "Declaration-only coverage is handled by syntax or attribute checks rather than executable fuzz generation."));
                continue;
            }

            if (CSharpNotApplicableOperationKinds.Contains(operationKind))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.CSharpNotApplicable,
                    "Visual Basic-only surface; explicitly out of scope for C# shape generation."));
                continue;
            }

            if (ConservativeOperationKinds.Contains(operationKind))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.OperationKind,
                    shapeId,
                    operationKind.ToString(),
                    ShapeClassification.IntentionallyConservative,
                    "Known executable surface that remains conservative until a tighter rule is implemented."));
                continue;
            }

            builder.Add(new RoslynShapeManifestEntry(
                RoslynShapeSurface.OperationKind,
                shapeId,
                operationKind.ToString(),
                ShapeClassification.IntentionallyConservative,
                "Explicitly classified but not yet assigned a dedicated generator or parent-handled rule."));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<RoslynShapeManifestEntry> BuildSyntaxEntries()
    {
        var builder = ImmutableArray.CreateBuilder<RoslynShapeManifestEntry>();
        foreach (var syntaxKind in Enum.GetValues<SyntaxKind>())
        {
            var shapeId = SyntaxShapeId(syntaxKind);
            var name = syntaxKind.ToString();
            if (syntaxKind == SyntaxKind.None)
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.SyntaxKind,
                    shapeId,
                    name,
                    ShapeClassification.CSharpNotApplicable,
                    "Sentinel syntax value; no parseable source shape exists."));
                continue;
            }

            if (SyntaxShadowKindNames.Contains(name))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.SyntaxKind,
                    shapeId,
                    name,
                    ShapeClassification.SyntaxShadow,
                    "Declaration, directive, or structured-trivia syntax is tracked outside executable operation generation."));
                continue;
            }

            if (IsTriviaOnlyKind(name))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.SyntaxKind,
                    shapeId,
                    name,
                    ShapeClassification.TriviaOnly,
                    "Trivia or structured-trivia syntax is not an executable fuzz target."));
                continue;
            }

            if (IsTokenOnlyKind(name))
            {
                builder.Add(new RoslynShapeManifestEntry(
                    RoslynShapeSurface.SyntaxKind,
                    shapeId,
                    name,
                    ShapeClassification.TokenOnly,
                    "Token-level syntax is covered as part of containing parse trees, not standalone generation targets."));
                continue;
            }

            builder.Add(new RoslynShapeManifestEntry(
                RoslynShapeSurface.SyntaxKind,
                shapeId,
                name,
                ShapeClassification.ParentHandled,
                "Executable or parser-level syntax is covered through the containing operation tree or generated program shape."));
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<OperationKind> GetRegisteredRuleOperationKinds()
    {
        var analyzerAssembly = typeof(SharpProofAnalyzer).Assembly;
        var registryType = analyzerAssembly.GetType("SharpProof.Analyzer.Engine.Rules.RuleRegistry", true)!;
        var getDefaultRulesMethod =
            registryType.GetMethod("GetDefaultRules", BindingFlags.Public | BindingFlags.Static)!;
        var rules = (IEnumerable)getDefaultRulesMethod.Invoke(null, null)!;
        var builder = ImmutableHashSet.CreateBuilder<OperationKind>();

        foreach (var rule in rules)
        {
            var applicableOperationKindsProperty = rule.GetType().GetProperty(
                "ApplicableOperationKinds",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var operationKinds = (IEnumerable)applicableOperationKindsProperty.GetValue(rule)!;
            foreach (OperationKind operationKind in operationKinds) builder.Add(operationKind);
        }

        return builder.ToImmutable();
    }

    private static bool IsTokenOnlyKind(string name)
    {
        return name.EndsWith("Token", StringComparison.Ordinal) ||
               name.EndsWith("Keyword", StringComparison.Ordinal);
    }

    private static bool IsTriviaOnlyKind(string name)
    {
        return name.EndsWith("Trivia", StringComparison.Ordinal) ||
               name.StartsWith("Xml", StringComparison.Ordinal);
    }

    private static OperationKind OperationKindValue(string name)
    {
        return (OperationKind)Enum.Parse(typeof(OperationKind), name);
    }
}
