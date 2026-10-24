using System.Xml.Linq;

namespace SharpProof.Tool;

internal static class SetupInitializerContainer
{
    private static readonly string[] ComposeNames = ["compose.yaml", "compose.yml", "docker-compose.yaml", "docker-compose.yml"];
    private const string Guard = "'$(SHARPPROOF_CONTAINER)' == '1'";

    internal static void Plan(SetupInitializerFiles files, string directory, string? consumerImage)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (consumerImage != null && !SetupInitializer.IsPinnedConsumerImage(consumerImage))
        { throw new ArgumentException("--consumer-image must be an immutable SHA-256 image in the SharpProof canonical or private-preview GHCR repository."); }
        var candidates = ComposeNames.Select(name =>
        {
            ArgumentNullException.ThrowIfNull(name);
            return Path.Combine(directory, name);
        }).Where(static path =>
        {
            ArgumentNullException.ThrowIfNull(path);
            return File.Exists(path) || Directory.Exists(path);
        }).ToArray();
        if (candidates.Length > 1)
        { throw new InvalidOperationException("Multiple compose files make container initialization ambiguous."); }
        var composePath = candidates.Length == 0 ? Path.Combine(directory, "compose.yaml") : candidates[0];
        var existing = files.Read(composePath);
        if (existing != null)
        {
            var lines = Lines(existing);
            var block = Service(lines);
            var imageLines = block.Where(static line =>
            {
                ArgumentNullException.ThrowIfNull(line);
                return line.StartsWith("    image: ", StringComparison.Ordinal);
            }).ToArray();
            var image = imageLines.Length == 1 ? imageLines[0][11..] : string.Empty;
            if (!SetupInitializer.IsPinnedConsumerImage(image) || consumerImage != null && consumerImage != image ||
                !block.SequenceEqual(Service(Lines(Compose(image))), StringComparer.Ordinal) || !HasVolumes(lines))
            { throw new InvalidOperationException("Existing compose verification settings conflict with the qualified starter layout."); }
            files.Add(composePath, existing);
        }
        else
        {
            if (consumerImage == null)
            { throw new ArgumentException("Container initialization requires --consumer-image unless an existing compatible immutable compose file supplies it."); }
            files.Add(composePath, Compose(consumerImage));
        }
        var propsPath = Path.Combine(directory, "Directory.Build.props");
        var propsText = files.Read(propsPath);
        if (propsText == null && SetupInitializerFiles.AncestorFile(Path.GetDirectoryName(directory) ?? directory, "Directory.Build.props") != null)
        { throw new InvalidOperationException("Container output isolation would shadow inherited Directory.Build.props; configure that inheritance manually."); }
        var props = SetupInitializerFiles.Xml(propsText, "Project");
        SetupInitializerProject.RequireNoImports(props.Root!);
        Isolation(props.Root!, "SharpProofProjectOutputKey", "$([MSBuild]::MakeRelative('$(MSBuildThisFileDirectory)', '$(MSBuildProjectDirectory)'))/$(MSBuildProjectName)");
        Isolation(props.Root!, "BaseIntermediateOutputPath", "$(MSBuildThisFileDirectory).sharpproof-obj/$(SharpProofProjectOutputKey)/");
        Isolation(props.Root!, "BaseOutputPath", "$(MSBuildThisFileDirectory).sharpproof-bin/$(SharpProofProjectOutputKey)/");
        files.Add(propsPath, SetupInitializerFiles.Serialize(props));
    }

    internal static string Compose(string image)
    {
        ArgumentException.ThrowIfNullOrEmpty(image);
        return """
services:
  verify:
    image: __IMAGE__
    platform: linux/amd64
    mem_limit: ${SHARPPROOF_CONTAINER_MEMORY_LIMIT:-40g}
    cpus: ${SHARPPROOF_CONTAINER_CPU_LIMIT:-0}
    working_dir: /workspace/consumer
    environment:
      - NuGetPackageSourceCredentials_SharpProofPreview
    volumes:
      - .:/workspace/consumer
      - sharpproof-obj:/workspace/consumer/.sharpproof-obj
      - sharpproof-bin:/workspace/consumer/.sharpproof-bin
      - sharpproof-packages:/home/sharpproof/.nuget/packages
      - sharpproof-dotnet:/home/sharpproof/.dotnet
volumes:
  sharpproof-obj:
  sharpproof-bin:
  sharpproof-packages:
  sharpproof-dotnet:
""".Replace("__IMAGE__", image, StringComparison.Ordinal) + "\n";
    }

    private static string[] Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('\t', StringComparison.Ordinal))
        { throw new InvalidOperationException("Compose indentation is ambiguous; use the qualified starter layout."); }
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(static line =>
        {
            ArgumentNullException.ThrowIfNull(line);
            return line.TrimEnd();
        }).Where(static line =>
        {
            ArgumentNullException.ThrowIfNull(line);
            return line.Length > 0 && !line.TrimStart().StartsWith('#');
        }).ToArray();
    }

    private static string[] Service(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var services = Array.IndexOf(lines, "services:");
        var start = Array.IndexOf(lines, "  verify:");
        if (services < 0 || start <= services || lines.Count(static line => { ArgumentNullException.ThrowIfNull(line); return line == "services:"; }) != 1 ||
            lines.Count(static line => { ArgumentNullException.ThrowIfNull(line); return line == "  verify:"; }) != 1 || lines.Any(static line =>
            {
                ArgumentNullException.ThrowIfNull(line);
                return line.StartsWith("include:", StringComparison.Ordinal) || line.Contains("extends:", StringComparison.Ordinal);
            }))
        { throw new InvalidOperationException("Compose must have one explicit verification service without imports or extensions."); }
        var end = start + 1;
        while (end < lines.Length && lines[end].StartsWith("    ", StringComparison.Ordinal))
        { end++; }
        return lines[start..end];
    }

    private static bool HasVolumes(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var start = Array.IndexOf(lines, "volumes:");
        if (start < 0 || lines.Count(static line => { ArgumentNullException.ThrowIfNull(line); return line == "volumes:"; }) != 1)
        { return false; }
        var required = new HashSet<string>(StringComparer.Ordinal) { "  sharpproof-obj:", "  sharpproof-bin:", "  sharpproof-packages:", "  sharpproof-dotnet:" };
        for (var index = start + 1; index < lines.Length && lines[index].StartsWith(' '); index++)
        {
            if (lines[index] is not ("  sharpproof-obj:" or "  sharpproof-bin:" or "  sharpproof-packages:" or "  sharpproof-dotnet:"))
            { continue; }
            if (!required.Remove(lines[index]) || index + 1 < lines.Length && lines[index + 1].StartsWith("    ", StringComparison.Ordinal))
            { return false; }
        }
        return required.Count == 0;
    }

    private static void Isolation(XElement root, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var matches = root.Descendants(name).ToArray();
        if (matches.Length > 1 || matches.Length == 1 && (matches[0].Value != value ||
            (string?)matches[0].Parent?.Attribute("Condition") != Guard || matches[0].Attribute("Condition") != null))
        { throw new InvalidOperationException("Existing build output paths conflict with guarded container isolation."); }
        if (matches.Length == 1)
        { return; }
        var group = root.Elements("PropertyGroup").FirstOrDefault(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return (string?)element.Attribute("Condition") == Guard;
        });
        if (group == null)
        {
            group = new XElement("PropertyGroup", new XAttribute("Condition", Guard));
            SetupInitializerFiles.Append(root, group);
        }
        SetupInitializerFiles.Append(group, new XElement(name, value));
    }
}
