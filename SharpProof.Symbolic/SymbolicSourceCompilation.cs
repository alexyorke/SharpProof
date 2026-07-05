using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Symbolic
{
    internal static class SymbolicSourceCompilation
    {
        public static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }

            return trustedPlatformAssemblies!
                .Split(Path.PathSeparator)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray<MetadataReference>();
        }

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
            var referenceArray = references?.ToImmutableArray();
            if (!referenceArray.HasValue || referenceArray.Value.IsDefaultOrEmpty)
            {
                referenceArray = GetTrustedPlatformReferences();
            }
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                referenceArray.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return (syntaxTree, compilation);
        }
    }
}
