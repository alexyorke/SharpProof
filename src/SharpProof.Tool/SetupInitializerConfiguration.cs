using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SharpProof.Tool;

internal static class SetupInitializerConfiguration
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
    private static readonly string[] NuGetNames = ["NuGet.Config", "nuget.config", "NuGet.config"];
    private const string FeedName = "SharpProofPreview";
    private const string FeedUrl = "https://nuget.pkg.github.com/alexyorke/index.json";

    internal static void Plan(SetupInitializerFiles files, string directory, string version)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(version);
        PlanFeed(files, directory);
        PlanManifest(files, directory, version);
    }

    private static void PlanFeed(SetupInitializerFiles files, string directory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string? path = null;
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            var candidates = NuGetNames.Select(name =>
            {
                ArgumentNullException.ThrowIfNull(name);
                return Path.Combine(current.FullName, name);
            }).Where(static candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return File.Exists(candidate) || Directory.Exists(candidate);
            }).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
            if (candidates.Length > 1)
            { throw new InvalidOperationException("Multiple NuGet configuration files make initialization ambiguous."); }
            if (candidates.Length == 1)
            { path = candidates[0]; break; }
        }
        path ??= Path.Combine(directory, "NuGet.Config");
        var document = SetupInitializerFiles.Xml(files.Read(path), "configuration");
        var root = document.Root!;
        var sources = Section(root, "packageSources");
        var source = Entries(sources, "add", "key", FeedName).SingleOrDefault();
        if (source != null && !string.Equals((string?)source.Attribute("value"), FeedUrl, StringComparison.Ordinal))
        { throw new InvalidOperationException("The existing SharpProofPreview source points to a different location."); }
        if (root.Element("packageSourceCredentials")?.Elements().Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Name.LocalName.Equals(FeedName, StringComparison.OrdinalIgnoreCase);
        }) == true)
        { throw new InvalidOperationException("Keep SharpProofPreview credentials in a credential provider or environment, outside the generated configuration."); }
        var disabled = root.Element("disabledPackageSources");
        if (disabled != null && Entries(disabled, "add", "key", FeedName).Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return ((string?)element.Attribute("value"))?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }))
        { throw new InvalidOperationException("The SharpProofPreview source is explicitly disabled."); }
        if (source == null)
        { SetupInitializerFiles.Append(sources, new XElement("add", new XAttribute("key", FeedName), new XAttribute("value", FeedUrl))); }
        if (!sources.Elements("add").Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return !string.Equals((string?)element.Attribute("key"), FeedName, StringComparison.OrdinalIgnoreCase);
        }))
        { SetupInitializerFiles.Append(sources, new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))); }
        var hadMapping = root.Element("packageSourceMapping") != null;
        var mapping = Section(root, "packageSourceMapping");
        foreach (var mapped in mapping.Elements("packageSource"))
        {
            if (string.Equals((string?)mapped.Attribute("key"), FeedName, StringComparison.OrdinalIgnoreCase))
            { continue; }
            if (mapped.Elements("package").Any(static package =>
            {
                ArgumentNullException.ThrowIfNull(package);
                return ((string?)package.Attribute("pattern"))?.StartsWith("SharpProof", StringComparison.OrdinalIgnoreCase) == true;
            }))
            { throw new InvalidOperationException("SharpProof packages are already mapped to another package source."); }
        }
        if (!hadMapping)
        {
            foreach (var external in sources.Elements("add"))
            {
                var key = (string?)external.Attribute("key");
                if (!string.IsNullOrEmpty(key) && !key.Equals(FeedName, StringComparison.OrdinalIgnoreCase))
                { SetupInitializerFiles.Append(mapping, new XElement("packageSource", new XAttribute("key", key), new XElement("package", new XAttribute("pattern", "*")))); }
            }
        }
        var previewMapping = Entries(mapping, "packageSource", "key", FeedName).SingleOrDefault();
        if (previewMapping == null)
        {
            previewMapping = new XElement("packageSource", new XAttribute("key", FeedName));
            SetupInitializerFiles.Append(mapping, previewMapping);
        }
        if (!previewMapping.Elements("package").Any(static package =>
        {
            ArgumentNullException.ThrowIfNull(package);
            return (string?)package.Attribute("pattern") == "SharpProof*";
        }))
        { SetupInitializerFiles.Append(previewMapping, new XElement("package", new XAttribute("pattern", "SharpProof*"))); }
        files.Add(path, SetupInitializerFiles.Serialize(document));
    }

    private static void PlanManifest(SetupInitializerFiles files, string directory, string version)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(version);
        string? path = null;
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            var local = Path.Combine(current.FullName, ".config", "dotnet-tools.json");
            var root = Path.Combine(current.FullName, "dotnet-tools.json");
            if ((File.Exists(local) || Directory.Exists(local)) && (File.Exists(root) || Directory.Exists(root)))
            { throw new InvalidOperationException("Multiple tool manifests make initialization ambiguous."); }
            if (File.Exists(local) || Directory.Exists(local))
            { path = local; break; }
            if (File.Exists(root) || Directory.Exists(root))
            { path = root; break; }
        }
        path ??= Path.Combine(directory, ".config", "dotnet-tools.json");
        var original = files.Read(path);
        JsonObject manifest;
        try
        { manifest = original == null ? new JsonObject { ["version"] = 1, ["isRoot"] = true, ["tools"] = new JsonObject() } : JsonNode.Parse(original.TrimStart('\uFEFF'))?.AsObject() ?? throw new InvalidDataException("The tool manifest must be a JSON object."); }
        catch (JsonException)
        { throw new InvalidDataException("The tool manifest must be well-formed JSON."); }
        if (manifest["version"]?.GetValue<int>() != 1 || manifest["isRoot"] is not JsonValue || manifest["tools"] is not JsonObject tools)
        { throw new InvalidOperationException("Only a simple version-1 .NET tool manifest is supported."); }
        _ = manifest["isRoot"]!.GetValue<bool>();
        var keys = tools.Select(static entry =>
        {
            global::SharpProof.Internal.InputValidation.Require(entry, nameof(entry));
            return entry.Key;
        }).Where(static key =>
        {
            ArgumentNullException.ThrowIfNull(key);
            return key.Equals("sharpproof.tool", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (keys.Length > 1)
        { throw new InvalidOperationException("The tool manifest contains duplicate SharpProof tool entries."); }
        if (keys.Length == 1)
        {
            var existing = tools[keys[0]]?.AsObject();
            if (existing == null || existing["version"]?.GetValue<string>() != version || existing["commands"] is not JsonArray commands ||
                commands.Count != 1 || commands[0]?.GetValue<string>() != "dotnet-sharpproof")
            { throw new InvalidOperationException("The existing SharpProof.Tool manifest entry conflicts with this tool version or command."); }
        }
        else
        {
            tools.Add("sharpproof.tool", new JsonObject
            {
                ["version"] = version,
                ["commands"] = new JsonArray("dotnet-sharpproof")
            });
        }
        files.Add(path, manifest.ToJsonString(ManifestJsonOptions) + "\n");
    }

    private static XElement Section(XElement root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var sections = root.Elements(name).ToArray();
        if (sections.Length > 1)
        { throw new InvalidOperationException("Repeated NuGet configuration sections require manual initialization."); }
        if (sections.Length == 1)
        { return sections[0]; }
        var section = new XElement(name);
        SetupInitializerFiles.Append(root, section);
        return section;
    }

    private static XElement[] Entries(XElement section, string elementName, string attributeName, string value)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrEmpty(elementName);
        ArgumentException.ThrowIfNullOrEmpty(attributeName);
        ArgumentNullException.ThrowIfNull(value);
        var entries = section.Elements(elementName).Where(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return string.Equals((string?)element.Attribute(attributeName), value, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (entries.Length > 1)
        { throw new InvalidOperationException("Repeated setup entries require manual initialization."); }
        return entries;
    }
}
