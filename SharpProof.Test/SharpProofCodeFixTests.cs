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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(6, 27, 6, 30)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.ConflictingPurityAttributesId)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
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
        var expectedImpure = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(15, 37, 15, 40)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(6, 16, 6, 21)
            .WithArguments("get_Value");

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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MisplacedAttributeId)
            .WithSpan(4, 2, 4, 13);
        await VerifyCF.VerifyCodeFixAsync(source, expected, fixedSource);
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.MisplacedAttributeId)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId)
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
        var expected = VerifyCF.Diagnostic(SharpProofDiagnostics.RedundantAllowSynchronizationId)
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

    [Test]
    public async Task SP0014_RemovesMisplacedZeroAllocationsAttribute()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0014:ZeroAllocations|}]
    public int Value = 42;
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value = 42;
}
";
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
    public async Task SP0017_RemovesMisplacedAllowedCapabilitiesAttribute()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0017:AllowedCapabilities(SharpProofCapability.None)|}]
    public int Value = 42;
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value = 42;
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
    public async Task SP0020_RemovesMisplacedEnsuresAttribute()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0020:Ensures(""true"")|}]
    public int Value = 42;
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value = 42;
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
    public async Task SP0023_RemovesMisplacedExpectedComplexityAttribute()
    {
        var source = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0023:ExpectedComplexity(ComplexityKind.Constant)|}]
    public int Value = 42;
}
";
        var fixedSource = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value = 42;
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
    public async Task SP0034_AddsInferredZeroAllocationsAttribute()
    {
        const string source = """
                              public static class C
                              {
                                  public static int {|SP0034:Identity|}(int value) => value;
                              }
                              """;
        const string fixedSource = """
                                   public static class C
                                   {
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
    public async Task SP0037_AddsInferredDoesNotThrowAttribute()
    {
        const string source = """
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static int {|SP0037:Identity|}(int value) => value;
                              }
                              """;
        const string fixedSource = """
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [DoesNotThrow]
                                       public static int Identity(int value) => value;
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "exceptions");
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
    public async Task SP0038_AddsInferredEnsuresAttribute()
    {
        const string source = """
                              using SharpProof.Attributes;

                              public static class C
                              {
                                  public static int {|SP0038:Identity|}(int value) => value;
                              }
                              """;
        const string fixedSource = """
                                   using SharpProof.Attributes;

                                   public static class C
                                   {
                                       [Ensures("result == value")]
                                       public static int Identity(int value) => value;
                                   }
                                   """;

        await VerifyInferredContractCodeFixAsync(source, fixedSource, "ensures");
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
