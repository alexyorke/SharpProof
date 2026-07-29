namespace SharpProof.CompilerArtifact;

internal static class CompilerOptionWireMappings
{
    internal static CompilerOutputKind Map(OutputKind value)
    {
        return value switch
        {
            OutputKind.ConsoleApplication => CompilerOutputKind.ConsoleApplication,
            OutputKind.WindowsApplication => CompilerOutputKind.WindowsApplication,
            OutputKind.DynamicallyLinkedLibrary => CompilerOutputKind.DynamicallyLinkedLibrary,
            OutputKind.NetModule => CompilerOutputKind.NetModule,
            OutputKind.WindowsRuntimeMetadata => CompilerOutputKind.WindowsRuntimeMetadata,
            OutputKind.WindowsRuntimeApplication => CompilerOutputKind.WindowsRuntimeApplication,
            _ => throw Unsupported(nameof(OutputKind), value)
        };
    }

    internal static CompilerOptimizationLevel Map(OptimizationLevel value)
    {
        return value switch
        {
            OptimizationLevel.Debug => CompilerOptimizationLevel.Debug,
            OptimizationLevel.Release => CompilerOptimizationLevel.Release,
            _ => throw Unsupported(nameof(OptimizationLevel), value)
        };
    }

    internal static CompilerPlatform Map(Platform value)
    {
        return value switch
        {
            Platform.AnyCpu => CompilerPlatform.AnyCpu,
            Platform.AnyCpu32BitPreferred => CompilerPlatform.AnyCpu32BitPreferred,
            Platform.Arm => CompilerPlatform.Arm,
            Platform.Arm64 => CompilerPlatform.Arm64,
            Platform.Itanium => CompilerPlatform.Itanium,
            Platform.X64 => CompilerPlatform.X64,
            Platform.X86 => CompilerPlatform.X86,
            _ => throw Unsupported(nameof(Platform), value)
        };
    }

    internal static CompilerNullableContext Map(NullableContextOptions value)
    {
        return value switch
        {
            NullableContextOptions.Disable => CompilerNullableContext.Disable,
            NullableContextOptions.Warnings => CompilerNullableContext.Warnings,
            NullableContextOptions.Annotations => CompilerNullableContext.Annotations,
            NullableContextOptions.Enable => CompilerNullableContext.Enable,
            _ => throw Unsupported(nameof(NullableContextOptions), value)
        };
    }

    internal static CompilerMetadataImportOptions Map(MetadataImportOptions value)
    {
        return value switch
        {
            MetadataImportOptions.Public => CompilerMetadataImportOptions.Public,
            MetadataImportOptions.Internal => CompilerMetadataImportOptions.Internal,
            MetadataImportOptions.All => CompilerMetadataImportOptions.All,
            _ => throw Unsupported(nameof(MetadataImportOptions), value)
        };
    }

    internal static CompilerAssemblyIdentityComparer Map(AssemblyIdentityComparer value)
    {
        return ReferenceEquals(value, AssemblyIdentityComparer.Default)
            ? CompilerAssemblyIdentityComparer.Default
            : ReferenceEquals(value, DesktopAssemblyIdentityComparer.Default)
                ? CompilerAssemblyIdentityComparer.Desktop
                : throw new InvalidOperationException(
                    "A custom assembly identity comparer is unsupported.");
    }

    internal static bool ReadInternalBoolean(
        CSharpCompilationOptions options,
        string name)
    {
        var property = typeof(CompilationOptions).GetProperty(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (property?.PropertyType != typeof(bool) ||
            property.GetIndexParameters().Length != 0)
        {
            throw new InvalidOperationException(
                $"The compiler option '{name}' is unavailable or has an unexpected shape.");
        }

        return (bool)(property.GetValue(options) ??
            throw new InvalidOperationException(
                $"The compiler option '{name}' returned no value."));
    }

    private static InvalidOperationException Unsupported<T>(
        string name,
        T value)
        where T : struct
    {
        return new($"The compiler option '{name}' has unsupported value '{value}'.");
    }
}
