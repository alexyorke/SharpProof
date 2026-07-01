using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicSourceCompilation
    {
        public static (SyntaxTree SyntaxTree, Compilation Compilation) Create(
            string sourceText,
            string filePath,
            string defaultFilePath,
            string assemblyName,
            IEnumerable<MetadataReference>? references,
            CancellationToken cancellationToken)
        {
            if (sourceText == null)
            {
                throw new ArgumentNullException(nameof(sourceText));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = defaultFilePath;
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                new CSharpParseOptions(LanguageVersion.Preview),
                filePath,
                cancellationToken: cancellationToken);
            var referenceArray = references?.ToImmutableArray() ?? SymbolicSourceQueryService.GetTrustedPlatformReferences();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                referenceArray,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return (syntaxTree, compilation);
        }
    }
}
