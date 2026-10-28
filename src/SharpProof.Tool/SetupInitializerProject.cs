using System.Xml.Linq;

namespace SharpProof.Tool;

internal static class SetupInitializerProject
{
    private static readonly string[] PackageOperations = ["Include", "Update", "Remove"];

    internal static void Plan(SetupInitializerFiles files, string projectPath, string verification, string version)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(verification);
        ArgumentException.ThrowIfNullOrEmpty(version);
        var directory = Path.GetDirectoryName(projectPath)!;
        var project = SetupInitializerFiles.Xml(files.Read(projectPath), "Project");
        var root = project.Root!;
        var sdk = (string?)root.Attribute("Sdk");
        if (sdk == null || !sdk.StartsWith("Microsoft.NET.Sdk", StringComparison.Ordinal) || sdk.Contains('$', StringComparison.Ordinal) || sdk.Contains(';', StringComparison.Ordinal))
        { throw new InvalidOperationException("Initialization supports SDK-style Microsoft.NET.Sdk projects with a literal SDK declaration."); }
        RequireNoImports(root);
        foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets" })
        {
            var inherited = SetupInitializerFiles.AncestorFile(directory, name);
            if (inherited == null)
            { continue; }
            var inheritedRoot = SetupInitializerFiles.Xml(files.Read(inherited), "Project").Root!;
            RequireNoImports(inheritedRoot);
            if (inheritedRoot.Descendants().Any(static element =>
            {
                ArgumentNullException.ThrowIfNull(element);
                return element.Name.LocalName.StartsWith("SharpProof", StringComparison.OrdinalIgnoreCase) && !element.Name.LocalName.Equals("SharpProofProjectOutputKey", StringComparison.OrdinalIgnoreCase) ||
                    element.Name.LocalName.Equals("ManagePackageVersionsCentrally", StringComparison.OrdinalIgnoreCase) ||
                    (element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase) || element.Name.LocalName.Equals("PackageVersion", StringComparison.OrdinalIgnoreCase)) &&
                    PackageOperations.Any(name =>
                        element.Attribute(name ?? throw new ArgumentNullException(nameof(name)))?.Value.Split(';').Any(static value =>
                            (value ?? throw new ArgumentNullException(nameof(value))).Trim().StartsWith("SharpProof", StringComparison.OrdinalIgnoreCase) || value.IndexOfAny(['$', '@', '*', '?']) >= 0) == true);
            }))
            { throw new InvalidOperationException("Inherited SharpProof configuration is ambiguous; initialize that configuration manually."); }
        }
        var centralPath = SetupInitializerFiles.AncestorFile(directory, "Directory.Packages.props");
        var central = centralPath == null ? null : SetupInitializerFiles.Xml(files.Read(centralPath), "Project");
        if (central != null)
        { RequireNoImports(central.Root!); }
        var projectManaged = ReadProperty(root, "ManagePackageVersionsCentrally");
        var centralManaged = central == null ? null : ReadProperty(central.Root!, "ManagePackageVersionsCentrally");
        foreach (var value in new[] { projectManaged, centralManaged })
        {
            if (value != null && !value.Equals("true", StringComparison.OrdinalIgnoreCase) && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Central package management must use a literal true or false value."); }
        }
        var managed = (projectManaged ?? centralManaged)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        if (central != null && projectManaged == null && centralManaged == null)
        { throw new InvalidOperationException("Central package version activation is ambiguous."); }
        if (managed && central == null)
        {
            centralPath = Path.Combine(directory, "Directory.Packages.props");
            central = SetupInitializerFiles.Xml(null, "Project");
            SetProperty(central.Root!, "ManagePackageVersionsCentrally", "true");
        }
        foreach (var package in new[] { "SharpProof.Attributes", "SharpProof.Verifier" })
        {
            foreach (var element in Items(root, "PackageReference").Concat(
                central == null ? Enumerable.Empty<XElement>() : Items(central.Root!, "PackageVersion")))
            {
                var identity = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update") ?? (string?)element.Attribute("Remove");
                if (!string.Equals(identity, package, StringComparison.OrdinalIgnoreCase))
                { continue; }
                var pinned = Metadata(element, "Version");
                if (Metadata(element, "VersionOverride") != null || pinned != null && pinned != version ||
                    element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase) && (managed ? pinned != null : pinned == null) ||
                    element.Attribute("Update") != null || element.Attribute("Remove") != null || element.AncestorsAndSelf().Any(static ancestor =>
                    {
                        ArgumentNullException.ThrowIfNull(ancestor);
                        return ancestor.Attribute("Condition") != null;
                    }))
                { throw new InvalidOperationException("Existing SharpProof package versions conflict with the installed tool version: " + package); }
                if (managed && element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase) && !Items(central!.Root!, "PackageVersion").Any(candidate =>
                {
                    ArgumentNullException.ThrowIfNull(candidate);
                    return string.Equals((string?)candidate.Attribute("Include"), package, StringComparison.OrdinalIgnoreCase);
                }))
                { throw new InvalidOperationException("Existing SharpProof package versions require an explicit matching central version: " + package); }
            }
        }
        SetProperty(root, "SharpProofProfile", verification == "portable" ? "advisory" : "strict");
        SetProperty(root, "SharpProofVerify", verification == "portable" ? "false" : "true");
        if (verification == "container")
        {
            SetProperty(root, "SharpProofVerifyResultFile", "$(MSBuildProjectDirectory)/artifacts/sharpproof/result.json");
            SetProperty(root, "SharpProofVerifyRequestFile", "$(MSBuildProjectDirectory)/artifacts/sharpproof/request.json");
            SetProperty(root, "SharpProofCompilerManifestFile", "$(MSBuildProjectDirectory)/artifacts/sharpproof/compiler-manifest.json");
            SetProperty(root, "SharpProofVerifySarifFile", "$(MSBuildProjectDirectory)/artifacts/sharpproof/result.sarif");
        }
        foreach (var name in new[] { "SharpProof", "SharpProof.CodeFixes" })
        {
            var reference = Reference(root, "PackageReference", name);
            var pinned = Metadata(reference, "Version");
            if (Metadata(reference, "VersionOverride") != null || pinned != null && pinned != version)
            { throw new InvalidOperationException("Existing SharpProof package versions conflict with the installed tool version."); }
            if (name == "SharpProof" && Metadata(reference, "PrivateAssets") is { } privateAssets && !privateAssets.Equals("none", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("SharpProof must remain a public dependency; review the existing PrivateAssets setting."); }
            if (managed)
            {
                reference.Attributes().Where(static metadata =>
                    (metadata ?? throw new ArgumentNullException(nameof(metadata))).Name.ToString().Equals("Version", StringComparison.OrdinalIgnoreCase)).Remove();
                reference.Elements().Where(static metadata =>
                    (metadata ?? throw new ArgumentNullException(nameof(metadata))).Name.ToString().Equals("Version", StringComparison.OrdinalIgnoreCase)).Remove();
                SetMetadata(Reference(central!.Root!, "PackageVersion", name), "Version", version);
            }
            else
            { SetMetadata(reference, "Version", version); }
            if (name == "SharpProof.CodeFixes")
            { SetMetadata(reference, "PrivateAssets", "all"); }
        }
        files.Add(projectPath, SetupInitializerFiles.Serialize(project));
        if (managed)
        { files.Add(centralPath!, SetupInitializerFiles.Serialize(central!)); }
    }

    internal static void RequireNoImports(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Descendants().Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Name.LocalName is "Import" or "ImportGroup" or "Sdk";
        }))
        { throw new InvalidOperationException("Explicit SDK/import chains are ambiguous; initialize them manually."); }
    }

    private static string? ReadProperty(XElement root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var matches = root.Descendants().Where(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (matches.Length > 1 || matches.Any(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Parent?.Name != "PropertyGroup" || element.Parent?.Parent != root || element.AncestorsAndSelf().Any(static ancestor =>
            {
                ArgumentNullException.ThrowIfNull(ancestor);
                return ancestor.Attribute("Condition") != null;
            });
        }))
        { throw new InvalidOperationException("Conditional or repeated setup properties require manual initialization."); }
        return matches.Length == 0 ? null : matches[0].Value.Trim();
    }

    internal static void SetProperty(XElement root, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var existing = ReadProperty(root, name);
        if (existing != null)
        {
            if (!existing.Equals(value, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("An existing setup property conflicts with the requested configuration: " + name); }
            return;
        }
        SetupInitializerFiles.Append(Group(root, "PropertyGroup"), new XElement(name, value));
    }

    private static IEnumerable<XElement> Items(XElement root, string itemName)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(itemName);
        return root.Descendants().Where(element =>
            (element ?? throw new ArgumentNullException(nameof(element))).Name.ToString().Equals(itemName, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement Reference(XElement root, string itemName, string package)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(itemName);
        ArgumentException.ThrowIfNullOrEmpty(package);
        var matches = Items(root, itemName).Where(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            var identity = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update") ?? (string?)element.Attribute("Remove");
            return identity?.Equals(package, StringComparison.OrdinalIgnoreCase) == true;
        }).ToArray();
        if (matches.Length > 1 || matches.Any(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Attribute("Include") == null || element.Parent?.Name != "ItemGroup" || element.Parent?.Parent != root || element.AncestorsAndSelf().Any(static ancestor =>
            {
                ArgumentNullException.ThrowIfNull(ancestor);
                return ancestor.Attribute("Condition") != null;
            });
        }))
        { throw new InvalidOperationException("Repeated, updated, or conditional SharpProof references require manual initialization."); }
        if (matches.Length == 1)
        { return matches[0]; }
        var reference = new XElement(itemName, new XAttribute("Include", package));
        SetupInitializerFiles.Append(Group(root, "ItemGroup"), reference);
        return reference;
    }

    private static XElement Group(XElement root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var group = root.Elements(name).FirstOrDefault(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return element.Attribute("Condition") == null;
        });
        if (group != null)
        { return group; }
        group = new XElement(name);
        SetupInitializerFiles.Append(root, group);
        return group;
    }

    private static string? Metadata(XElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(name);
        // MSBuild metadata names are case-insensitive, including attribute syntax.
        var attributes = element.Attributes().Where(metadata =>
            (metadata ?? throw new ArgumentNullException(nameof(metadata))).Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var children = element.Elements().Where(metadata =>
            (metadata ?? throw new ArgumentNullException(nameof(metadata))).Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (attributes.Length > 1 || children.Length > 1 || attributes.Length != 0 && children.Length > 0 || children.Any(static child =>
        {
            ArgumentNullException.ThrowIfNull(child);
            return child.Attribute("Condition") != null;
        }))
        { throw new InvalidOperationException("Ambiguous package metadata requires manual initialization."); }
        return attributes.SingleOrDefault()?.Value ?? children.SingleOrDefault()?.Value;
    }

    private static void SetMetadata(XElement element, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var existing = Metadata(element, name);
        if (existing != null && !existing.Equals(value, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidOperationException("Existing package metadata conflicts with initializer settings: " + name); }
        if (existing == null)
        { element.Add(new XAttribute(name, value)); }
    }
}
