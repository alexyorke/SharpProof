using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace PurelySharp.Analyzer
{
    internal sealed class SummaryAssemblyIdentity
    {
        public SummaryAssemblyIdentity(string? assemblyName, string? assemblySha256, string? moduleVersionId)
        {
            AssemblyName = assemblyName;
            AssemblySha256 = assemblySha256;
            ModuleVersionId = moduleVersionId;
        }

        public string? AssemblyName { get; }

        public string? AssemblySha256 { get; }

        public string? ModuleVersionId { get; }

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(AssemblyName) &&
            !string.IsNullOrWhiteSpace(AssemblySha256) &&
            !string.IsNullOrWhiteSpace(ModuleVersionId);

        public bool Matches(ActualAssemblyIdentity actualAssemblyIdentity)
        {
            return string.Equals(AssemblyName, actualAssemblyIdentity.AssemblyName, StringComparison.Ordinal) &&
                string.Equals(AssemblySha256, actualAssemblyIdentity.AssemblySha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ModuleVersionId, actualAssemblyIdentity.ModuleVersionId, StringComparison.OrdinalIgnoreCase);
        }

        public static SummaryAssemblyIdentity? FromJson(JsonElement assemblyElement)
        {
            var assemblyName = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblyName");
            var assemblySha256 = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblySha256");
            var moduleVersionId = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "ModuleVersionId");
            if (string.IsNullOrWhiteSpace(assemblyName) &&
                string.IsNullOrWhiteSpace(assemblySha256) &&
                string.IsNullOrWhiteSpace(moduleVersionId))
            {
                return null;
            }

            return new SummaryAssemblyIdentity(
                assemblyName?.Trim(),
                assemblySha256?.Trim(),
                moduleVersionId?.Trim());
        }
    }

    internal sealed class SummaryMethodIdentity
    {
        public SummaryMethodIdentity(string? metadataToken, string? methodBodySha256)
        {
            MetadataToken = metadataToken;
            MethodBodySha256 = methodBodySha256;
        }

        public string? MetadataToken { get; }

        public string? MethodBodySha256 { get; }

        public bool IsCompleteEnoughFor(ActualMethodIdentity? actualMethodIdentity)
        {
            if (actualMethodIdentity == null || string.IsNullOrWhiteSpace(MetadataToken))
            {
                return false;
            }

            if (actualMethodIdentity.MethodBodySha256 == null)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(MethodBodySha256);
        }

        public bool Matches(ActualMethodIdentity actualMethodIdentity)
        {
            if (!string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (actualMethodIdentity.MethodBodySha256 == null)
            {
                return true;
            }

            return string.Equals(MethodBodySha256, actualMethodIdentity.MethodBodySha256, StringComparison.OrdinalIgnoreCase);
        }

        public static SummaryMethodIdentity? FromJson(JsonElement methodElement)
        {
            var metadataToken = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MetadataToken");
            var methodBodySha256 = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MethodBodySha256");
            if (string.IsNullOrWhiteSpace(metadataToken) && string.IsNullOrWhiteSpace(methodBodySha256))
            {
                return null;
            }

            return new SummaryMethodIdentity(metadataToken?.Trim(), methodBodySha256?.Trim());
        }
    }

    internal sealed class ActualMethodIdentity
    {
        public ActualMethodIdentity(string metadataToken, string? methodBodySha256, MethodAttributes attributes = 0)
        {
            MetadataToken = metadataToken;
            MethodBodySha256 = methodBodySha256;
            Attributes = attributes;
        }

        public string MetadataToken { get; }

        public string? MethodBodySha256 { get; }

        public MethodAttributes Attributes { get; }

        public bool CanBeOverridden =>
            Attributes.HasFlag(MethodAttributes.Virtual) &&
            !Attributes.HasFlag(MethodAttributes.Final) &&
            !Attributes.HasFlag(MethodAttributes.Static);
    }

    internal sealed class ActualAssemblyIdentity
    {
        public ActualAssemblyIdentity(string assemblyName, string assemblySha256, string moduleVersionId)
        {
            AssemblyName = assemblyName;
            AssemblySha256 = assemblySha256;
            ModuleVersionId = moduleVersionId;
        }

        public string AssemblyName { get; }

        public string AssemblySha256 { get; }

        public string ModuleVersionId { get; }

        public static ActualAssemblyIdentity? FromFile(string path)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return null;
            }

            var metadataReader = peReader.GetMetadataReader();
            var assemblyName = metadataReader.IsAssembly
                ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name)
                : Path.GetFileNameWithoutExtension(path);
            var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid).ToString("D");
            var assemblySha256 = ComputeSha256(path);

            return new ActualAssemblyIdentity(assemblyName, assemblySha256, moduleVersionId);
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            return CompatibilityHelpers.ToLowerHex(sha256.ComputeHash(stream));
        }
    }

    internal static class RuntimeMetadataAssemblyLocator
    {
        public static IEnumerable<string> GetTrustedPlatformAssemblyPaths()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                var trustedPlatformAssembliesValue = trustedPlatformAssemblies!;
                return trustedPlatformAssembliesValue
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }

            var coreAssemblyLocation = typeof(object).Assembly.Location;
            if (string.IsNullOrWhiteSpace(coreAssemblyLocation))
            {
                return Array.Empty<string>();
            }

            var runtimeDirectory = Path.GetDirectoryName(coreAssemblyLocation);
            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        }
    }
}
