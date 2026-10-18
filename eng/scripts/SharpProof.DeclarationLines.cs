using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class SharpProofDeclarationLines
{
    public static int Main(string[] args)
    {
        using var request = JsonDocument.Parse(File.ReadAllText(args[0]));
        var results = new List<object>();
        foreach (var item in request.RootElement.EnumerateArray())
        {
            string path = item.GetProperty("path").GetString();
            string source = File.ReadAllText(item.GetProperty("sourcePath").GetString());
            string version = item.GetProperty("languageVersion").GetString();
            if (!LanguageVersionFacts.TryParse(version, out var languageVersion))
                throw new InvalidDataException("Unknown evaluated C# language version: " + version);
            var symbols = item.GetProperty("preprocessorSymbols").EnumerateArray()
                .Select(value => value.GetString());
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(languageVersion, preprocessorSymbols: symbols));
            var text = tree.GetText();
            var root = tree.GetRoot();
            var safe = new List<TextSpan>();
            var unsafeSpans = new List<TextSpan>();
            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case BaseTypeDeclarationSyntax type when type.OpenBraceToken.RawKind != 0 && !type.OpenBraceToken.IsMissing:
                        safe.Add(TextSpan.FromBounds(type.SpanStart, type.OpenBraceToken.SpanStart));
                        // Primary constructors can perform implicit assignments.
                        if (type is TypeDeclarationSyntax declaration && declaration.ParameterList != null)
                            unsafeSpans.Add(declaration.ParameterList.Span);
                        break;
                    case BaseMethodDeclarationSyntax:
                    case DelegateDeclarationSyntax:
                    case BaseFieldDeclarationSyntax:
                    case PropertyDeclarationSyntax:
                    case EventDeclarationSyntax:
                    case EnumMemberDeclarationSyntax:
                        safe.Add(node.Span);
                        break;
                }
                // These regions have executable behavior or affect it without
                // an ordinary sequence point. Never classify their tokens as
                // declaration-only, even when they share a header's line.
                // A parameter default is a compile-time constant. Unlike a field
                // initializer, it has no runtime sequence point to cover.
                if (node is BlockSyntax or ArrowExpressionClauseSyntax or
                    AccessorListSyntax or ConstructorInitializerSyntax or
                    PrimaryConstructorBaseTypeSyntax or AttributeListSyntax ||
                    node is EqualsValueClauseSyntax initializer &&
                    initializer.Parent is not ParameterSyntax)
                    unsafeSpans.Add(node.Span);
            }
            var tokensByLine = new Dictionary<int, List<SyntaxToken>>();
            foreach (var token in root.DescendantTokens())
            {
                if (token.Span.Length == 0)
                    continue;
                int start = text.Lines.GetLineFromPosition(token.SpanStart).LineNumber + 1;
                int end = text.Lines.GetLineFromPosition(token.Span.End - 1).LineNumber + 1;
                for (int line = start; line <= end; line++)
                {
                    if (!tokensByLine.TryGetValue(line, out var tokens))
                        tokensByLine[line] = tokens = new List<SyntaxToken>();
                    tokens.Add(token);
                }
            }
            bool valid = !tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
            int[] lines = valid ? tokensByLine.Where(pair => pair.Value.All(token =>
                safe.Any(span => span.Contains(token.Span)) &&
                !unsafeSpans.Any(span => span.OverlapsWith(token.Span))))
                .Select(pair => pair.Key).OrderBy(line => line).ToArray() : Array.Empty<int>();
            results.Add(new { path, declarationLines = lines, validSyntax = valid });
        }
        File.WriteAllText(args[1], JsonSerializer.Serialize(results));
        return 0;
    }
}
