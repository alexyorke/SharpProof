using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed partial class DotnetApiInventoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    [Test]
    public void PinnedFrameworkApiSurfaceAndSupportEvidenceMatchReviewedBaseline()
    {
        var root = RepositoryRoot.Find(AppContext.BaseDirectory, "SharpProof.slnx", "global.json") ?? throw new InvalidOperationException("Repository root unavailable.");
        var pack = Path.Combine(new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName, "packs", "Microsoft.NETCore.App.Ref", DotnetApiInventory.PackVersion, "ref", DotnetApiInventory.Framework);
        Assert.That(Directory.Exists(pack), Is.True, "Pinned reference pack is missing; do not fall back to runtime assemblies.");
        var files = Directory.GetFiles(pack, "*.dll").Order(StringComparer.Ordinal).ToArray();
        Assert.That(files, Is.Not.Empty);
        var compilation = CSharpCompilation.Create("DotnetApiInventory", references: files.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var rows = DotnetApiInventory.Collect(compilation);
        Assert.That(rows.Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Key)).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(rows.Length));
        var current = rows.Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Line)).ToArray();
        var baselineDirectory = Path.Combine(root, "eng", "api-coverage");
        var baselinePath = Path.Combine(baselineDirectory, "net10.0.tsv");
        var manifestPath = Path.Combine(baselineDirectory, "net10.0.manifest.json");
        var manifest = JsonSerializer.Serialize(new { schemaVersion = 1, sdkVersion = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json"))).RootElement.GetProperty("sdk").GetProperty("version").GetString(), pack = "Microsoft.NETCore.App.Ref", version = DotnetApiInventory.PackVersion, framework = DotnetApiInventory.Framework, catalogSha256 = ApiSpecTable.Default.ContentSha256, assemblies = files.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (new { name = Path.GetFileName(path), sha256 = DotnetApiInventory.Fingerprint(path) })) }, JsonOptions) + "\n";
        var before = File.Exists(baselinePath) ? File.ReadAllLines(baselinePath) : [];
        var delta = DotnetApiInventory.Compare(before, current);
        var output = Path.Combine(root, "artifacts", "api-coverage");
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "inventory.tsv"), current, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(output, "delta.json"), JsonSerializer.Serialize(delta, JsonOptions) + "\n");
        File.WriteAllText(Path.Combine(output, "report.md"), DotnetApiInventory.Markdown(rows, delta));
        WriteIntrinsicTestEvidence(root, compilation, rows);
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String,System.String)")).Evidence, Does.Contain("intrinsic=string-static-equality"));
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String)")).Evidence, Does.Contain("intrinsic=string-instance-equality"));
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String,System.String,System.StringComparison)")).Evidence, Is.Empty, "An intrinsic match must not grant support to other overloads.");
        // Explicit local maintenance writes a candidate under artifacts. CI never
        // rewrites its authority or silently approves a changed SDK/catalog.
        if (Environment.GetEnvironmentVariable("SHARPPROOF_API_INVENTORY_CANDIDATE") == "1")
        {
            Assert.That(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), Is.Not.EqualTo("true"), "Candidate mode must not bypass the CI baseline gate.");
            return;
        }

        Assert.That(File.Exists(baselinePath) && File.Exists(manifestPath), Is.True, "Reviewed API baseline is missing.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delta.Added, Is.Empty, "New APIs need review; see artifacts/api-coverage/report.md.");
            Assert.That(delta.Removed, Is.Empty, "APIs disappeared; verify the reference pack and baseline.");
            Assert.That(delta.Changed, Is.Empty, "Support evidence changed; review for regressions before updating the baseline.");
            Assert.That(manifest, Is.EqualTo(File.ReadAllText(manifestPath)), "SDK, assembly bytes, or catalog semantics changed; review manifest.json.");
        }
    }

    [Test]
    public void DeltaDetectsNewRemovedAndRegressedApisWithoutCountCancellation()
    {
        var delta = DotnetApiInventory.Compare(["A\tM:Supported\tMethod\tpartially-modeled\teffects=None", "A\tM:Removed\tMethod\tunmodeled\t"], ["A\tM:Supported\tMethod\tunmodeled\t", "A\tM:New\tMethod\tunmodeled\t"]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delta.Added.Single(), Is.EqualTo("A\tM:New"));
            Assert.That(delta.Removed.Single(), Is.EqualTo("A\tM:Removed"));
            Assert.That(delta.Changed.Single(), Is.EqualTo("A\tM:Supported"));
        }
    }

    [Test]
    public void DeltaRejectsDuplicateIdentitiesAndIgnoresEnumerationOrder()
    {
        Assert.Throws<ArgumentException>((Action)(() => DotnetApiInventory.Compare(["A\tM:X", "A\tM:X"], [])));
        var delta = DotnetApiInventory.Compare(["A\tM:X", "B\tM:X"], ["B\tM:X", "A\tM:X"]);
        Assert.That(delta.Added.Concat(delta.Removed).Concat(delta.Changed), Is.Empty);
    }

    [Test]
    public void SurfaceTraversalPreservesOverloadsAndVisibilityWithoutAccessorDuplicates()
    {
        var compilation = CSharpCompilation.Create("InventoryShape", [CSharpSyntaxTree.ParseText("""
            public class Outer
            {
                protected Outer() { }
                public void Overload(int x) { }
                public void Overload(string x) { }
                public int Value { get; private set; }
                protected internal class Visible { public void Method() { } }
                private class Hidden { public void Method() { } }
                internal void Internal() { }
                private protected void PrivateProtected() { }
            }
            internal class HiddenOuter { public class Nested { } }
            """)], references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        var declarations = DotnetApiInventory.Members(compilation.Assembly.GlobalNamespace).Select(symbol => symbol is null ? throw new global::System.ArgumentNullException(nameof(symbol)) : (symbol.GetDocumentationCommentId())).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(declarations, Does.Contain("M:Outer.Overload(System.Int32)"));
            Assert.That(declarations, Does.Contain("M:Outer.Overload(System.String)"));
            Assert.That(declarations, Does.Contain("T:Outer.Visible"));
            Assert.That(declarations, Does.Contain("M:Outer.#ctor"));
            Assert.That(declarations, Does.Contain("P:Outer.Value"));
            Assert.That(declarations.Any(id => id is null ? throw new global::System.ArgumentNullException(nameof(id)) : (id!.Contains("Hidden", StringComparison.Ordinal) || id.Contains("Internal", StringComparison.Ordinal) || id.Contains("PrivateProtected", StringComparison.Ordinal) || id.Contains("get_", StringComparison.Ordinal) || id.Contains("set_", StringComparison.Ordinal))), Is.False);
        }
    }
}
