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

    private static NullableContextOptions MapNullable(
        WorkerNullableContext value) =>
        value switch {
            WorkerNullableContext.Disabled =>
                NullableContextOptions.Disable,
            WorkerNullableContext.Warnings =>
                NullableContextOptions.Warnings,
            WorkerNullableContext.Annotations =>
                NullableContextOptions.Annotations,
            WorkerNullableContext.Enabled =>
                NullableContextOptions.Enable,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static OptimizationLevel MapOptimization(
        WorkerOptimizationLevel value) =>
        value switch {
            WorkerOptimizationLevel.Debug => OptimizationLevel.Debug,
            WorkerOptimizationLevel.Release => OptimizationLevel.Release,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static OutputKind MapOutputKind(WorkerOutputKind value) =>
        value switch {
            WorkerOutputKind.ConsoleApplication =>
                OutputKind.ConsoleApplication,
            WorkerOutputKind.WindowsApplication =>
                OutputKind.WindowsApplication,
            WorkerOutputKind.DynamicallyLinkedLibrary =>
                OutputKind.DynamicallyLinkedLibrary,
            WorkerOutputKind.NetModule => OutputKind.NetModule,
            WorkerOutputKind.WindowsRuntimeMetadata =>
                OutputKind.WindowsRuntimeMetadata,
            WorkerOutputKind.WindowsRuntimeApplication =>
                OutputKind.WindowsRuntimeApplication,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static Platform MapPlatform(WorkerPlatform value) =>
        value switch {
            WorkerPlatform.AnyCpu => Platform.AnyCpu,
            WorkerPlatform.AnyCpu32BitPreferred =>
                Platform.AnyCpu32BitPreferred,
            WorkerPlatform.X86 => Platform.X86,
            WorkerPlatform.X64 => Platform.X64,
            WorkerPlatform.Arm => Platform.Arm,
            WorkerPlatform.Arm64 => Platform.Arm64,
            WorkerPlatform.Itanium => Platform.Itanium,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
}
