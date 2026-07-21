using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Tools.Fuzz;

public enum RoslynShapeSurface {
    OperationKind,
    SyntaxKind
}

public enum ShapeClassification {
    Handled,
    GeneratorBacked,
    ParentHandled,
    SyntaxShadow,
    TokenOnly,
    TriviaOnly,
    IntentionallyConservative,
    CSharpNotApplicable
}

public sealed record RoslynShapeManifestEntry(
    RoslynShapeSurface Surface,
    string ShapeId,
    string DisplayName,
    ShapeClassification Classification);

public static class RoslynShapeManifest {
    private static readonly ImmutableHashSet<OperationKind> RegisteredRuleKinds =
        ImmutableHashSet<OperationKind>.Empty;

    private static readonly ImmutableHashSet<string> GeneratorBackedOperationShapeIds =
        FuzzCaseGenerator.RegistryEntries
            .SelectMany(static entry => entry.PrimaryShapeIds)
            .Where(static shapeId => shapeId.StartsWith("operation:", StringComparison.Ordinal))
            .ToImmutableHashSet(StringComparer.Ordinal);

    public static ImmutableArray<RoslynShapeManifestEntry> OperationEntries { get; } =
        Enum.GetValues<OperationKind>().Select(CreateOperationEntry).ToImmutableArray();

    public static ImmutableArray<RoslynShapeManifestEntry> SyntaxEntries { get; } =
        Enum.GetValues<SyntaxKind>().Select(CreateSyntaxEntry).ToImmutableArray();

    public static ImmutableDictionary<string, RoslynShapeManifestEntry> EntriesByShapeId { get; } =
        OperationEntries.Concat(SyntaxEntries).ToImmutableDictionary(static entry => entry.ShapeId, StringComparer.Ordinal);

    public static ImmutableDictionary<string, bool> ActionSurfaceEntries { get; } =
        new[] {
            "CompilationStart", "CompilationEnd", "Operation", "OperationBlock", "OperationBlockStart",
            "SemanticModel", "Symbol", "SyntaxNode", "SyntaxTree"
        }.ToImmutableDictionary(static name => name, IsActionSurfaceUsed, StringComparer.Ordinal);

    public static ImmutableArray<string> GeneratorBackedShapeIds { get; } =
        EntriesByShapeId.Values
            .Where(static entry => entry.Classification == ShapeClassification.GeneratorBacked)
            .Select(static entry => entry.ShapeId)
            .OrderBy(static shapeId => shapeId, StringComparer.Ordinal)
            .ToImmutableArray();

    public static bool IsActionableUnobservedOperationKind(OperationKind kind) {
        if (kind is OperationKind.Invalid or OperationKind.InterpolatedStringAppendInvalid ||
            IsParentHandled(kind))
            return false;

        return EntriesByShapeId.TryGetValue(OperationShapeId(kind), out var entry) &&
               entry.Classification is not (ShapeClassification.ParentHandled or
                   ShapeClassification.CSharpNotApplicable or ShapeClassification.SyntaxShadow);
    }

    public static string OperationShapeId(OperationKind operationKind) => "operation:" + operationKind;

    public static string SyntaxShapeId(SyntaxKind syntaxKind) => "syntax:" + syntaxKind;

    private static RoslynShapeManifestEntry CreateOperationEntry(OperationKind kind) {
        var classification = ClassifyOperation(kind);
        return new RoslynShapeManifestEntry(
            RoslynShapeSurface.OperationKind,
            OperationShapeId(kind),
            kind.ToString(),
            classification);
    }

    private static ShapeClassification ClassifyOperation(OperationKind kind) {
        if (GeneratorBackedOperationShapeIds.Contains(OperationShapeId(kind)))
            return ShapeClassification.GeneratorBacked;
        if (RegisteredRuleKinds.Contains(kind))
            return ShapeClassification.Handled;
        if (IsParentHandled(kind))
            return ShapeClassification.ParentHandled;
        if (kind == OperationKind.Attribute)
            return ShapeClassification.SyntaxShadow;
        if (kind is OperationKind.Stop or OperationKind.End or OperationKind.RaiseEvent or
            OperationKind.ReDim or OperationKind.ReDimClause)
            return ShapeClassification.CSharpNotApplicable;
        return ShapeClassification.IntentionallyConservative;
    }

