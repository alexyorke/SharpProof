namespace SharpProof.Effects.Test;

internal static class EffectTestHost
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
        CreatePlatformReferences();
    private static readonly ImmutableArray<MetadataReference> DefaultReferences =
        PlatformReferences.Add(MetadataReference.CreateFromFile(
            typeof(EffectContractAttribute).Assembly.Location));

    internal static CSharpCompilation CreateCompilation(
        string source,
        params MetadataReference[] additionalReferences)
    {
        return CreateCompilationCore(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                path: "EffectsTest.cs")],
            "EffectsTest",
            DefaultReferences.AddRange(additionalReferences));
    }

    internal static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        string assemblyName = "EffectsTest",
        params MetadataReference[] additionalReferences)
    {
        return CreateCompilationCore(
            syntaxTrees,
            assemblyName,
            DefaultReferences.AddRange(additionalReferences));
    }

    internal static PortableExecutableReference EmitReference(
        string source,
        string assemblyName)
    {
        return EmitImage(source, assemblyName).Reference;
    }

    internal static CSharpCompilation CreateCompilationWithoutContractPackage(
        string source,
        params MetadataReference[] additionalReferences)
    {
        return CreateCompilationCore(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12),
                path: "EffectsTest.cs")],
            "EffectsTest",
            PlatformReferences.AddRange(additionalReferences));
    }

    internal static PortableExecutableReference EmitReferenceWithoutContractPackage(
        string source,
        string assemblyName)
    {
        var compilation = CreateCompilationCore(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp12),
                path: assemblyName + ".cs")],
            assemblyName,
            PlatformReferences);
        return EmitImage(compilation).Reference;
    }

    internal static PortableExecutableReference
        EmitUnapprovedContractApiReference(
            bool validContractShape)
    {
        var version =
            typeof(EffectContractAttribute).Assembly
                .GetName().Version ??
            throw new InvalidOperationException(
                "SharpProof.Attributes has no assembly version.");
        var conditional = validContractShape
            ? """
              [System.Diagnostics.Conditional(
                  ConditionalSymbol)]
              """
            : string.Empty;
        return EmitReferenceWithoutContractPackage(
            $$"""
            using System.Reflection;

            [assembly: AssemblyVersion("{{version}}")]

            namespace SharpProof.Attributes {
                public static class Contract {
                    public const string ConditionalSymbol =
                        "SHARPPROOF_CONTRACTS";

                    {{conditional}}
                    public static void Requires(bool condition) {
                    }

                    {{conditional}}
                    public static void Ensures(bool condition) {
                    }

                    {{conditional}}
                    public static void Assume(bool condition) {
                    }

                    public static T Result<T>() => default!;
                    public static T Old<T>(T value) => value;
                }

                public enum SharpProofEffect {
                    None = 0
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute : System.Attribute {
                }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class EffectContractAttribute :
                    System.Attribute {
                    public EffectContractAttribute(
                        SharpProofEffect effects) {
                    }

                    public bool Complete {
                        get;
                        set;
                    }
                }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class SharpProofTrustedAttribute :
                    System.Attribute {
                    public SharpProofTrustedAttribute(string reason) {
                    }
                }
            }
            """,
            "SharpProof.Attributes");
    }

    internal static EmittedAssemblyImage EmitImage(
        string source,
        string assemblyName)
    {
        var compilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                path: assemblyName + ".cs")],
            assemblyName);
        return EmitImage(compilation);
    }

    internal static EmittedAssemblyImage EmitImage(
        CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(FormatErrors(result.Diagnostics));
        }

        var image = stream.ToArray();
        return new EmittedAssemblyImage(
            MetadataReference.CreateFromImage(image),
            image);
    }

    internal static IMethodSymbol RequireMethod(
        Compilation compilation,
        string typeMetadataName,
        string methodName)
    {
        var type = compilation.GetTypeByMetadataName(typeMetadataName) ??
                   throw new InvalidOperationException(
                       $"Type '{typeMetadataName}' was not found.");
        return type.GetMembers(methodName)
                   .OfType<IMethodSymbol>()
                   .Single(static method => method.MethodKind == MethodKind.Ordinary);
    }

    internal static IMethodSymbol SampleMethod(
        Compilation compilation,
        string methodName)
    {
        return RequireMethod(compilation, "Sample", methodName);
    }

    internal static EffectMethodResult AnalyzeSample(
        Compilation compilation,
        string methodName)
    {
        return new EffectAnalysisSession(compilation)
            .Analyze(SampleMethod(compilation, methodName));
    }

    internal static IOperation RootOperation(
        Compilation compilation,
        IMethodSymbol method)
    {
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        return compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax) ??
            throw new InvalidOperationException(
                $"Operation for '{method.Name}' was not found.");
    }

    internal static OperationCompletionEvaluator CreateCompletionEvaluator(
        Compilation compilation,
        IMethodSymbol caller)
    {
        return new(
            new EffectAnalysisSession(compilation),
            caller,
            static (_, _) => false,
            static (_, _) => false,
            static _ => false);
    }

    internal static bool HasStaticWrite(
        Compilation compilation,
        IMethodSymbol method)
    {
        return new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary.Writes.Contains(EffectRegionId.Static());
    }

    internal static CatchClauseSyntax CatchClauseIn(IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Single().GetSyntax()
            .DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Single();
    }

    internal static ExceptionHandlerReachability CreateHandlerReachability(
        Compilation compilation,
        IMethodSymbol caller,
        EffectAnalysisSession session,
        bool isKnownNonThrowing = false)
    {
        return new(
            compilation: compilation,
            caller: caller,
            abstractFlow: null,
            canCompleteNormally: static _ => true,
            canMethodCompleteNormally: static _ => true,
            canCompoundValueComplete: static _ => true,
            canIncrementValueComplete: static _ => true,
            canWithCloneComplete: static _ => true,
            conversionEffects: new ConversionEffectClassifier(
                session,
                abstractFlow: null),
            getReachableListPatternMembers: static _ => [],
            apiSpecs: session.ApiSpecs,
            knownSymbols: session.KnownSymbols,
            isKnownNonThrowing: isKnownNonThrowing
                ? static _ => true
                : static _ => false);
    }

    internal static INamedTypeSymbol RequireType(
        Compilation compilation,
        string metadataName)
    {
        return compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException(
            $"Type '{metadataName}' was not found.");
    }

    private static CSharpCompilation CreateCompilationCore(
        IEnumerable<SyntaxTree> syntaxTrees,
        string assemblyName,
        IEnumerable<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
        RequireNoErrors(compilation);
        return compilation;
    }

    private static ImmutableArray<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");
        return [.. trustedAssemblies
            .Split(Path.PathSeparator)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static void RequireNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(FormatErrors(errors));
        }
    }

    private static string FormatErrors(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic => diagnostic.ToString()));
    }
}

internal sealed class EmittedAssemblyImage(
    PortableExecutableReference reference,
    byte[] image)
{
    internal PortableExecutableReference Reference { get; } = reference;
    internal byte[] Image { get; } = image;
}
