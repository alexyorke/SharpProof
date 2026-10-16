using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;
using SharpProof.Roslyn;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class RoslynSymbolFactsCompilerSymbolTests
{
    private const string CSharpSource = """
    public class Box<T> { }
    public sealed class Derived : Box<int> { }
    public sealed class Other { }
    public interface IMarker { }
    public sealed class Implementer : IMarker { }
    public class Constrained<T> where T : Box<int> { }
    public static class Probe
    {
        public static bool Target() => typeof(Derived).BaseType == typeof(Box<int>) &&
            !typeof(Box<string>).IsAssignableFrom(typeof(Derived)) &&
            typeof(IMarker).IsAssignableFrom(typeof(Implementer));
    }
    """ + "\n";

    private const string VisualBasicSource = """
    Public Class Box(Of T)
    End Class
    Public NotInheritable Class Derived
        Inherits Box(Of Integer)
    End Class
    Public NotInheritable Class Other
    End Class
    Public Interface IMarker
    End Interface
    Public NotInheritable Class Implementer
        Implements IMarker
    End Class
    Public Class Constrained(Of T As Box(Of Integer))
    End Class
    Public Class Probe
        Public Shared Function Target() As Boolean
            Return GetType(Derived).BaseType Is GetType(Box(Of Integer)) AndAlso
                Not GetType(Box(Of String)).IsAssignableFrom(GetType(Derived)) AndAlso
                GetType(IMarker).IsAssignableFrom(GetType(Implementer))
        End Function
    End Class
    """ + "\n";

    [TestCase("CSharp", "null-base")]
    [TestCase("CSharp", "null-type")]
    [TestCase("CSharp", "self")]
    [TestCase("CSharp", "self-excluded")]
    [TestCase("CSharp", "inherited")]
    [TestCase("CSharp", "inherited-exclude-self")]
    [TestCase("CSharp", "reverse")]
    [TestCase("CSharp", "unrelated")]
    [TestCase("CSharp", "constructed-to-definition-original")]
    [TestCase("CSharp", "constructed-to-definition-identity")]
    [TestCase("CSharp", "definition-to-constructed-original")]
    [TestCase("CSharp", "definition-to-constructed-identity")]
    [TestCase("CSharp", "constructed-self-identity")]
    [TestCase("CSharp", "inherited-constructed-identity")]
    [TestCase("CSharp", "inherited-other-constructed-identity")]
    [TestCase("CSharp", "inherited-other-constructed-original")]
    [TestCase("CSharp", "interface-implemented")]
    [TestCase("CSharp", "interface-self")]
    [TestCase("CSharp", "parameter-self")]
    [TestCase("CSharp", "parameter-object")]
    [TestCase("VisualBasic", "null-base")]
    [TestCase("VisualBasic", "null-type")]
    [TestCase("VisualBasic", "self")]
    [TestCase("VisualBasic", "self-excluded")]
    [TestCase("VisualBasic", "inherited")]
    [TestCase("VisualBasic", "inherited-exclude-self")]
    [TestCase("VisualBasic", "reverse")]
    [TestCase("VisualBasic", "unrelated")]
    [TestCase("VisualBasic", "constructed-to-definition-original")]
    [TestCase("VisualBasic", "constructed-to-definition-identity")]
    [TestCase("VisualBasic", "definition-to-constructed-original")]
    [TestCase("VisualBasic", "definition-to-constructed-identity")]
    [TestCase("VisualBasic", "constructed-self-identity")]
    [TestCase("VisualBasic", "inherited-constructed-identity")]
    [TestCase("VisualBasic", "inherited-other-constructed-identity")]
    [TestCase("VisualBasic", "inherited-other-constructed-original")]
    [TestCase("VisualBasic", "interface-implemented")]
    [TestCase("VisualBasic", "interface-self")]
    [TestCase("VisualBasic", "parameter-self")]
    [TestCase("VisualBasic", "parameter-object")]
    public void IsOrDerivesFromUsesActualCompilerTypeAncestry(string language, string relation)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(relation);
        Compilation compilation = language switch
        {
            "CSharp" => CSharpCompilation.Create("SymbolFactsCSharp",
                [CSharpSyntaxTree.ParseText(CSharpSource, path: "symbol-facts.cs")], TestMetadataReferences.Platform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release)),
            "VisualBasic" => VisualBasicCompilation.Create("SymbolFactsVisualBasic",
                [VisualBasicSyntaxTree.ParseText(VisualBasicSource, path: "symbol-facts.vb")], TestMetadataReferences.Platform,
                new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optionStrict: OptionStrict.On,
                    optimizationLevel: OptimizationLevel.Release)),
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(typeof(RoslynSymbolFacts).Assembly.GetName().Name, Is.EqualTo("SharpProof.Frontend"));

        var box = RequiredType(compilation, "Box`1");
        var derived = RequiredType(compilation, "Derived");
        var other = RequiredType(compilation, "Other");
        var marker = RequiredType(compilation, "IMarker");
        var implementer = RequiredType(compilation, "Implementer");
        var constrained = RequiredType(compilation, "Constrained`1");
        var boxInt = box.Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var boxString = box.Construct(compilation.GetSpecialType(SpecialType.System_String));
        var parameter = constrained.TypeParameters.Single();
        Assert.That(boxInt.IsDefinition, Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(boxInt.OriginalDefinition, box), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(boxInt, boxString), Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(derived.BaseType, boxInt), Is.True);
        Assert.That(implementer.Interfaces.Length, Is.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(implementer.Interfaces.Single(), marker), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(implementer.BaseType, marker), Is.False);
        Assert.That(parameter.TypeKind, Is.EqualTo(TypeKind.TypeParameter));
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, constrained), Is.True);
        Assert.That(parameter.ConstraintTypes.Length, Is.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.ConstraintTypes.Single(), boxInt), Is.True);

        (ITypeSymbol? type, ITypeSymbol? possibleBase, bool includeSelf, bool compareDefinitions, bool expected) = relation switch
        {
            "null-base" => (derived, null, true, true, false),
            "null-type" => (null, box, true, true, false),
            "self" => (box, box, true, true, true),
            "self-excluded" => (box, box, false, true, false),
            "inherited" => (derived, box, true, true, true),
            "inherited-exclude-self" => (derived, box, false, true, true),
            "reverse" => (box, derived, true, true, false),
            "unrelated" => (derived, other, true, true, false),
            "constructed-to-definition-original" => (boxInt, box, true, true, true),
            "constructed-to-definition-identity" => (boxInt, box, true, false, false),
            "definition-to-constructed-original" => (box, boxInt, true, true, true),
            "definition-to-constructed-identity" => (box, boxInt, true, false, false),
            "constructed-self-identity" => (boxInt, boxInt, true, false, true),
            "inherited-constructed-identity" => (derived, boxInt, true, false, true),
            "inherited-other-constructed-identity" => (derived, boxString, true, false, false),
            "inherited-other-constructed-original" => (derived, boxString, true, true, true),
            "interface-implemented" => (implementer, marker, true, true, false),
            "interface-self" => (marker, marker, true, true, true),
            "parameter-self" => (parameter, parameter, true, true, false),
            "parameter-object" => (parameter, compilation.GetSpecialType(SpecialType.System_Object), true, true, false),
            _ => throw new ArgumentOutOfRangeException(nameof(relation)),
        };

        // This query follows named-type BaseType links, not interface or constraint assignability.
        Assert.That(RoslynSymbolFacts.IsOrDerivesFrom(type, possibleBase, includeSelf, compareDefinitions), Is.EqualTo(expected));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("SymbolFacts" + language + relation, isCollectible: true);
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var target = assembly.GetType("Probe", throwOnError: true)!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, null), Is.EqualTo(true));
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static INamedTypeSymbol RequiredType(Compilation compilation, string metadataName)
    {
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.That(type, Is.Not.Null, metadataName);
        return type!;
    }
}
