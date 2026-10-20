using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TestCompilationImmutableBoundaryTests
{
    [Test]
    public void ExactCompilationRetainsNameDuplicatePathsAndSourceOrder()
    {
        const string assemblyName = "ImmutableExactCompilation";
        const string first = "internal sealed class First { }";
        const string second = "internal sealed class Second { }";
        ImmutableArray<(string FileName, string Source)> sources =
            [("shared.cs", first), ("shared.cs", second)];
        var compilation = TestCompilation.CreateExact(assemblyName, sources);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.AssemblyName, Is.EqualTo(assemblyName));
            Assert.That(compilation.SyntaxTrees.Select(tree => tree is null ? throw new ArgumentNullException(nameof(tree)) : tree.FilePath),
                Is.EqualTo(ImmutableArray.Create("shared.cs", "shared.cs")));
            Assert.That(compilation.SyntaxTrees.Select(tree => tree is null ? throw new ArgumentNullException(nameof(tree)) : tree.ToString()),
                Is.EqualTo(ImmutableArray.Create(first, second)));
        }
    }

    [Test]
    public void ExactOutputCompilationRetainsNameAndOutputKind()
    {
        const string assemblyName = "ImmutableExactOutputCompilation";
        var compilation = TestCompilation.CreateExact(assemblyName,
            OutputKind.DynamicallyLinkedLibrary,
            [("Subject.cs", "internal sealed class Subject { }")]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.AssemblyName, Is.EqualTo(assemblyName));
            Assert.That(compilation.Options.OutputKind,
                Is.EqualTo(OutputKind.DynamicallyLinkedLibrary));
        }
    }

    [Test]
    public void ExistingVectorOverloadRetainsUniqueAssemblySuffix()
    {
        const string prefix = "ImmutableSuffixingCompilation";
        ImmutableArray<(string FileName, string Source)> sources =
            [("Subject.cs", "internal sealed class Subject { }")];
        var compilation = TestCompilation.Create(prefix, sources);
        Assert.That(compilation.AssemblyName, Does.StartWith(prefix + "_"));
    }

    [TestCase(false, "sources")]
    [TestCase(true, "assemblyName")]
    public void ExactHelpersRetainGuardOrder(bool withOutputKind, string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            if (withOutputKind)
            {
                TestCompilation.CreateExact(null!, OutputKind.DynamicallyLinkedLibrary, default);
            }
            else
            {
                TestCompilation.CreateExact(null!, default);
            }
        }));
        Assert.That(error!.ParamName, Is.EqualTo(parameter));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExactHelpersAcceptInitializedEmptySources(bool withOutputKind)
    {
        const string assemblyName = "ImmutableEmptyCompilation";
        var compilation = withOutputKind
            ? TestCompilation.CreateExact(assemblyName, OutputKind.DynamicallyLinkedLibrary,
                ImmutableArray<(string FileName, string Source)>.Empty)
            : TestCompilation.CreateExact(assemblyName,
                ImmutableArray<(string FileName, string Source)>.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilation.AssemblyName, Is.EqualTo(assemblyName));
            Assert.That(compilation.SyntaxTrees, Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExactHelpersRejectDefaultSourcesWithValidName(bool withOutputKind)
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            if (withOutputKind)
            {
                TestCompilation.CreateExact("ImmutableDefaultCompilation",
                    OutputKind.DynamicallyLinkedLibrary, default);
            }
            else
            {
                TestCompilation.CreateExact("ImmutableDefaultCompilation", default);
            }
        }));
        Assert.That(error!.ParamName, Is.EqualTo("sources"));
    }
}
