using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer.Configuration
{
    internal sealed class DiagnosticBaseline
    {
        private const string BaselineFileName = "SharpProof.Baseline.json";
        private static readonly Regex PropertyRegex = new Regex(@"""(?<name>id|diagnosticId|symbol|path)""\s*:\s*""(?<value>(?:\\.|[^""])*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static readonly DiagnosticBaseline Empty = new DiagnosticBaseline(ImmutableArray<BaselineEntry>.Empty);

        private readonly ImmutableArray<BaselineEntry> _entries;

        private DiagnosticBaseline(ImmutableArray<BaselineEntry> entries)
        {
            _entries = entries;
        }

        public static DiagnosticBaseline FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (!string.Equals(System.IO.Path.GetFileName(additionalFile.Path), BaselineFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = additionalFile.GetText(cancellationToken)?.ToString();
                if (text == null || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var entry in ParseEntries(text, additionalFile.Path))
                {
                    builder.Add(entry);
                }
            }

            return builder.Count == 0 ? Empty : new DiagnosticBaseline(builder.ToImmutable());
        }

        public bool IsSuppressed(string diagnosticId, ISymbol symbol, SyntaxTree syntaxTree)
        {
            if (_entries.IsDefaultOrEmpty)
            {
                return false;
            }

            var symbolIds = GetSymbolIds(symbol);
            var sourcePath = syntaxTree.FilePath ?? string.Empty;

            foreach (var entry in _entries)
            {
                foreach (var symbolId in symbolIds)
                {
                    if (entry.Matches(diagnosticId, symbolId, sourcePath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static ImmutableArray<string> GetSymbolIds(ISymbol symbol)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            var documentationId = DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition);
            if (!string.IsNullOrWhiteSpace(documentationId))
            {
                builder.Add(documentationId!);
            }

            builder.Add(symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));

            if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
            {
                var containingType = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                var methodName = methodSymbol.MetadataName == ".ctor" ? "#ctor" : methodSymbol.MetadataName;
                builder.Add("M:" + containingType + "." + methodName);
            }

            return builder.Distinct(StringComparer.Ordinal).ToImmutableArray();
        }

        private static ImmutableArray<BaselineEntry> ParseEntries(string json, string baselinePath)
        {
            var builder = ImmutableArray.CreateBuilder<BaselineEntry>();
            var baseDirectory = GetBaseDirectory(baselinePath);
            foreach (var objectBody in EnumerateObjectBodies(json))
            {
                if (ContainsUnquotedBrace(objectBody))
                {
                    continue;
                }

                string? id = null;
                string? symbol = null;
                string? path = null;

                foreach (Match propertyMatch in PropertyRegex.Matches(objectBody))
                {
                    var name = propertyMatch.Groups["name"].Value;
                    var value = UnescapeJsonString(propertyMatch.Groups["value"].Value);
                    if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "diagnosticId", StringComparison.OrdinalIgnoreCase))
                    {
                        id = value;
                    }
                    else if (string.Equals(name, "symbol", StringComparison.OrdinalIgnoreCase))
                    {
                        symbol = value;
                    }
                    else if (string.Equals(name, "path", StringComparison.OrdinalIgnoreCase))
                    {
                        path = value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(id) &&
                    !string.IsNullOrWhiteSpace(symbol) &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    builder.Add(new BaselineEntry(id!, symbol!, path!, baseDirectory));
                }
            }

            return builder.ToImmutable();
        }

        private static string GetBaseDirectory(string baselinePath)
        {
            if (string.IsNullOrWhiteSpace(baselinePath))
            {
                return string.Empty;
            }

            var directory = System.IO.Path.GetDirectoryName(baselinePath);
            return string.IsNullOrWhiteSpace(directory) ? string.Empty : NormalizePath(directory!);
        }

        private static IEnumerable<string> EnumerateObjectBodies(string json)
        {
            var objectStarts = new Stack<int>();
            var inString = false;
            var escaped = false;

            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == '{')
                {
                    objectStarts.Push(i + 1);
                }
                else if (ch == '}' && objectStarts.Count > 0)
                {
                    var start = objectStarts.Pop();
                    yield return json.Substring(start, i - start);
                }
            }
        }

        private static bool ContainsUnquotedBrace(string value)
        {
            var inString = false;
            var escaped = false;

            foreach (var ch in value)
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == '{' || ch == '}')
                {
                    return true;
                }
            }

            return false;
        }

        private static string UnescapeJsonString(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(ch);
                    continue;
                }

                var escape = value[++i];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u' when i + 4 < value.Length:
                        var unicodeValue = 0;
                        var validUnicodeEscape = true;
                        for (var j = 1; j <= 4; j++)
                        {
                            var digit = HexValue(value[i + j]);
                            if (digit < 0)
                            {
                                validUnicodeEscape = false;
                                break;
                            }

                            unicodeValue = (unicodeValue << 4) + digit;
                        }

                        if (validUnicodeEscape)
                        {
                            builder.Append((char)unicodeValue);
                            i += 4;
                        }
                        else
                        {
                            builder.Append("\\u");
                        }

                        break;
                    default:
                        builder.Append('\\').Append(escape);
                        break;
                }
            }

            return builder.ToString();
        }

        private static int HexValue(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                return ch - '0';
            }

            if (ch >= 'a' && ch <= 'f')
            {
                return ch - 'a' + 10;
            }

            if (ch >= 'A' && ch <= 'F')
            {
                return ch - 'A' + 10;
            }

            return -1;
        }

        private static string NormalizePath(string path)
        {
            var normalized = path.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        private readonly struct BaselineEntry
        {
            public BaselineEntry(string diagnosticId, string symbolId, string path, string baseDirectory)
            {
                DiagnosticId = diagnosticId;
                SymbolId = symbolId;
                Path = NormalizePath(path);
                AbsolutePath = MakeAbsolutePath(path, baseDirectory);
            }

            private string DiagnosticId { get; }
            private string SymbolId { get; }
            private string Path { get; }
            private string AbsolutePath { get; }

            public bool Matches(string diagnosticId, string symbolId, string sourcePath)
            {
                return string.Equals(DiagnosticId, diagnosticId, StringComparison.Ordinal) &&
                       string.Equals(SymbolId, symbolId, StringComparison.Ordinal) &&
                       MatchesPath(sourcePath);
            }

            private bool MatchesPath(string sourcePath)
            {
                var normalizedSourcePath = NormalizePath(sourcePath);
                return string.Equals(Path, normalizedSourcePath, StringComparison.OrdinalIgnoreCase) ||
                       (!string.IsNullOrWhiteSpace(AbsolutePath) &&
                        string.Equals(AbsolutePath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase));
            }

            private static string MakeAbsolutePath(string path, string baseDirectory)
            {
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    return string.Empty;
                }

                if (System.IO.Path.IsPathRooted(path))
                {
                    return NormalizePath(path);
                }

                return NormalizePath(System.IO.Path.Combine(baseDirectory, path));
            }
        }
    }
}
