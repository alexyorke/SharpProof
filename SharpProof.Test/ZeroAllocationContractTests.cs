using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class ZeroAllocationContractTests
{
    private static IEnumerable<TestCaseData> AllocationScenarios()
    {
        yield return Case("ZeroAllocations_ObjectCreation_ReportsSiteDiagnostic", "public object TestMethod()",
            "return {|SP0013:new object()|};");
        yield return Case("ZeroAllocations_PotentialReferenceTypeParameterCreation_ReportsSiteDiagnostic",
            "public T Create<T>() where T : new()", "return {|SP0013:new T()|};");
        yield return Case("ZeroAllocations_ValueTypeParameterCreation_DoesNotReport",
            "public T Create<T>() where T : struct", "return new T();");
        yield return Case("ZeroAllocations_ArrayCreation_ReportsSiteDiagnostic", "public int[] TestMethod()",
            "return {|SP0013:new[] { 1, 2, 3 }|};");
        yield return Case("ZeroAllocations_AnonymousObjectCreation_ReportsSiteDiagnostic", "public object TestMethod()",
            "return {|SP0013:new { Value = 1 }|};");
        yield return Case("ZeroAllocations_CollectionExpression_ReportsSiteDiagnostic", "public int[] TestMethod()",
            "int[] values = {|SP0013:[1, 2, 3]|};\n        return values;");
        yield return Case("ZeroAllocations_DelegateCreation_ReportsSiteDiagnostic", "public Func<int> TestMethod()",
            "return {|SP0013:() => 1|};", "using System;");
        yield return Case("ZeroAllocations_BoxingConversion_ReportsSiteDiagnostic", "public object TestMethod()",
            "return {|SP0013:(object)1|};");
        yield return Case("ZeroAllocations_WithExpressionOnRecordClass_ReportsSiteDiagnostic",
            "public Box TestMethod(Box input)", "return {|SP0013:input with { Value = 5 }|};",
            declarations: "public record Box(int Value);");
        yield return Case("ZeroAllocations_StackAlloc_DoesNotReportDiagnostic", "public int TestMethod()",
            "Span<int> values = stackalloc int[4];\n        return values.Length;", "using System;");
        yield return Case("ZeroAllocations_ValueTypeConstruction_DoesNotReportDiagnostic", "public int TestMethod()",
            "var point = new Point(5);\n        return point.Value;", declarations: """
public readonly struct Point
{
    [Impure]
    public Point(int value) => Value = value;

    [Impure]
    public int Value { get; }
}
""");
        yield return Case("ZeroAllocations_ParamsArrayLowering_ReportsImplicitArrayAllocation",
            "public int TestMethod()", "return {|SP0013:Count(1, 2, 3)|};\n    }\n\n    [Impure]\n    private static int Count(params int[] values)\n    {\n        return values.Length;");
        yield return Case("ZeroAllocations_NestedLambdaBodyAllocation_DoesNotReportInnerAllocationDiagnostic",
            "public Func<object> TestMethod()", "return {|SP0013:() => new object()|};", "using System;");
        yield return Case("ZeroAllocations_MultipleAllocationSites_ReportEachSite", "public object TestMethod()",
            "var first = {|SP0013:new object()|};\n        var second = {|SP0013:new object()|};\n        return second ?? first;");
        yield return Case("ZeroAllocations_CollectionExpressionToSpan_DoesNotReportDiagnostic",
            "public int TestMethod()", "Span<int> values = [1, 2, 3];\n        return values.Length;", "using System;");
    }

    private static TestCaseData Case(
        string name,
        string signature,
        string body,
        string imports = "",
        string declarations = "")
    {
        return new TestCaseData(imports, declarations, signature, body).SetName(name);
    }

    [Test]
    public async Task ZeroAllocationsAttributeOnAccessor_NoPlacementDiagnostic()
    {
        const string test = """
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        [Impure]
        [ZeroAllocations]
        get => 42;
    }
}
""";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ZeroAllocationsAttributeOnProperty_AliasesGetterWithoutPlacementDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(CreateExpressionBodiedPropertyContractSource("ZeroAllocations"));
    }

    [TestCaseSource(nameof(AllocationScenarios))]
    public async Task ZeroAllocations_Scenario(
        string imports,
        string declarations,
        string signature,
        string body)
    {
        var test = $@"
{imports}
using SharpProof.Attributes;

{declarations}
public sealed class TestClass
{{
    [Impure]
    [ZeroAllocations]
    {signature}
    {{
        {body}
    }}
}}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ZeroAllocations_AndPurityContract_ReportIndependently()
    {
        const string test = """
using System.Diagnostics;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [ZeroAllocations]
    public ActivitySource {|SP0002:TestMethod|}()
    {
        return {|SP0013:new ActivitySource("test", "1.0.0")|};
    }
}
""";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ZeroAllocations_DiagnosticIncludesStructuredProperties()
    {
        var diagnostics = await GetDiagnosticsAsync("""
using SharpProof.Attributes;

public sealed class TestClass
{
    [ZeroAllocations]
    public object TestMethod()
    {
        return new object();
    }
}
""");

        var diagnostic = SingleDiagnostic(diagnostics, "SP0013");
        Assert.That(diagnostic.Properties["sharpproof.allocation.kind"], Is.EqualTo("object_creation"));
        Assert.That(diagnostic.Properties["sharpproof.allocation.operation_kind"],
            Is.EqualTo("ObjectCreation"));
        Assert.That(diagnostic.Properties["sharpproof.allocation.symbol"], Does.Contain("Object"));
    }
}
