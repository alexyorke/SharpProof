using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCF = SharpProof.Test.CSharpCodeFixVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer,
    SharpProof.SharpProofCodeFixProvider>;

namespace SharpProof.Test;

[TestFixture]
public sealed class SharpProofCodeFixTests
{
    [Test]
    public async Task SP0004_AddEnforcePure_InsertsFullyQualifiedAttribute()
    {
        var source = @"
namespace N
{
    public static class C
    {
        public static int Add(int a, int b) => a + b;
    }
}
";
        var fixedSource = @"
namespace N
{
    public static class C
    {
        [global::SharpProof.Attributes.EnforcePure]
        public static int Add(int a, int b) => a + b;
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0004")
            .WithSpan(6, 27, 6, 30)
            .WithArguments("Add");
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0004_AddEnforcePure_PreservesDocumentationAndIndentation()
    {
        const string source = """
                              public static class C
                              {
                                  /// <summary>Adds one.</summary>
                                  public static int Add(int value) => value + 1;
                              }
                              """;
        const string fixedSource = """
                                   public static class C
                                   {
                                       /// <summary>Adds one.</summary>
                                       [global::SharpProof.Attributes.EnforcePure]
                                       public static int Add(int value) => value + 1;
                                   }
                                   """;
        var expected = VerifyCF.Diagnostic("SP0004")
            .WithSpan(4, 23, 4, 26)
            .WithArguments("Add");

        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0004_AddEnforcePure_AliasOnlyImportKeepsFullyQualifiedAttribute()
    {
        var source = @"
using SP = SharpProof.Attributes;

public static class C
{
    public static int Add(int a, int b) => a + b;
}
";
        var fixedSource = @"
using SP = SharpProof.Attributes;

public static class C
{
    [global::SharpProof.Attributes.EnforcePure]
    public static int Add(int a, int b) => a + b;
}
";
        var expected = VerifyCF.Diagnostic("SP0004")
            .WithSpan(6, 23, 6, 26)
            .WithArguments("Add");

        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0004_AddEnforcePure_UsesNamespaceScopedImport()
    {
        const string source = """
                              namespace N
                              {
                                  using SharpProof.Attributes;

                                  public static class C
                                  {
                                      public static int Add(int value) => value + 1;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   namespace N
                                   {
                                       using SharpProof.Attributes;

                                       public static class C
                                       {
                                           [EnforcePure]
                                           public static int Add(int value) => value + 1;
                                       }
                                   }
                                   """;
        var expected = VerifyCF.Diagnostic("SP0004")
            .WithSpan(7, 27, 7, 30)
            .WithArguments("Add");

        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0004_AddEnforcePure_AmbiguousShortNameKeepsFullyQualifiedAttribute()
    {
        var source = @"
#pragma warning disable SP0026
using SharpProof.Attributes;

namespace N
{
    public sealed class EnforcePureAttribute : System.Attribute { }

    public static class C
    {
        public static int Add(int a, int b) => a + b;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0026
using SharpProof.Attributes;

namespace N
{
    public sealed class EnforcePureAttribute : System.Attribute { }

    public static class C
    {
        [global::SharpProof.Attributes.EnforcePure]
        public static int Add(int a, int b) => a + b;
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0004")
            .WithSpan(11, 27, 11, 30)
            .WithArguments("Add");

        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0005_RemovesPure_KeepsEnforcePure()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        [Pure]
        public static int Id(int x) => x;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        public static int Id(int x) => x;
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0005")
            .WithSpan(11, 27, 11, 29)
            .WithArguments("Id");
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0002_RemovesPurityAttributes()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        [EnforcePure]
        public static int Bad()
        {
            System.Console.Write(1);
            return 0;
        }
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

namespace N
{
    public static class C
    {
        public static int Bad()
        {
            System.Console.Write(1);
            return 0;
        }
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(10, 27, 10, 30)
            .WithArguments("Bad");
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0002_RemovesPurityAttributesFromConversionOperator()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }

    [EnforcePure]
    public static explicit operator int(Temperature value)
    {
        System.Console.WriteLine(value._celsius);
        return value._celsius;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Temperature
{
    private readonly int _celsius;

    public Temperature(int celsius)
    {
        _celsius = celsius;
    }
    public static explicit operator int(Temperature value)
    {
        System.Console.WriteLine(value._celsius);
        return value._celsius;
    }
}
";
        var expectedImpure = VerifyCF.Diagnostic("SP0002")
            .WithSpan(15, 19, 15, 27)
            .WithArguments("op_Explicit");
        await VerifyCF.VerifyCodeFixAsync(
            source,
            expectedImpure,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0002_RemovesPurityAttributesFromExpressionBodiedProperty()
    {
        var source = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;

    [Pure]
    public int Value => _counter++;
}
";
        var fixedSource = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;
    public int Value => _counter++;
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(9, 16, 9, 21)
            .WithArguments("get_Value");
        await VerifyCF.VerifyCodeFixAsync(
            source,
            expected,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0002_RemovesAccessorPurityAttribute()
    {
        var source = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        [Pure]
        get
        {
            System.Console.WriteLine();
            return 1;
        }
    }
}
";
        var fixedSource = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        get
        {
            System.Console.WriteLine();
            return 1;
        }
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(6, 16, 6, 21)
            .WithArguments("get_Value");

        await VerifyCF.VerifyNonLocalCodeFixAsync(
            source,
            expected,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0002_RemovesPurityAttributesFromDeclarationAndAccessorTogether()
    {
        var source = @"
#pragma warning disable SP0005
using SharpProof.Attributes;

public sealed class TestClass
{
    [Pure]
    public int Value
    {
        [EnforcePure]
        get
        {
            System.Console.WriteLine();
            return 1;
        }
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0005
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        get
        {
            System.Console.WriteLine();
            return 1;
        }
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(8, 16, 8, 21)
            .WithArguments("get_Value");

        await VerifyCF.VerifyNonLocalCodeFixAsync(
            source,
            expected,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0002_RemovesEventAccessorPurityAttribute()
    {
        var source = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    public event Action E
    {
        [Pure]
        add { Console.WriteLine(); }
        remove { }
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    public event Action E
    {
        add { Console.WriteLine(); }
        remove { }
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(11, 9, 11, 12)
            .WithArguments("add_E");

        await VerifyCF.VerifyNonLocalCodeFixAsync(
            source,
            expected,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0002_PreservesForeignLookalikeAttribute()
    {
        var source = @"
#pragma warning disable SP0026
namespace Other
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class EnforcePureAttribute : System.Attribute { }
}

public static class TestClass
{
    [Other.EnforcePure]
    [SharpProof.Attributes.EnforcePure]
    public static int Bad()
    {
        System.Console.WriteLine();
        return 1;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0026
namespace Other
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class EnforcePureAttribute : System.Attribute { }
}

public static class TestClass
{
    [Other.EnforcePure]
    public static int Bad()
    {
        System.Console.WriteLine();
        return 1;
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0002")
            .WithSpan(13, 23, 13, 26)
            .WithArguments("Bad");

        await VerifyCF.VerifyCodeFixAsync(
            source,
            expected,
            fixedSource,
            "RemoveAttributesMatchingAsyncSP0002");
    }

    [Test]
    public async Task SP0003_RemovesMisplacedEnforcePureOnClass()
    {
        var source = @"
using SharpProof.Attributes;

[EnforcePure]
public class C
{
}
";
        var fixedSource = @"
using SharpProof.Attributes;
public class C
{
}
";
        var expected = VerifyCF.Diagnostic("SP0003")
            .WithSpan(4, 2, 4, 13);
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0003_RemovalPreservesDocumentationTrivia()
    {
        const string source = """
                              using SharpProof.Attributes;

                              /// <summary>Keep this documentation.</summary>
                              [{|SP0003:EnforcePure|}]
                              public class C
                              {
                              }
                              """;
        const string fixedSource = """
                                   using SharpProof.Attributes;

                                   /// <summary>Keep this documentation.</summary>
                                   public class C
                                   {
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0003_RemovesMisplacedEnforcePureOnEventField()
    {
        var source = @"
using System;
using SharpProof.Attributes;

public sealed class C
{
    [EnforcePure]
    public event Action E;
}
";
        var fixedSource = @"
using System;
using SharpProof.Attributes;

public sealed class C
{
    public event Action E;
}
";
        var expected = VerifyCF.Diagnostic("SP0003")
            .WithSpan(7, 6, 7, 17);
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0006_RemoveAllowSynchronization_LeavesImpureMethodWithoutExtraDiagnostics()
    {
        var source = @"
using SharpProof.Attributes;
using System;

namespace N
{
    public class C
    {
        [AllowSynchronization]
        public void M() { Console.Write(1); }
    }
}
";
        var fixedSource = @"
using SharpProof.Attributes;
using System;

namespace N
{
    public class C
    {
        public void M() { Console.Write(1); }
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0006")
            .WithSpan(10, 21, 10, 22)
            .WithArguments("M");
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource, "RemoveAttributesMatchingAsyncSP0006b");
    }

    [Test]
    public async Task SP0008_RemovesRedundantAllowSynchronization()
    {
        var source = @"
using SharpProof.Attributes;

namespace N
{
    public class C
    {
        [EnforcePure]
        [AllowSynchronization]
        public int M() => 1;
    }
}
";
        var fixedSource = @"
using SharpProof.Attributes;

namespace N
{
    public class C
    {
        [EnforcePure]
        public int M() => 1;
    }
}
";
        var expected = VerifyCF.Diagnostic("SP0008")
            .WithSpan(10, 20, 10, 21)
            .WithArguments("M");
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
    }

    [Test]
    public async Task SP0013_RemovesZeroAllocationsAttribute()
    {
        var source = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public object TestMethod()
    {
        return {|SP0013:new object()|};
    }
}
";
        var fixedSource = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    public object TestMethod()
    {
        return new object();
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [TestCase("SP0014", "ZeroAllocations")]
    [TestCase("SP0017", "AllowedCapabilities(SharpProofCapability.None)")]
    [TestCase("SP0020", "Ensures(\"true\")")]
    [TestCase("SP0023", "ExpectedComplexity(ComplexityKind.Constant)")]
    public async Task RemovesMisplacedContractAttributeFromField(
        string diagnosticId,
        string attributeText)
    {
        const string sourceTemplate = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|DIAGNOSTIC:ATTRIBUTE|}]
    public int Value = 42;
}
";
        const string fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value = 42;
}
";
        var source = sourceTemplate
            .Replace("DIAGNOSTIC", diagnosticId, StringComparison.Ordinal)
            .Replace("ATTRIBUTE", attributeText, StringComparison.Ordinal);
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0015_RemovesAllowedCapabilitiesAttributeForViolation()
    {
        var source = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        {|SP0015:Console.WriteLine(""hello"")|};
    }
}
";
        var fixedSource = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    public void TestMethod()
    {
        Console.WriteLine(""hello"");
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0016_RemovesAllowedCapabilitiesAttributeForUnknown()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod(dynamic value)
    {
        {|SP0016:value.ToString()|};
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public void TestMethod(dynamic value)
    {
        value.ToString();
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0018_RemovesEnsuresAttributeForUnprovenReturn()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Identity()
    {
        return {|SP0018:0|};
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Identity()
    {
        return 0;
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0019_RemovesEnsuresAttributeForUnsupportedCondition()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0019:Ensures(""local > 0"")|}]
    public int Value(int input)
    {
        var local = input + 1;
        return local;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value(int input)
    {
        var local = input + 1;
        return local;
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0021_RemovesExpectedComplexityAttributeForExceededBound()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int {|SP0021:Work|}(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0022_RemovesExpectedComplexityAttributeForUnknownBound()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    public static int Step(int value) => value + 1;

    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int {|SP0022:Work|}(int n)
    {
        var i = 0;
        while (i < n)
        {
            i = Step(i);
        }

        return i;
    }
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    public static int Step(int value) => value + 1;
    public static int Work(int n)
    {
        var i = 0;
        while (i < n)
        {
            i = Step(i);
        }

        return i;
    }
}
";
        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0029_MovesRequiresFromExpressionPropertyToGeneratedGetter()
    {
        const string source = """
                              #pragma warning disable SP0004
                              using SharpProof.Attributes;

                              public sealed class C
                              {
                                  [{|SP0029:Requires("true")|}]
                                  public int Value => 42; // preserved
                              }
                              """;
        const string fixedSource = """
                                   #pragma warning disable SP0004
                                   using SharpProof.Attributes;

                                   public sealed class C
                                   {
                                       public int Value
                                       {
                                           [Requires("true")]
                                           get => 42; // preserved
                                       }
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0029_MovesRequiresFromIndexerToExistingGetter()
    {
        const string source = """
                              #pragma warning disable SP0004
                              using SharpProof.Attributes;

                              public sealed class C
                              {
                                  [{|SP0029:Requires("index >= 0")|}]
                                  public int this[int index]
                                  {
                                      get => index;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   #pragma warning disable SP0004
                                   using SharpProof.Attributes;

                                   public sealed class C
                                   {
                                       public int this[int index]
                                       {
                                           [Requires("index >= 0")]
                                           get => index;
                                       }
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0029_ClearsGetTargetWhenMovingRequiresToGetter()
    {
        const string source = """
                              #pragma warning disable SP0004
                              using SharpProof.Attributes;

                              public sealed class C
                              {
                                  [get: {|SP0029:Requires("true")|}]
                                  public int Value
                                  {
                                      get => 42;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   #pragma warning disable SP0004
                                   using SharpProof.Attributes;

                                   public sealed class C
                                   {
                                       public int Value
                                       {
                                           [Requires("true")]
                                           get => 42;
                                       }
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0029_MovesRequiresWithoutDroppingComments()
    {
        const string source = """
                              #pragma warning disable SP0004
                              using SharpProof.Attributes;

                              public sealed class C
                              {
                                  // getter contract
                                  [{|SP0029:Requires("true")|}] // keep with contract
                                  public int Value
                                  {
                                      get => 42;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   #pragma warning disable SP0004
                                   using SharpProof.Attributes;

                                   public sealed class C
                                   {
                                       public int Value
                                       {
                                           // getter contract
                                           [Requires("true")] // keep with contract
                                           get => 42;
                                       }
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0034_PreservesMixedLineEndingsInsideStringLiterals()
    {
        var source =
            "public static class C\r\n" +
            "{\r\n" +
            "    public const string Verbatim = @\"first\nsecond\";\r\n" +
            "    public const string Raw = \"\"\"\r\n" +
            "        first\n" +
            "        second\r\n" +
            "        \"\"\";\r\n" +
            "    public static int {|SP0034:Identity|}(int value) => value;\r\n" +
            "}\r\n";
        var fixedSource =
            "public static class C\r\n" +
            "{\r\n" +
            "    public const string Verbatim = @\"first\nsecond\";\r\n" +
            "    public const string Raw = \"\"\"\r\n" +
            "        first\n" +
            "        second\r\n" +
            "        \"\"\";\r\n" +
            "    [global::SharpProof.Attributes.ZeroAllocations]\r\n" +
            "    public static int Identity(int value) => value;\r\n" +
            "}\r\n";

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "zero-allocations");
    }

    [TestCase(
        "SP0034",
        "",
        "[global::SharpProof.Attributes.ZeroAllocations]",
        "zero-allocations")]
    [TestCase(
        "SP0037",
        "using SharpProof.Attributes;\n\n",
        "[DoesNotThrow]",
        "exceptions")]
    [TestCase(
        "SP0038",
        "using SharpProof.Attributes;\n\n",
        "[Ensures(\"result == value\")]",
        "ensures")]
    public async Task AddsInferredContractToIdentityMethod(
        string diagnosticId,
        string imports,
        string attributeText,
        string inferenceCategory)
    {
        const string sourceTemplate = """
                                            IMPORTSpublic static class C
                                            {
                                                public static int {|DIAGNOSTIC:Identity|}(int value) => value;
                                            }
                                            """;
        const string fixedSourceTemplate = """
                                                 IMPORTSpublic static class C
                                                 {
                                                     ATTRIBUTE
                                                     public static int Identity(int value) => value;
                                                 }
                                                 """;
        var source = sourceTemplate
            .Replace("IMPORTS", imports, StringComparison.Ordinal)
            .Replace("DIAGNOSTIC", diagnosticId, StringComparison.Ordinal);
        var fixedSource = fixedSourceTemplate
            .Replace("IMPORTS", imports, StringComparison.Ordinal)
            .Replace("ATTRIBUTE", attributeText, StringComparison.Ordinal);

        await VerifyInferredContractCodeFixAsync(source, fixedSource, inferenceCategory);
    }

    [Test]
    public async Task SP0034_PreservesDocumentationBeforeInferredAttribute()
    {
        const string source = """
                              public static class C
                              {
                                  /// <summary>Returns the input.</summary>
                                  public static int {|SP0034:Identity|}(int value) => value;
                              }
                              """;
        const string fixedSource = """
                                   public static class C
                                   {
                                       /// <summary>Returns the input.</summary>
                                       [global::SharpProof.Attributes.ZeroAllocations]
                                       public static int Identity(int value) => value;
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "zero-allocations");
    }

    [Test]
    public async Task SP0035_AddsInferredCapabilitiesAttributeWithShortNames()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static void {|SP0035:Write|}() => Console.WriteLine(1);
                              }
                              """;
        const string fixedSource = """
                                   using System;
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [AllowedCapabilities(SharpProofCapability.IO | SharpProofCapability.Console)]
                                       public static void Write() => Console.WriteLine(1);
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "capabilities");
    }

    [Test]
    public async Task SP0036_AddsInferredComplexityAttribute()
    {
        const string source = """
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static int {|SP0036:Work|}(int n)
                                  {
                                      var sum = 0;
                                      for (var i = 0; i < n; i++)
                                      for (var j = 0; j < n; j++)
                                          sum += i + j;
                                      return sum;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [ExpectedComplexity(ComplexityKind.Quadratic)]
                                       public static int Work(int n)
                                       {
                                           var sum = 0;
                                           for (var i = 0; i < n; i++)
                                           for (var j = 0; j < n; j++)
                                               sum += i + j;
                                           return sum;
                                       }
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "complexity");
    }

    [Test]
    public async Task SP0037_AddsInferredAllowedExceptionsAttributeAtMediumConfidence()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static void {|SP0037:Fail|}()
                                  {
                                      throw new InvalidOperationException();
                                  }
                              }
                              """;
        const string fixedSource = """
                                   using System;
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [AllowedExceptions(typeof(global::System.InvalidOperationException))]
                                       public static void Fail()
                                       {
                                           throw new InvalidOperationException();
                                       }
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "exceptions", "medium");
    }

    [Test]
    public async Task SP0039_AddsInferredRequiresAttribute()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static int {|SP0039:Positive|}(int value)
                                  {
                                      if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                                      return value;
                                  }
                              }
                              """;
        const string fixedSource = """
                                   using System;
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [Requires("value > 0")]
                                       public static int Positive(int value)
                                       {
                                           if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                                           return value;
                                       }
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "requires");
    }

    [ReadmeExample("sp0045-unnecessary-null-forgiving")]
    [Test]
    public async Task SP0045_RemovesUnnecessaryNullForgivingOperator()
    {
        const string source = """
                              #nullable enable
                              #pragma warning disable SP0004
                              public static class C
                              {
                                  public static int Length(string value) => value{|SP0045:!|}.Length;
                              }
                              """;
        const string fixedSource = """
                                   #nullable enable
                                   #pragma warning disable SP0004
                                   public static class C
                                   {
                                       public static int Length(string value) => value.Length;
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [Test]
    public async Task SP0045_PreservesTriviaBeforeNullForgivingOperator()
    {
        const string source = """
                              #nullable enable
                              #pragma warning disable SP0004
                              public static class C
                              {
                                  public static int Length(string value) => value /* keep */ {|SP0045:!|}.Length;
                              }
                              """;
        const string fixedSource = """
                                   #nullable enable
                                   #pragma warning disable SP0004
                                   public static class C
                                   {
                                       public static int Length(string value) => value /* keep */ .Length;
                                   }
                                   """;

        await VerifyCF.VerifyCodeFixAsync(source, fixedSource);
    }

    [ReadmeExample("sp0046-suggest-nullable-contract")]
    [Test]
    public async Task SP0046_AddsInferredNullableReturnAttribute()
    {
        const string source = """
                              #nullable enable
                              public static class C
                              {
                                  public static string? {|SP0046:Name|}() => "name";
                              }
                              """;
        const string fixedSource = """
                                   #nullable enable
                                   public static class C
                                   {
                                       [return: global::System.Diagnostics.CodeAnalysis.NotNull]
                                       public static string? Name() => "name";
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "nullability");
    }

    [Test]
    public async Task SP0046_PreservesDocumentationAndLfForNullableReturnAttribute()
    {
        var source = "#nullable enable\n" +
                     "public static class C\n" +
                     "{\n" +
                     "    /// <summary>Returns a name.</summary>\n" +
                     "    public static string? {|SP0046:Name|}() => \"name\";\n" +
                     "}\n";
        var fixedSource = "#nullable enable\n" +
                          "public static class C\n" +
                          "{\n" +
                          "    /// <summary>Returns a name.</summary>\n" +
                          "    [return: global::System.Diagnostics.CodeAnalysis.NotNull]\n" +
                          "    public static string? Name() => \"name\";\n" +
                          "}\n";

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "nullability");
    }

    [Test]
    public async Task SP0046_AddsInferredNullableReturnAttributeToGetter()
    {
        const string source = """
                              #nullable enable
                              public sealed class C
                              {
                                  public string? Value
                                  {
                                      {|SP0046:get|} => "value";
                                  }
                              }
                              """;
        const string fixedSource = """
                                   #nullable enable
                                   public sealed class C
                                   {
                                       public string? Value
                                       {
                                           [return: global::System.Diagnostics.CodeAnalysis.NotNull]
                                           get => "value";
                                       }
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "nullability");
    }

    [Test]
    public async Task SP0046_AddsInferredNullableAttributeToSetterValue()
    {
        const string source = """
                              #nullable enable
                              public sealed class C
                              {
                                  private string? _value;

                                  public string? Value
                                  {
                                      get => _value;
                                      {|SP0046:set|}
                                      {
                                          if (value is null) throw new System.ArgumentNullException();
                                          _value = value;
                                      }
                                  }
                              }
                              """;
        const string fixedSource = """
                                   #nullable enable
                                   public sealed class C
                                   {
                                       private string? _value;

                                       public string? Value
                                       {
                                           get => _value;
                                           [param: global::System.Diagnostics.CodeAnalysis.NotNull]
                                           set
                                           {
                                               if (value is null) throw new System.ArgumentNullException();
                                               _value = value;
                                           }
                                       }
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "nullability");
    }

    private static async Task VerifyInferredContractCodeFixAsync(
        string source,
        string fixedSource,
        string kind,
        string minimumConfidence = "high")
    {
        var options = ImmutableDictionary<string, string>.Empty
            .Add("sharpproof_suggest_missing_enforce_pure", "false")
            .Add("sharpproof_suggest_inferred_contracts", "true")
            .Add("sharpproof_suggest_inferred_contracts_kinds", kind)
            .Add("sharpproof_suggest_inferred_contracts_minimum_confidence", minimumConfidence);
        await VerifyCF.VerifyCodeFixAsync(
            source,
            Microsoft.CodeAnalysis.Testing.DiagnosticResult.EmptyDiagnosticResults,
            fixedSource,
            options);
    }
}
