namespace SharpProof.Worker;
internal static class WorkerCompilation {
    internal static CSharpCompilation Create(
        WorkerVerifyRequest request,
        WorkerInputSnapshot snapshot) {
        if (!LanguageVersionFacts.TryParse(
                request.Compilation.LanguageVersion,
                out var languageVersion))
            throw new ArgumentException(
                "The C# language version is invalid.",
                nameof(request));
        var parseOptions = new CSharpParseOptions(
            languageVersion,
            preprocessorSymbols: request.DefineConstants
                .OrderBy(static value => value, StringComparer.Ordinal));
        var trees = snapshot.Sources.Select(source =>
            CSharpSyntaxTree.ParseText(
                SourceText.From(
                    source.Text,
                    Encoding.UTF8,
                    SourceHashAlgorithm.Sha256),
                parseOptions,
                source.Path));
        var references = snapshot.References.Select(reference =>
            MetadataReference.CreateFromImage(
                ImmutableArray.Create(reference.Image),
                filePath: reference.Path));
        return CSharpCompilation.Create(
            request.AssemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                MapOutputKind(request.Compilation.OutputKind),
                optimizationLevel: MapOptimization(
                    request.Compilation.Optimization),
                checkOverflow: request.Compilation.CheckOverflow!.Value,
                allowUnsafe: request.Compilation.AllowUnsafe!.Value,
                platform: MapPlatform(request.Compilation.Platform),
                nullableContextOptions: MapNullable(
                    request.Compilation.NullableContext),
                deterministic: request.Compilation.Deterministic!.Value,
                concurrentBuild: false));
    }
    private static NullableContextOptions MapNullable(WorkerNullableContext value) =>
        value switch {
            WorkerNullableContext.Disabled => NullableContextOptions.Disable,
            WorkerNullableContext.Enabled => NullableContextOptions.Enable,
            _ => MapEnum<WorkerNullableContext, NullableContextOptions>(value)
        };
    private static OptimizationLevel MapOptimization(WorkerOptimizationLevel value) =>
        MapEnum<WorkerOptimizationLevel, OptimizationLevel>(value);
    private static OutputKind MapOutputKind(WorkerOutputKind value) =>
        MapEnum<WorkerOutputKind, OutputKind>(value);

    private static Platform MapPlatform(WorkerPlatform value) =>
        MapEnum<WorkerPlatform, Platform>(value);

    private static TTarget MapEnum<TSource, TTarget>(TSource value)
        where TSource : struct, Enum
        where TTarget : struct, Enum =>
        Enum.TryParse(value.ToString(), out TTarget mapped)
            ? mapped
            : throw new ArgumentOutOfRangeException(nameof(value));
}
