using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace SharpProof.ArchitectureTest;
/// <summary>
/// Finds framework metadata names represented by source expressions.
/// </summary>
/// <remarks>
/// The architecture gate intentionally scans source rather than compiled
/// assemblies. Roslyn's constant-value service gives the scan a bounded
/// semantic view without needing to restore or build every production
/// project. The fallback shape check covers non-constant interpolated
/// prefixes while keeping path and DLL asset names out of the result.
/// </remarks>
internal static class FrameworkIdentityScanner
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
    internal static string[] ReadInventory(
        string path,
        string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path);
        var compilation = CreateCompilation([tree]);
        var model = compilation.GetSemanticModel(tree);
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in tree.GetRoot()
                     .DescendantNodes()
                     .OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not ExpressionSyntax value)
                {
                    continue;
                }
                if (TryGetConstantString(model, value, out var constant))
                {
                    values.Add(constant);
                    continue;
                }
                foreach (var literal in value
                             .DescendantNodesAndSelf()
                             .OfType<LiteralExpressionSyntax>()
                             .Where(static literal => literal.IsKind(
                                 SyntaxKind.StringLiteralExpression)))
                {
                    values.Add(literal.Token.ValueText);
                }
            }
        }
        return [.. values
            .Where(static value => value.StartsWith(
                "System.",
                StringComparison.Ordinal))
            .OrderBy(static value => value, StringComparer.Ordinal)];
    }
    internal static string[] FindViolations(
        IEnumerable<(string Path, string Source)> files,
        IEnumerable<string> inventory,
        IEnumerable<string> approvedPaths)
    {
        var identityInventory = new HashSet<string>(
            inventory,
            StringComparer.Ordinal);
        var approved = new HashSet<string>(
            approvedPaths.Select(NormalizePath),
            StringComparer.Ordinal);
        var sourceFiles = files
            .Select(file => (Path: NormalizePath(file.Path), file.Source))
            .ToArray();
        var violations = new List<string>();
        var scanFiles = sourceFiles
            .Where(file => !approved.Contains(file.Path))
            .ToArray();
        var trees = scanFiles
            .Select(file => CSharpSyntaxTree.ParseText(
                file.Source,
                ParseOptions,
                file.Path))
            .ToArray();
        var compilation = CreateCompilation(trees);
        for (var index = 0; index < trees.Length; index++)
        {
            var tree = trees[index];
            var model = compilation.GetSemanticModel(tree);
            ScanTree(
                scanFiles[index],
                tree,
                model,
                identityInventory,
                violations);
        }
        return [.. violations
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)];
    }
    private static void ScanTree(
        (string Path, string Source) sourceFile,
        SyntaxTree tree,
        SemanticModel model,
        HashSet<string> identityInventory,
        List<string> violations)
    {
        var root = tree.GetRoot();
        var reportedSpans = new HashSet<int>();
        foreach (var expression in root
                     .DescendantNodes()
                     .OfType<ExpressionSyntax>()
                     .Where(IsCandidateExpression))
        {
            if (TryGetConstantString(model, expression, out var constant) &&
                IsFrameworkIdentity(constant, identityInventory) &&
                IsOutermostConstantExpression(model, expression, constant) &&
                reportedSpans.Add(expression.SpanStart))
            {
                AddViolation(
                    violations,
                    sourceFile.Path,
                    expression,
                    constant);
            }
        }
        foreach (var interpolation in root
                     .DescendantNodes()
                     .OfType<InterpolatedStringExpressionSyntax>())
        {
            if (model.GetConstantValue(interpolation) is
                { HasValue: true, Value: string constant } ||
                !HasFrameworkPrefix(interpolation))
            {
                continue;
            }
            if (reportedSpans.Add(interpolation.SpanStart))
            {
                AddViolation(
                    violations,
                    sourceFile.Path,
                    interpolation,
                    "<interpolated System.* identity>");
            }
        }
    }
    private static bool IsCandidateExpression(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
                   literal.IsKind(SyntaxKind.StringLiteralExpression) ||
            expression is InterpolatedStringExpressionSyntax ||
            expression is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.AddExpression);
    }
    private static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> trees)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        return CSharpCompilation.Create(
            "SharpProof.FrameworkIdentityScan",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
    private static bool TryGetConstantString(
        SemanticModel model,
        ExpressionSyntax expression,
        out string value)
    {
        var constant = model.GetConstantValue(expression);
        if (constant is { HasValue: true, Value: string text })
        {
            value = text;
            return true;
        }
        value = string.Empty;
        return false;
    }
    private static bool IsOutermostConstantExpression(
        SemanticModel model,
        ExpressionSyntax expression,
        string value)
    {
        if (expression.Parent is not ExpressionSyntax parent)
        {
            return true;
        }
        var parentConstant = model.GetConstantValue(parent);
        return !parentConstant.HasValue ||
            parentConstant.Value is not string parentValue ||
            !string.Equals(parentValue, value, StringComparison.Ordinal);
    }
    private static bool IsFrameworkIdentity(
        string value,
        HashSet<string> inventory)
    {
        if (value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (inventory.Contains(value))
        {
            return true;
        }
        return LooksLikeMetadataName(value);
    }
    private static bool LooksLikeMetadataName(string value)
    {
        if (!value.StartsWith("System.", StringComparison.Ordinal) ||
            value.Length <= "System.".Length)
        {
            return false;
        }
        var segmentStart = "System.".Length;
        for (var index = segmentStart; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '.' or '+' or '`' ||
                char.IsLetterOrDigit(character) ||
                character is '_' or '$')
            {
                if (character == '.' &&
                    (index == segmentStart || index == value.Length - 1 ||
                     value[index - 1] == '.' || value[index + 1] == '.'))
                {
                    return false;
                }
                continue;
            }
            return false;
        }
        return true;
    }
    private static bool HasFrameworkPrefix(
        InterpolatedStringExpressionSyntax interpolation)
    {
        var firstText = interpolation.Contents
            .OfType<InterpolatedStringTextSyntax>()
            .FirstOrDefault();
        return firstText != null &&
            firstText.TextToken.ValueText.StartsWith(
                "System.",
                StringComparison.Ordinal) &&
            !firstText.TextToken.ValueText.EndsWith(
                ".dll",
                StringComparison.OrdinalIgnoreCase);
    }
    private static void AddViolation(
        List<string> violations,
        string path,
        SyntaxNode node,
        string value)
    {
        var line = node.GetLocation()
            .GetLineSpan()
            .StartLinePosition.Line + 1;
        violations.Add($"{path}:{line}: {value}");
    }
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
