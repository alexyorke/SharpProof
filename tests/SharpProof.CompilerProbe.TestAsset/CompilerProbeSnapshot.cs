using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.CompilerSupport;
using static SharpProof.CompilerProbe.TestAsset.CompilerProbeSourceHelpers;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSnapshot
{
    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;
    private static readonly ConditionalWeakTable<Type, PortableImageMethod>
        PortableImageMethods = new();
    private static readonly ConditionalWeakTable<Type, CompilationProperty>
        CompilationProperties = new();

    private sealed class PortableImageMethod(Type metadataType)
    {
        internal MethodInfo? Value { get; } = (metadataType ?? throw new System.ArgumentNullException(nameof(metadataType))).GetMethod(
            "GetEntireImage",
            InstanceMemberFlags,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
    }

    private sealed class CompilationProperty(Type referenceType)
    {
        internal PropertyInfo? Value { get; } = (referenceType is null ? throw new System.ArgumentNullException(nameof(referenceType)) : referenceType
            .GetProperties(InstanceMemberFlags)
            .SingleOrDefault(static candidate =>
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Name == "Compilation" &&
                typeof(CSharpCompilation).IsAssignableFrom(
                    candidate.PropertyType))));
    }

    private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceIdentityComparer Instance = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        int IEqualityComparer<object>.GetHashCode(object obj)
        {
            obj = RepositoryParameterGuard.NotNull(obj, nameof(obj));
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    internal static string Create(CompilationAnalysisContext context)
    {
        context = RepositoryParameterGuard.ValidateInitialized(context, context.Compilation is null, nameof(context), "Value must be initialized.");

        var compilation = (CSharpCompilation)context.Compilation;
        var json = new ProbeJsonObject();
        json.String(
"schema",
            CompilerProbeContract.SchemaName);
        json.Integer(
"schemaVersion",
            CompilerProbeContract.SchemaVersion);
        json.Object("assembly", child => AppendAssembly(RepositoryParameterGuard.NotNull(child, nameof(child)), compilation));
        json.Object("options", child => AppendOptions(RepositoryParameterGuard.NotNull(child, nameof(child)), compilation));
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
        return json.Complete();
    }

    private static void AppendAssembly(
        ProbeJsonObject json,
        CSharpCompilation compilation)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        json.String(
"identity",
            compilation.Assembly.Identity.ToString());
        json.String(
"name",
            compilation.AssemblyName ?? string.Empty);
    }

    private static void AppendOptions(
        ProbeJsonObject json,
        CSharpCompilation compilation)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var options = compilation.Options;
        var parseOptions = compilation.SyntaxTrees
            .Select(static tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (tree.Options))
            .OfType<CSharpParseOptions>()
            .ToImmutableArray();
        var languageVersions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var preprocessorSymbols = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var specifiedLanguageVersions = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var option in parseOptions)
        {
            languageVersions.Add(option.LanguageVersion.ToString());
            specifiedLanguageVersions.Add(option.SpecifiedLanguageVersion.ToString());
            foreach (var symbol in option.PreprocessorSymbolNames)
            {
                preprocessorSymbols.Add(symbol);
            }
        }
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
            languageVersions.ToImmutable().OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal));
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
            preprocessorSymbols.ToImmutable().OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal));
        json.StringArray(
"specifiedLanguageVersions",
            specifiedLanguageVersions.ToImmutable().OrderBy(
                static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value),
                StringComparer.Ordinal));
        json.StringArray(
"usings",
            options.Usings.OrderBy(
                static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value),
                StringComparer.Ordinal));
    }

    private static IEnumerable<string> CreateConsumedOptionRows(
        CompilationAnalysisContext context)
    {
        context = RepositoryParameterGuard.ValidateInitialized(context, context.Compilation is null, nameof(context), "Value must be initialized.");

        var provider = context.Options.AnalyzerConfigOptionsProvider;
        var rows = ImmutableArray.CreateBuilder<string>();
        rows.AddRange((ImmutableArray<string>)[
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
        ]);
        rows.AddRange(context.Options.AdditionalFiles.Select(file =>
            file is null ? throw new System.ArgumentNullException(nameof(file)) : (CreateOptionRow(
                CompilerProbeContract.AdditionalFileMetadataOptionKey,
                NormalizePath(file.Path),
                GetOption(
                    provider.GetOptions(file),
                    CompilerProbeContract.AdditionalFileMetadataOptionKey)))));
        return rows.ToImmutable().OrderBy(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row), StringComparer.Ordinal);
    }

    private static string CreateOptionRow(
        string key,
        string path,
        string value)
    {
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var json = new ProbeJsonObject();
        json.String("key", key);
        json.String("path", path);
        json.String("value", value);
        return json.Complete();
    }

    private static IEnumerable<string> CreateSyntaxTreeRows(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var indexedTrees = compilation.SyntaxTrees
            .Select(static (tree, ordinal) =>
                tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Ordinal: ordinal, Path: NormalizePath(tree.FilePath))))
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
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        var text = tree.GetText(cancellationToken).ToString();
        var json = new ProbeJsonObject();
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
        json.Object("parseOptions", child => AppendParseOptions(RepositoryParameterGuard.NotNull(child, nameof(child)), (CSharpParseOptions)tree.Options));
        json.String(
"path",
            path);
        json.String(
"textSha256",
            ProbeHash.Text(text));
        return json.Complete();
    }

    private static IEnumerable<string> GetDeclaredSymbols(
        CSharpCompilation compilation,
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot(cancellationToken)
            .DescendantNodesAndSelf()
            .Where(static node =>
                node is null ? throw new System.ArgumentNullException(nameof(node)) : (node is BaseTypeDeclarationSyntax or
                    DelegateDeclarationSyntax or
                    BaseMethodDeclarationSyntax or
                    PropertyDeclarationSyntax or
                    EventDeclarationSyntax))
            .Select(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (model.GetDeclaredSymbol(node, cancellationToken)))
            .Where(static symbol => symbol != null)
            .Select(static symbol => symbol is null ? throw new System.ArgumentNullException(nameof(symbol)) : (symbol!.ToDisplayString(
                SymbolDisplayFormat.CSharpErrorMessageFormat)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal);
    }

    private static void AppendParseOptions(
        ProbeJsonObject json,
        CSharpParseOptions options)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
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
                static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value),
                StringComparer.Ordinal));
        json.String(
"specifiedLanguageVersion",
            options.SpecifiedLanguageVersion.ToString());
    }

    private static string CreateFeatureRow(string key, string value)
    {
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var json = new ProbeJsonObject();
        json.String("key", key);
        json.String("value", value);
        return json.Complete();
    }

    private static IEnumerable<string> CreateReferenceRows(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        return compilation.References
            .Select(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (CreateReferenceRow(
                    compilation,
                    reference,
                    cancellationToken)))
            .OrderBy(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row), StringComparer.Ordinal);
    }

    private static string CreateReferenceRow(
        CSharpCompilation compilation,
        MetadataReference reference,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
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
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        var path = reference.FilePath ?? string.Empty;
        var json = new ProbeJsonObject();
        json.StringArray(
"aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias is null ? throw new System.ArgumentNullException(nameof(alias)) : (alias),
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
        return json.Complete();
    }

    private static string CreateCompilationReferenceRow(
        CSharpCompilation compilation,
        CompilationReference reference,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        var json = new ProbeJsonObject();
        json.StringArray(
"aliases",
            reference.Properties.Aliases.OrderBy(
                static alias => alias is null ? throw new System.ArgumentNullException(nameof(alias)) : (alias),
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
        return json.Complete();
    }

    private static string GetPortableReferenceSha256(
        PortableExecutableReference reference,
        string path)
    {
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
        if (File.Exists(path))
        {
            return ProbeHash.File(path);
        }

        var metadata = reference.GetMetadata();
        var image = PortableImageMethods.GetValue(
            metadata.GetType(),
            static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (new PortableImageMethod(type)))
            .Value?.Invoke(metadata, null);
        if (image is null)
        {
            image = FindRetainedPortableImage(
                metadata,
                depth: 4,
                ImmutableHashSet.CreateBuilder<object>(ReferenceIdentityComparer.Instance));
        }
        if (image is System.Collections.Immutable.ImmutableArray<byte> bytes &&
            !bytes.IsDefault)
        {
            return ProbeHash.Bytes(bytes);
        }
        if (image is System.Reflection.PortableExecutable.PEMemoryBlock block)
        {
            return ProbeHash.Bytes(block.GetContent());
        }

        return string.Empty;
    }

    private static System.Collections.Immutable.ImmutableArray<byte>
        FindRetainedPortableImage(
            object? value,
            int depth,
            ImmutableHashSet<object>.Builder visited)
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
            !visited.Add(value))
        {
            return default;
        }

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
        var property = CompilationProperties.GetValue(
            reference.GetType(),
            static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (new CompilationProperty(type)))
            .Value;
        return property?.GetValue(reference) as CSharpCompilation ??
            throw new InvalidOperationException(
                "The C# compiler probe encountered a non-C# compilation reference.");
    }

    private static string CreateCompilationReferenceSha256(
        CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var json = new ProbeJsonObject();
        json.Object("assembly", child => AppendAssembly(RepositoryParameterGuard.NotNull(child, nameof(child)), compilation));
        json.Object("options", child => AppendOptions(RepositoryParameterGuard.NotNull(child, nameof(child)), compilation));
        json.RawArray(
"syntaxTrees",
            CreateSyntaxTreeRows(compilation, cancellationToken));
        json.RawArray(
"references",
            CreateReferenceRows(compilation, cancellationToken));
        return ProbeHash.Text(json.Complete());
    }

    private static string GetReferenceIdentity(
        CSharpCompilation compilation,
        MetadataReference reference)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        reference = RepositoryParameterGuard.NotNull(reference, nameof(reference));
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
        context = RepositoryParameterGuard.ValidateInitialized(context, context.Compilation is null, nameof(context), "Value must be initialized.");

        var provider = context.Options.AnalyzerConfigOptionsProvider;
        return context.Options.AdditionalFiles
            .Select(file =>
            {
                file = RepositoryParameterGuard.NotNull(file, nameof(file));
                var text = GetStableAdditionalText(
                    file,
                    context.CancellationToken).ToString();
                var json = new ProbeJsonObject();
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
                return json.Complete();
            })
            .OrderBy(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row), StringComparer.Ordinal);
    }

    private static SourceText GetStableAdditionalText(
        AdditionalText file,
        CancellationToken cancellationToken)
    {
        // The command-line compiler's AdditionalTextFile caches one
        // Lazy<SourceText> for both generators and analyzers. A custom provider
        // has no equivalent consistency guarantee, so it cannot back this
        // final-compilation authority.
        file = RepositoryParameterGuard.NotNull(file, nameof(file));
        return CompilerAdditionalTextStability.GetStableAdditionalText(
            file,
            cancellationToken);
    }

}
