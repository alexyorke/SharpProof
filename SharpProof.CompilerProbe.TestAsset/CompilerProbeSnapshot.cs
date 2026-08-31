using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSnapshot
{
    private const string CommandLineAdditionalTextTypeName =
        "Microsoft.CodeAnalysis.AdditionalTextFile";

    internal static string Create(CompilationAnalysisContext context)
    {
        var compilation = (CSharpCompilation)context.Compilation;
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringProperty(
            builder,
            ref first,
            "schema",
            CompilerProbeContract.SchemaName);
        ProbeJson.IntegerProperty(
            builder,
            ref first,
            "schemaVersion",
            CompilerProbeContract.SchemaVersion);
        ProbeJson.PropertyName(builder, ref first, "assembly");
        AppendAssembly(builder, compilation);
        ProbeJson.PropertyName(builder, ref first, "options");
        AppendOptions(builder, compilation);
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "consumedOptions",
            CreateConsumedOptionRows(context));
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "syntaxTrees",
            CreateSyntaxTreeRows(compilation, context.CancellationToken));
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "portableReferences",
            CreateReferenceRows(compilation, context.CancellationToken));
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "additionalFiles",
            CreateAdditionalFileRows(context));
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendAssembly(
        StringBuilder builder,
        CSharpCompilation compilation)
    {
        var first = true;
        builder.Append('{');
        ProbeJson.StringProperty(
            builder,
            ref first,
            "identity",
            compilation.Assembly.Identity.ToString());
        ProbeJson.StringProperty(
            builder,
            ref first,
            "name",
            compilation.AssemblyName ?? string.Empty);
        builder.Append('}');
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
        var first = true;
        builder.Append('{');
        ProbeJson.BooleanProperty(
            builder,
            ref first,
            "allowUnsafe",
            options.AllowUnsafe);
        ProbeJson.BooleanProperty(
            builder,
            ref first,
            "checkOverflow",
            options.CheckOverflow);
        ProbeJson.BooleanProperty(
            builder,
            ref first,
            "deterministic",
            options.Deterministic);
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "languageVersions",
            parseOptions
                .Select(static option => option.LanguageVersion.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "mainTypeName",
            options.MainTypeName ?? string.Empty);
        ProbeJson.StringProperty(
            builder,
            ref first,
            "nullableContextOptions",
            options.NullableContextOptions.ToString());
        ProbeJson.StringProperty(
            builder,
            ref first,
            "optimizationLevel",
            options.OptimizationLevel.ToString());
        ProbeJson.StringProperty(
            builder,
            ref first,
            "outputKind",
            options.OutputKind.ToString());
        ProbeJson.StringProperty(
            builder,
            ref first,
            "platform",
            options.Platform.ToString());
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "preprocessorSymbols",
            parseOptions
                .SelectMany(static option => option.PreprocessorSymbolNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "specifiedLanguageVersions",
            parseOptions
                .Select(static option =>
                    option.SpecifiedLanguageVersion.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "usings",
            options.Usings.OrderBy(
                static value => value,
                StringComparer.Ordinal));
        builder.Append('}');
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
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringProperty(builder, ref first, "key", key);
        ProbeJson.StringProperty(builder, ref first, "path", path);
        ProbeJson.StringProperty(builder, ref first, "value", value);
        builder.Append('}');
        return builder.ToString();
    }

    private static IEnumerable<string> CreateSyntaxTreeRows(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var indexedTrees = compilation.SyntaxTrees
            .Select(static (tree, ordinal) => (Tree: tree, Ordinal: ordinal))
            .OrderBy(
                static item => NormalizePath(item.Tree.FilePath),
                StringComparer.Ordinal)
            .ThenBy(static item => item.Ordinal);
        return indexedTrees.Select(item =>
            CreateSyntaxTreeRow(
                compilation,
                item.Tree,
                item.Ordinal,
                cancellationToken));
    }

    private static string CreateSyntaxTreeRow(
        CSharpCompilation compilation,
        SyntaxTree tree,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var text = tree.GetText(cancellationToken).ToString();
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "declaredSymbols",
            GetDeclaredSymbols(compilation, tree, cancellationToken));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "generatedKind",
            // A Compilation exposes no trustworthy pipeline-origin metadata for
            // a SyntaxTree. File names and comments are conventions that
            // handwritten source can freely imitate, so do not assert source
            // provenance from those heuristics.
            "Unknown");
        ProbeJson.IntegerProperty(
            builder,
            ref first,
            "ordinal",
            ordinal);
        ProbeJson.PropertyName(builder, ref first, "parseOptions");
        AppendParseOptions(builder, (CSharpParseOptions)tree.Options);
        ProbeJson.StringProperty(
            builder,
            ref first,
            "path",
            NormalizePath(tree.FilePath));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "textSha256",
            ProbeHash.Text(text));
        builder.Append('}');
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
        var first = true;
        builder.Append('{');
        ProbeJson.StringProperty(
            builder,
            ref first,
            "documentationMode",
            options.DocumentationMode.ToString());
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "features",
            options.Features
                .OrderBy(static feature => feature.Key, StringComparer.Ordinal)
                .ThenBy(static feature => feature.Value, StringComparer.Ordinal)
                .Select(static feature =>
                    CreateFeatureRow(feature.Key, feature.Value)));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "kind",
            options.Kind.ToString());
        ProbeJson.StringProperty(
            builder,
            ref first,
            "languageVersion",
            options.LanguageVersion.ToString());
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "preprocessorSymbols",
            options.PreprocessorSymbolNames.OrderBy(
                static value => value,
                StringComparer.Ordinal));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "specifiedLanguageVersion",
            options.SpecifiedLanguageVersion.ToString());
        builder.Append('}');
    }

    private static string CreateFeatureRow(string key, string value)
    {
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringProperty(builder, ref first, "key", key);
        ProbeJson.StringProperty(builder, ref first, "value", value);
        builder.Append('}');
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
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias,
                StringComparer.Ordinal));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "assemblyOrModuleIdentity",
            GetReferenceIdentity(compilation, reference));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "display",
            NormalizePath(reference.Display ?? string.Empty));
        ProbeJson.BooleanProperty(
            builder,
            ref first,
            "embedInteropTypes",
            reference.Properties.EmbedInteropTypes);
        ProbeJson.StringProperty(
            builder,
            ref first,
            "filePath",
            NormalizePath(path));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "fileSha256",
            GetPortableReferenceSha256(reference, path));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "kind",
            reference.Properties.Kind.ToString());
        builder.Append('}');
        return builder.ToString();
    }

    private static string CreateCompilationReferenceRow(
        CSharpCompilation compilation,
        CompilationReference reference,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.StringArrayProperty(
            builder,
            ref first,
            "aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias,
                StringComparer.Ordinal));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "assemblyOrModuleIdentity",
            GetReferenceIdentity(compilation, reference));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "compilationSha256",
            CreateCompilationReferenceSha256(
                GetReferencedCompilation(reference),
                cancellationToken));
        ProbeJson.StringProperty(
            builder,
            ref first,
            "display",
            NormalizePath(reference.Display ?? string.Empty));
        ProbeJson.BooleanProperty(
            builder,
            ref first,
            "embedInteropTypes",
            reference.Properties.EmbedInteropTypes);
        ProbeJson.StringProperty(
            builder,
            ref first,
            "kind",
            reference.Properties.Kind.ToString());
        builder.Append('}');
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
            var reader = metadata.GetType().GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.GetValue(metadata))
                .OfType<System.Reflection.PortableExecutable.PEReader>()
                .SingleOrDefault();
            image = reader?.GetEntireImage();
        }
        if (image is System.Collections.Immutable.ImmutableArray<byte> bytes &&
            !bytes.IsDefault)
        {
            return ProbeHash.Bytes(bytes.ToArray());
        }

        return string.Empty;
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
        var builder = new StringBuilder();
        var first = true;
        builder.Append('{');
        ProbeJson.PropertyName(builder, ref first, "assembly");
        AppendAssembly(builder, compilation);
        ProbeJson.PropertyName(builder, ref first, "options");
        AppendOptions(builder, compilation);
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "syntaxTrees",
            CreateSyntaxTreeRows(compilation, cancellationToken));
        ProbeJson.RawArrayProperty(
            builder,
            ref first,
            "references",
            CreateReferenceRows(compilation, cancellationToken));
        builder.Append('}');
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
                var builder = new StringBuilder();
                var first = true;
                builder.Append('{');
                ProbeJson.StringProperty(
                    builder,
                    ref first,
                    "metadataValue",
                    GetOption(
                        provider.GetOptions(file),
                        CompilerProbeContract
                            .AdditionalFileMetadataOptionKey));
                ProbeJson.StringProperty(
                    builder,
                    ref first,
                    "path",
                    NormalizePath(file.Path));
                ProbeJson.StringProperty(
                    builder,
                    ref first,
                    "textSha256",
                    ProbeHash.Text(text));
                builder.Append('}');
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

    private static string GetOption(
        AnalyzerConfigOptions options,
        string key)
    {
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
