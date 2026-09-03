using Microsoft.CodeAnalysis.CSharp.Syntax;
using static SharpProof.CompilerProbe.TestAsset.CompilerProbeSourceHelpers;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSnapshot
{
    private const string CommandLineAdditionalTextTypeName =
        "Microsoft.CodeAnalysis.AdditionalTextFile";

    internal static string Create(CompilationAnalysisContext context)
    {
        var compilation = (CSharpCompilation)context.Compilation;
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.String(
"schema",
            CompilerProbeContract.SchemaName);
        json.Integer(
"schemaVersion",
            CompilerProbeContract.SchemaVersion);
        json.PropertyName("assembly");
        AppendAssembly(builder, compilation);
        json.PropertyName("options");
        AppendOptions(builder, compilation);
        json.RawArray(
"consumedOptions",
            CreateConsumedOptionRows(context));
        json.RawArray(
"syntaxTrees",
            CreateSyntaxTreeRows(compilation, context.CancellationToken));
        json.RawArray(
"portableReferences",
            CreateReferenceRows(compilation, context.CancellationToken));
        json.RawArray(
"additionalFiles",
            CreateAdditionalFileRows(context));
        json.Complete();
        return builder.ToString();
    }

    private static void AppendAssembly(
        StringBuilder builder,
        CSharpCompilation compilation)
    {
        var json = new ProbeJsonObject(builder);
        json.String(
"identity",
            compilation.Assembly.Identity.ToString());
        json.String(
"name",
            compilation.AssemblyName ?? string.Empty);
        json.Complete();
    }

    private static void AppendOptions(
        StringBuilder builder,
        CSharpCompilation compilation)
    {
        var options = compilation.Options;
        var parseOptions = compilation.SyntaxTrees
            .Select(static tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .ToArray();
        var json = new ProbeJsonObject(builder);
        json.Boolean(
"allowUnsafe",
            options.AllowUnsafe);
        json.Boolean(
"checkOverflow",
            options.CheckOverflow);
        json.Boolean(
"deterministic",
            options.Deterministic);
        json.StringArray(
"languageVersions",
            parseOptions
                .Select(static option => option.LanguageVersion.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        json.String(
"mainTypeName",
            options.MainTypeName ?? string.Empty);
        json.String(
"nullableContextOptions",
            options.NullableContextOptions.ToString());
        json.String(
"optimizationLevel",
            options.OptimizationLevel.ToString());
        json.String(
"outputKind",
            options.OutputKind.ToString());
        json.String(
"platform",
            options.Platform.ToString());
        json.StringArray(
"preprocessorSymbols",
            parseOptions
                .SelectMany(static option => option.PreprocessorSymbolNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        json.StringArray(
"specifiedLanguageVersions",
            parseOptions
                .Select(static option =>
                    option.SpecifiedLanguageVersion.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        json.StringArray(
"usings",
            options.Usings.OrderBy(
                static value => value,
                StringComparer.Ordinal));
        json.Complete();
    }

    private static IEnumerable<string> CreateConsumedOptionRows(
        CompilationAnalysisContext context)
    {
        var provider = context.Options.AnalyzerConfigOptionsProvider;
        var rows = new List<string> {
            CreateOptionRow(
                CompilerProbeContract.GlobalValueOptionKey,
                string.Empty,
                GetOption(
                    provider.GlobalOptions,
                    CompilerProbeContract.GlobalValueOptionKey)),
            CreateOptionRow(
                CompilerProbeContract.OutputPathOptionKey,
                string.Empty,
                GetOption(
                    provider.GlobalOptions,
                    CompilerProbeContract.OutputPathOptionKey))
        };
        rows.AddRange(context.Options.AdditionalFiles.Select(file =>
            CreateOptionRow(
                CompilerProbeContract.AdditionalFileMetadataOptionKey,
                NormalizePath(file.Path),
                GetOption(
                    provider.GetOptions(file),
                    CompilerProbeContract.AdditionalFileMetadataOptionKey))));
        return rows.OrderBy(static row => row, StringComparer.Ordinal);
    }

    private static string CreateOptionRow(
        string key,
        string path,
        string value)
    {
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.String("key", key);
        json.String("path", path);
        json.String("value", value);
        json.Complete();
        return builder.ToString();
    }

    private static IEnumerable<string> CreateSyntaxTreeRows(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var indexedTrees = compilation.SyntaxTrees
            .Select(static (tree, ordinal) =>
                (Tree: tree, Ordinal: ordinal, Path: NormalizePath(tree.FilePath)))
            .OrderBy(
                static item => item.Path,
                StringComparer.Ordinal)
            .ThenBy(static item => item.Ordinal);
        return indexedTrees.Select(item =>
            CreateSyntaxTreeRow(
                compilation,
                item.Tree,
                item.Ordinal,
                item.Path,
                cancellationToken));
    }

    private static string CreateSyntaxTreeRow(
        CSharpCompilation compilation,
        SyntaxTree tree,
        int ordinal,
        string path,
        CancellationToken cancellationToken)
    {
        var text = tree.GetText(cancellationToken).ToString();
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.StringArray(
"declaredSymbols",
            GetDeclaredSymbols(compilation, tree, cancellationToken));
        json.String(
"generatedKind",
            // A Compilation exposes no trustworthy pipeline-origin metadata for
            // a SyntaxTree. File names and comments are conventions that
            // handwritten source can freely imitate, so do not assert source
            // provenance from those heuristics.
            "Unknown");
        json.Integer(
"ordinal",
            ordinal);
        json.PropertyName("parseOptions");
        AppendParseOptions(builder, (CSharpParseOptions)tree.Options);
        json.String(
"path",
            path);
        json.String(
"textSha256",
            ProbeHash.Text(text));
        json.Complete();
        return builder.ToString();
    }

    private static IEnumerable<string> GetDeclaredSymbols(
        CSharpCompilation compilation,
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot(cancellationToken)
            .DescendantNodesAndSelf()
            .Where(static node =>
                node is BaseTypeDeclarationSyntax or
                    DelegateDeclarationSyntax or
                    BaseMethodDeclarationSyntax or
                    PropertyDeclarationSyntax or
                    EventDeclarationSyntax)
            .Select(node => model.GetDeclaredSymbol(node, cancellationToken))
            .Where(static symbol => symbol != null)
            .Select(static symbol => symbol!.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal);
    }

    private static void AppendParseOptions(
        StringBuilder builder,
        CSharpParseOptions options)
    {
        var json = new ProbeJsonObject(builder);
        json.String(
"documentationMode",
            options.DocumentationMode.ToString());
        json.RawArray(
"features",
            options.Features
                .OrderBy(static feature => feature.Key, StringComparer.Ordinal)
                .ThenBy(static feature => feature.Value, StringComparer.Ordinal)
                .Select(static feature =>
                    CreateFeatureRow(feature.Key, feature.Value)));
        json.String(
"kind",
            options.Kind.ToString());
        json.String(
"languageVersion",
            options.LanguageVersion.ToString());
        json.StringArray(
"preprocessorSymbols",
            options.PreprocessorSymbolNames.OrderBy(
                static value => value,
                StringComparer.Ordinal));
        json.String(
"specifiedLanguageVersion",
            options.SpecifiedLanguageVersion.ToString());
        json.Complete();
    }

    private static string CreateFeatureRow(string key, string value)
    {
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.String("key", key);
        json.String("value", value);
        json.Complete();
        return builder.ToString();
    }

    private static IEnumerable<string> CreateReferenceRows(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        return compilation.References
            .Select(reference =>
                CreateReferenceRow(
                    compilation,
                    reference,
                    cancellationToken))
            .OrderBy(static row => row, StringComparer.Ordinal);
    }

    private static string CreateReferenceRow(
        CSharpCompilation compilation,
        MetadataReference reference,
        CancellationToken cancellationToken)
    {
        return reference switch
        {
            PortableExecutableReference portable =>
                CreatePortableReferenceRow(compilation, portable),
            CompilationReference source =>
                CreateCompilationReferenceRow(
                    compilation,
                    source,
                    cancellationToken),
            _ => throw new InvalidOperationException(
                "The C# compiler probe encountered an unsupported reference.")
        };
    }

    private static string CreatePortableReferenceRow(
        CSharpCompilation compilation,
        PortableExecutableReference reference)
    {
        var path = reference.FilePath ?? string.Empty;
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.StringArray(
"aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias,
                StringComparer.Ordinal));
        json.String(
"assemblyOrModuleIdentity",
            GetReferenceIdentity(compilation, reference));
        json.String(
"display",
            NormalizePath(reference.Display ?? string.Empty));
        json.Boolean(
"embedInteropTypes",
            reference.Properties.EmbedInteropTypes);
        json.String(
"filePath",
            NormalizePath(path));
        json.String(
"fileSha256",
            GetPortableReferenceSha256(reference, path));
        json.String(
"kind",
            reference.Properties.Kind.ToString());
        json.Complete();
        return builder.ToString();
    }

    private static string CreateCompilationReferenceRow(
        CSharpCompilation compilation,
        CompilationReference reference,
        CancellationToken cancellationToken)
    {
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.StringArray(
"aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias,
                StringComparer.Ordinal));
        json.String(
"assemblyOrModuleIdentity",
            GetReferenceIdentity(compilation, reference));
        json.String(
"compilationSha256",
            CreateCompilationReferenceSha256(
                GetReferencedCompilation(reference),
                cancellationToken));
        json.String(
"display",
            NormalizePath(reference.Display ?? string.Empty));
        json.Boolean(
"embedInteropTypes",
            reference.Properties.EmbedInteropTypes);
        json.String(
"kind",
            reference.Properties.Kind.ToString());
        json.Complete();
        return builder.ToString();
    }

    private static string GetPortableReferenceSha256(
        PortableExecutableReference reference,
        string path)
    {
        if (File.Exists(path))
        {
            return ProbeHash.File(path);
        }

        var metadata = reference.GetMetadata();
        var method = metadata.GetType().GetMethod(
            "GetEntireImage",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        var image = method?.Invoke(metadata, null);
        if (image is null)
        {
            image = FindRetainedPortableImage(
                metadata,
                depth: 4,
                []);
        }
        if (image is System.Collections.Immutable.ImmutableArray<byte> bytes &&
            !bytes.IsDefault)
        {
            return ProbeHash.Bytes(bytes.ToArray());
        }
        if (image is System.Reflection.PortableExecutable.PEMemoryBlock block)
        {
            return ProbeHash.Bytes(block.GetContent().ToArray());
        }

        return string.Empty;
    }

    private static System.Collections.Immutable.ImmutableArray<byte>
        FindRetainedPortableImage(
            object? value,
            int depth,
            List<object> visited)
    {
        if (value is null || depth < 0)
        {
            return default;
        }
        if (value is System.Reflection.PortableExecutable.PEReader reader)
        {
            return reader.GetEntireImage().GetContent();
        }
        if (value is System.Collections.Immutable.ImmutableArray<byte> bytes &&
            !bytes.IsDefault)
        {
            return bytes;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or Delegate ||
            visited.Any(item => ReferenceEquals(item, value)))
        {
            return default;
        }
        visited.Add(value);

        if (value is System.Collections.IEnumerable sequence)
        {
            var count = 0;
            foreach (var item in sequence)
            {
                var found = FindRetainedPortableImage(
                    item,
                    depth - 1,
                    visited);
                if (!found.IsDefault || ++count >= 32)
                {
                    return found;
                }
            }
        }

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;
        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags))
            {
                var found = FindRetainedPortableImage(
                    field.GetValue(value),
                    depth - 1,
                    visited);
                if (!found.IsDefault)
                {
                    return found;
                }
            }
        }
        return default;
    }

    private static CSharpCompilation GetReferencedCompilation(
        CompilationReference reference)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;
        var property = reference.GetType()
            .GetProperties(flags)
            .SingleOrDefault(static candidate =>
                candidate.Name == "Compilation" &&
                typeof(CSharpCompilation).IsAssignableFrom(
                    candidate.PropertyType));
        return property?.GetValue(reference) as CSharpCompilation ??
            throw new InvalidOperationException(
                "The C# compiler probe encountered a non-C# compilation reference.");
    }

    private static string CreateCompilationReferenceSha256(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var json = new ProbeJsonObject();
        var builder = json.Builder;
        json.PropertyName("assembly");
        AppendAssembly(builder, compilation);
        json.PropertyName("options");
        AppendOptions(builder, compilation);
        json.RawArray(
"syntaxTrees",
            CreateSyntaxTreeRows(compilation, cancellationToken));
        json.RawArray(
"references",
            CreateReferenceRows(compilation, cancellationToken));
        json.Complete();
        return ProbeHash.Text(builder.ToString());
    }

    private static string GetReferenceIdentity(
        CSharpCompilation compilation,
        MetadataReference reference)
    {
        return compilation.GetAssemblyOrModuleSymbol(reference) switch
        {
            IAssemblySymbol assembly => assembly.Identity.ToString(),
            IModuleSymbol module => module.Name,
            _ => string.Empty
        };
    }

    private static IEnumerable<string> CreateAdditionalFileRows(
        CompilationAnalysisContext context)
    {
        var provider = context.Options.AnalyzerConfigOptionsProvider;
        return context.Options.AdditionalFiles
            .Select(file =>
            {
                var text = GetStableAdditionalText(
                    file,
                    context.CancellationToken).ToString();
                var json = new ProbeJsonObject();
                var builder = json.Builder;
                json.String(
"metadataValue",
                    GetOption(
                        provider.GetOptions(file),
                        CompilerProbeContract
                            .AdditionalFileMetadataOptionKey));
                json.String(
"path",
                    NormalizePath(file.Path));
                json.String(
"textSha256",
                    ProbeHash.Text(text));
                json.Complete();
                return builder.ToString();
            })
            .OrderBy(static row => row, StringComparer.Ordinal);
    }

    private static SourceText GetStableAdditionalText(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        // The command-line compiler's AdditionalTextFile caches one
        // Lazy<SourceText> for both generators and analyzers. A custom provider
        // has no equivalent consistency guarantee, so it cannot back this
        // final-compilation authority.
        var providerType = file.GetType();
        if (providerType.Assembly != typeof(AdditionalText).Assembly ||
            !string.Equals(
                providerType.FullName,
                CommandLineAdditionalTextTypeName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An additional file does not expose a stable compiler input snapshot.");
        }

        return file.GetText(cancellationToken) ??
            throw new InvalidOperationException(
                "An additional file has no compiler text.");
    }

}
