using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryCompilerClrTypeMatcherTests
{
    private const string Source = """
        namespace OrdinaryTypes;
        public struct Value { public int Number; }
        public enum Kind { First, Second }
        public class Outer<T> { public class Inner<U> { } }
        public static class Shapes {
            public static int Scalar(int unused, int value) => value;
            public static int Text(string unused, int value) => value;
            public static int Object(object unused, int value) => value;
            public static int Vector(int[] unused, int value) => value;
            public static int Matrix(int[,] unused, int value) => value;
            public static int Jagged(int[][] unused, int value) => value;
            public static int Generic<T>(T unused, int value) => value;
            public static int GenericArray<T>(T[] unused, int value) => value;
            public static int Nullable<T>(T? unused, int value) where T : struct => value;
            public static int Nested(Outer<int>.Inner<string> unused, int value) => value;
            public static int Tuple((int Left, int Right) unused, int value) => value;
            public static int Nominal(Value unused, int value) => value;
            public static int Enumeration(Kind unused, int value) => value;
            public static int Dynamic(dynamic unused, int value) => value;
        }
        """;

    [TestCase("Scalar", false, true)]
    [TestCase("Text", false, true)]
    [TestCase("Object", false, true, TestName = "ActualObjectClrProjectionMatches")]
    [TestCase("Vector", false, true)]
    [TestCase("Matrix", false, true)]
    [TestCase("Jagged", false, true)]
    [TestCase("Generic", false, true)]
    [TestCase("Generic", true, true)]
    [TestCase("GenericArray", false, true)]
    [TestCase("GenericArray", true, true)]
    [TestCase("Nullable", false, true)]
    [TestCase("Nested", false, true)]
    [TestCase("Tuple", false, true)]
    [TestCase("Nominal", false, true)]
    [TestCase("Enumeration", false, true)]
    [TestCase("Dynamic", false, false, TestName = "ActualDynamicClrProjectionRemainsConservative")]
    public void ActualSameEmissionParameterTypesKeepTheExistingMatchingRules(
        string methodName, bool stringChoice, bool expectedMatch)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        var compilation = AnalyzerGateHost.CreateCompilation(Source, "OrdinaryCompilerClrTypes");
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var owner = compilation.GetTypeByMetadataName("OrdinaryTypes.Shapes")!;
        var symbol = owner.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        var sourceType = symbol.Parameters[0].Type;
        var choice = stringChoice ? typeof(string) : typeof(int);

        using var image = NativeSourceEmission.EmitImage(compilation, "Compiler CLR type source did not compile: ", CancellationToken.None);
        var context = new AssemblyLoadContext("ordinary-compiler-clr-types-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType("OrdinaryTypes.Shapes", throwOnError: true)!.GetMethod(methodName)!;
            if (method.IsGenericMethodDefinition)
            {
                method = method.MakeGenericMethod(choice);
            }
            var runtimeType = method.GetParameters()[0].ParameterType;
            var unused = runtimeType.IsValueType ? Activator.CreateInstance(runtimeType) : null;
            Assert.That(method.Invoke(null, [unused, 37]), Is.EqualTo(37));
            Assert.That(method.Name, Is.EqualTo(symbol.MetadataName));
            Assert.That(method.GetParameters(), Has.Length.EqualTo(symbol.Parameters.Length));
            Assert.That(CompilerClrTypeMatcher.Matches(sourceType, runtimeType, choice), Is.EqualTo(expectedMatch));
            Assert.That(CompilerClrTypeMatcher.MetadataName(owner), Is.EqualTo(method.DeclaringType!.FullName));
            if (methodName == "Dynamic")
            {
                Assert.That(sourceType.TypeKind, Is.EqualTo(TypeKind.Dynamic));
                Assert.That(runtimeType, Is.EqualTo(typeof(object)));
                Assert.That(method.GetParameters()[0].GetCustomAttributesData().Select(static attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeType.FullName)),
                    Does.Contain("System.Runtime.CompilerServices.DynamicAttribute"));
            }
            TestContext.Out.WriteLine("actual-compiler-clr: " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))) +
                " " + symbol.GetDocumentationCommentId() + " source=" + sourceType.ToDisplayString() +
                " runtime=" + runtimeType + " choice=" + choice + " matches=" + expectedMatch);
        }
        finally
        {
            context.Unload();
        }
    }
}