    private static bool IsParentHandled(OperationKind kind) => kind is
        OperationKind.None or
        OperationKind.MethodReference or
        OperationKind.UnaryOperator or
        OperationKind.BinaryOperator or
        OperationKind.BinaryPattern or
        OperationKind.Branch or
        OperationKind.Parenthesized or
        OperationKind.ConditionalAccessInstance or
        OperationKind.Empty or
        OperationKind.FlowAnonymousFunction or
        OperationKind.Labeled or
        OperationKind.Loop or
        OperationKind.MemberInitializer or
        OperationKind.PropertyInitializer or
        OperationKind.TranslatedQuery or
        OperationKind.DeclarationExpression or
        OperationKind.OmittedArgument or
        OperationKind.ParameterInitializer or
        OperationKind.SwitchCase or
        OperationKind.InterpolatedStringText or
        OperationKind.Interpolation or
        OperationKind.TupleBinary or
        OperationKind.TupleBinaryOperator or
        OperationKind.MethodBody or
        OperationKind.ConstructorBody or
        OperationKind.Discard or
        OperationKind.FlowCapture or
        OperationKind.FlowCaptureReference or
        OperationKind.IsNull or
        OperationKind.CaughtException or
        OperationKind.StaticLocalInitializationSemaphore or
        OperationKind.SwitchExpressionArm or
        OperationKind.YieldBreak ||
        kind == OperationKindValue("CollectionElementInitializer");

    private static RoslynShapeManifestEntry CreateSyntaxEntry(SyntaxKind kind) {
        var name = kind.ToString();
        var classification = kind == SyntaxKind.None
            ? ShapeClassification.CSharpNotApplicable
            : IsSyntaxShadow(name)
                ? ShapeClassification.SyntaxShadow
                : IsTriviaOnlyKind(name)
                    ? ShapeClassification.TriviaOnly
                    : IsTokenOnlyKind(name)
                        ? ShapeClassification.TokenOnly
                        : ShapeClassification.ParentHandled;
        return new RoslynShapeManifestEntry(
            RoslynShapeSurface.SyntaxKind,
            SyntaxShapeId(kind),
            name,
            classification);
    }

    private static bool IsSyntaxShadow(string name) =>
        name.StartsWith("Xml", StringComparison.Ordinal) ||
        name.EndsWith("DirectiveTrivia", StringComparison.Ordinal) ||
        name is nameof(SyntaxKind.Attribute) or
            nameof(SyntaxKind.AttributeList) or
            nameof(SyntaxKind.AttributeArgument) or
            nameof(SyntaxKind.AttributeArgumentList) or
            nameof(SyntaxKind.ClassDeclaration) or
            nameof(SyntaxKind.RecordDeclaration) or
            nameof(SyntaxKind.RecordStructDeclaration) or
            nameof(SyntaxKind.StructDeclaration) or
            nameof(SyntaxKind.InterfaceDeclaration) or
            nameof(SyntaxKind.EnumDeclaration) or
            nameof(SyntaxKind.DelegateDeclaration) or
            nameof(SyntaxKind.NamespaceDeclaration) or
            nameof(SyntaxKind.FileScopedNamespaceDeclaration) or
            nameof(SyntaxKind.UsingDirective) or
            nameof(SyntaxKind.ExternAliasDirective) or
            nameof(SyntaxKind.GlobalStatement) or
            nameof(SyntaxKind.SingleLineDocumentationCommentTrivia) or
            nameof(SyntaxKind.MultiLineDocumentationCommentTrivia) or
            nameof(SyntaxKind.DocumentationCommentExteriorTrivia) or
            nameof(SyntaxKind.BadDirectiveTrivia) or
            nameof(SyntaxKind.SkippedTokensTrivia) or
            nameof(SyntaxKind.PrimaryConstructorBaseType);

    private static bool IsActionSurfaceUsed(string name) =>
        name is "CompilationStart" or "CompilationEnd" or "OperationBlock" or "SyntaxNode" or "SyntaxTree";

    private static bool IsTokenOnlyKind(string name) =>
        name.EndsWith("Token", StringComparison.Ordinal) || name.EndsWith("Keyword", StringComparison.Ordinal);

    private static bool IsTriviaOnlyKind(string name) =>
        name.EndsWith("Trivia", StringComparison.Ordinal) || name.StartsWith("Xml", StringComparison.Ordinal);

    private static OperationKind OperationKindValue(string name) =>
        (OperationKind)Enum.Parse(typeof(OperationKind), name);
}
