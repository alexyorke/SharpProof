using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ZeroAllocationContractTests
    {
        [Test]
        public async Task ZeroAllocationsAttributeOnAccessor_NoPlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    public int Value
    {
        [Impure]
        [ZeroAllocations]
        get => 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocationsAttributeOnProperty_PlacementDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0014:ZeroAllocations|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_ObjectCreation_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public object TestMethod()
    {
        return {|SP0013:new object()|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_ArrayCreation_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int[] TestMethod()
    {
        return {|SP0013:new[] { 1, 2, 3 }|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_AnonymousObjectCreation_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public object TestMethod()
    {
        return {|SP0013:new { Value = 1 }|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_CollectionExpression_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int[] TestMethod()
    {
        int[] values = {|SP0013:[1, 2, 3]|};
        return values;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_DelegateCreation_ReportsSiteDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public Func<int> TestMethod()
    {
        return {|SP0013:() => 1|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_BoxingConversion_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public object TestMethod()
    {
        return {|SP0013:(object)1|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_WithExpressionOnRecordClass_ReportsSiteDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public record Box(int Value);

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public Box TestMethod(Box input)
    {
        return {|SP0013:input with { Value = 5 }|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_StackAlloc_DoesNotReportDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int TestMethod()
    {
        Span<int> values = stackalloc int[4];
        return values.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_ValueTypeConstruction_DoesNotReportDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public readonly struct Point
{
    [Impure]
    public Point(int value)
    {
        Value = value;
    }

    [Impure]
    public int Value { get; }
}

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int TestMethod()
    {
        var point = new Point(5);
        return point.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_ParamsArrayLowering_DoesNotReportDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int TestMethod()
    {
        return Count(1, 2, 3);
    }

    [Impure]
    private static int Count(params int[] values)
    {
        return values.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_NestedLambdaBodyAllocation_DoesNotReportInnerAllocationDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public Func<object> TestMethod()
    {
        return {|SP0013:() => new object()|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_MultipleAllocationSites_ReportEachSite()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public object TestMethod()
    {
        var first = {|SP0013:new object()|};
        var second = {|SP0013:new object()|};
        return second ?? first;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_AndPurityContract_ReportIndependently()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [ZeroAllocations]
    public ActivitySource {|SP0002:TestMethod|}()
    {
        return {|SP0013:new ActivitySource(""test"", ""1.0.0"")|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ZeroAllocations_DiagnosticIncludesStructuredProperties()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
using SharpProof.Attributes;

public sealed class TestClass
{
    [ZeroAllocations]
    public object TestMethod()
    {
        return new object();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.AllocationInZeroAllocationMethodId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AllocationKindProperty], Is.EqualTo("object_creation"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AllocationOperationKindProperty], Is.EqualTo("ObjectCreation"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AllocationSymbolProperty], Does.Contain("Object"));
        }

        [Test]
        public async Task ZeroAllocations_CollectionExpressionToSpan_DoesNotReportDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Impure]
    [ZeroAllocations]
    public int TestMethod()
    {
        Span<int> values = [1, 2, 3];
        return values.Length;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
