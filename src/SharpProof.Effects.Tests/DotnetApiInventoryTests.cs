using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void PinnedFrameworkApiSupportEvidenceIsRecorded()
    {
        var root = RepositoryRoot.Find(AppContext.BaseDirectory, "SharpProof.slnx", "global.json") ?? throw new InvalidOperationException("Repository root unavailable.");
        var pack = Path.Combine(new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName, "packs", "Microsoft.NETCore.App.Ref", DotnetApiInventory.PackVersion, "ref", DotnetApiInventory.Framework);
        Assert.That(Directory.Exists(pack), Is.True, "Pinned reference pack is missing; do not fall back to runtime assemblies.");
        var files = Directory.GetFiles(pack, "*.dll").Order(StringComparer.Ordinal).ToImmutableArray();
        Assert.That(files, Is.Not.Empty);
        var compilation = CSharpCompilation.Create("DotnetApiInventory", references: files.Select(path => path is null ? throw new global::System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path))), options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var rows = DotnetApiInventory.Collect(compilation);
        Assert.That(rows.Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Key)).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(rows.Length));
        WriteIntrinsicTestEvidence(root, compilation, rows);
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String,System.String)")).Evidence, Does.Contain("intrinsic=string-static-equality"));
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String)")).Evidence, Does.Contain("intrinsic=string-instance-equality"));
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Id == "M:System.String.Equals(System.String,System.String,System.StringComparison)")).Evidence, Is.Empty, "An intrinsic match must not grant support to other overloads.");
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
        var declarations = DotnetApiInventory.Members(compilation.Assembly.GlobalNamespace).Select(symbol => symbol is null ? throw new global::System.ArgumentNullException(nameof(symbol)) : (symbol.GetDocumentationCommentId())).ToImmutableArray();
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
