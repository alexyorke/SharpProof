using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.TestSupport;

internal static class DiagnosticDescriptorCatalogAssertions
{
    private const string RepositoryHelpPrefix =
        "https://github.com/alexyorke/SharpProof/blob/master/";

    internal static void AssertOutput(
        string outputName,
        Assembly assembly)
    {
        using var catalog = ReadCatalog();
        Assert.That(
            catalog.RootElement.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        var outputs = catalog.RootElement.GetProperty("outputs")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            outputs.Select(static output =>
                output.GetProperty("name").GetString()),
            Is.EqualTo([
                "analyzer",
                "contractForGenerator",
                "metaAnalyzer"
            ]));
        var output = outputs.Single(candidate =>
            candidate.GetProperty("name").GetString() == outputName);
        AssertOutput(output, assembly);
    }

    private static void AssertOutput(
        JsonElement output,
        Assembly assembly)
    {
        var name = output.GetProperty("name").GetString()!;
        var typeName =
            output.GetProperty("namespace").GetString() + "." +
            output.GetProperty("className").GetString();
        var type = assembly.GetType(typeName, throwOnError: true)!;
        var specifications = output.GetProperty("diagnostics")
            .EnumerateArray()
            .ToArray();
        var fields = type.GetFields(
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(static field =>
                field.FieldType == typeof(DiagnosticDescriptor))
            .OrderBy(static field => field.MetadataToken)
            .ToArray();

        Assert.That(
            fields.Select(static field => field.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("symbol").GetString())),
            name);
        for (var index = 0; index < fields.Length; index++)
        {
            Assert.That(
                specifications[index].GetProperty("order").GetInt32(),
                Is.EqualTo(index),
                fields[index].Name);
            AssertDescriptor(
                (DiagnosticDescriptor)fields[index].GetValue(null)!,
                specifications[index]);
        }

        var supportedMember =
            output.GetProperty("supportedDiagnosticsMember");
        if (supportedMember.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var aggregate = type.GetField(
            supportedMember.GetString()!,
            BindingFlags.NonPublic |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly);
        Assert.That(aggregate, Is.Not.Null, name);
        var descriptors =
            ((IEnumerable<DiagnosticDescriptor>)aggregate!.GetValue(null)!)
            .ToArray();
        Assert.That(
            descriptors,
            Is.EqualTo(fields.Select(static field =>
                (DiagnosticDescriptor)field.GetValue(null)!)),
            name);
    }

    private static void AssertDescriptor(
        DiagnosticDescriptor descriptor,
        JsonElement specification)
    {
        var id = specification.GetProperty("id").GetString()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.Id, Is.EqualTo(id));
            Assert.That(
                descriptor.Title.ToString(CultureInfo.InvariantCulture),
                Is.EqualTo(specification.GetProperty("title").GetString()),
                id);
            Assert.That(
                descriptor.MessageFormat.ToString(
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    specification.GetProperty("messageFormat").GetString()),
                id);
            Assert.That(
                descriptor.Category,
                Is.EqualTo(specification.GetProperty("category").GetString()),
                id);
            Assert.That(
                descriptor.DefaultSeverity.ToString(),
                Is.EqualTo(
                    specification.GetProperty("defaultSeverity").GetString()),
                id);
            Assert.That(
                descriptor.IsEnabledByDefault,
                Is.EqualTo(specification
                    .GetProperty("isEnabledByDefault")
                    .GetBoolean()),
                id);
            Assert.That(
                descriptor.Description.ToString(
                    CultureInfo.InvariantCulture),
                Is.EqualTo(
                    specification.GetProperty("description").GetString()),
                id);
            Assert.That(
                descriptor.CustomTags,
                Is.EqualTo(specification.GetProperty("customTags")
                    .EnumerateArray()
                    .Select(static tag => tag.GetString())),
                id);
        }

        var helpElement = specification.GetProperty("helpLinkUri");
        var expectedHelp = helpElement.ValueKind == JsonValueKind.Null
            ? string.Empty
            : helpElement.GetString()!;
        Assert.That(descriptor.HelpLinkUri, Is.EqualTo(expectedHelp), id);
        if (expectedHelp.Length != 0)
        {
            AssertRepositoryHelpLink(expectedHelp, id);
        }
    }

    private static void AssertRepositoryHelpLink(string link, string id)
    {
        Assert.That(
            link,
            Does.StartWith(RepositoryHelpPrefix),
            id);
        var uri = new Uri(link, UriKind.Absolute);
        var relativePath = Uri.UnescapeDataString(
            link[RepositoryHelpPrefix.Length..].Split('#')[0]);
        var targetPath = Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(targetPath), Is.True, id);
        var fragment = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        Assert.That(fragment, Is.Not.Empty, id);
        Assert.That(
            MarkdownAnchors(File.ReadAllText(targetPath)).Contains(fragment),
            Is.True,
            id);
    }

    private static HashSet<string> MarkdownAnchors(string text)
    {
        var anchors = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     text,
                     """<a\s+id\s*=\s*["'](?<id>[^"']+)["']""",
                     RegexOptions.IgnoreCase))
        {
            anchors.Add(match.Groups["id"].Value);
        }

        foreach (Match match in Regex.Matches(
                     text,
                     @"^(?:#{1,6})[ \t]+(?<heading>.+?)[ \t]*#*[ \t]*$",
                     RegexOptions.Multiline))
        {
            var heading = Regex.Replace(
                match.Groups["heading"].Value,
                "<[^>]+>|`",
                string.Empty);
            var slug = Regex.Replace(
                heading.ToUpperInvariant(),
                @"[^\p{L}\p{Nd}\s-]",
                string.Empty);
            anchors.Add(Regex.Replace(slug.Trim(), @"\s+", "-"));
        }
        return anchors;
    }

    private static JsonDocument ReadCatalog()
    {
        var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "eng",
            "diagnostics",
            "diagnostic-descriptors.v1.json")));
        try
        {
            AssertUniqueProperties(document.RootElement, "diagnostic catalog");
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void AssertUniqueProperties(
        JsonElement value,
        string context)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                AssertUniqueProperties(item, $"{context}[{index}]");
                index++;
            }
            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException(
                    $"{context} contains duplicate property '{property.Name}'.");
            }
            AssertUniqueProperties(
                property.Value,
                $"{context}.{property.Name}");
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException(
            "Could not find the repository root.");
    }
}
