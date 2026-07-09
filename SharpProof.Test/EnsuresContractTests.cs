using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class EnsuresContractTests
    {
        [Test]
        public async Task Ensures_StraightLineReturn_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result == 1"")]
    public int Value()
    {
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_BranchRefinedReturn_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Normalize(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_FailingReturn_ReportsSp0018()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Identity()
    {
        return {|SP0018:0|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_OneOfMultipleReturnSitesFails_ReportsOnlyFailingSite()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result >= 0"")]
    public int Normalize(bool useValue)
    {
        if (useValue)
        {
            return {|SP0018:-1|};
        }

        return 0;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_MultipleAttributes_CanAllBeProven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    [Ensures(""result < 10"")]
    public int Value()
    {
        return 5;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ResultCanReferenceParameter_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result == value"")]
    public int Identity(int value)
    {
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ParameterOnlyConditionCanUseRequiresAssumption_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""value > 0"")]
    [Ensures(""value > 0"")]
    public int Identity(int value)
    {
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ResultComparedWithParameter_FailingReturnReportsSp0018()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > value"")]
    public int Identity(int value)
    {
        return {|SP0018:value|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_OutParameterAssignedValue_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value != null"")]
    public bool TryGet(out string? value)
    {
        value = string.Empty;
        return true;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_OutParameterAssignedNullFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value != null"")]
    public bool TryGet(out string? value)
    {
        value = null;
        return {|SP0018:false|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_RefParameterAssignedValue_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value != null"")]
    public void Normalize(ref string? value)
    {
        value = string.Empty;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_RefParameterAssignedNullFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value != null"")]
    public void Normalize(ref string? value)
    {
        value = null;
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_NullableOutParameterHasValue_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value.HasValue"")]
    public void Assign(out int? value)
    {
        value = 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_NullableOutParameterValuePredicate_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value.Value > 0"")]
    public void Assign(out int? value)
    {
        value = 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_NullableOutParameterNoValueFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""value.HasValue"")]
    public void Assign(out int? value)
    {
        value = null;
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ArrayLengthPredicate_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""values.Length == 3"")]
    public void Fill(out int[] values)
    {
        values = new[] { 1, 2, 3 };
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ArrayLengthPredicateFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""values.Length > 0"")]
    public void Fill(out int[] values)
    {
        values = new int[0];
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_CollectionCountPredicate_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""values.Count == 2"")]
    public void Fill(out List<int> values)
    {
        values = new List<int> { 1, 2 };
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_CollectionCountPredicateFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""values.Count > 0"")]
    public void Fill(out List<int> values)
    {
        values = new List<int>();
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ExplicitThisMemberState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; private set; }

    [Ensures(""this.Value != null"")]
    public int Initialize()
    {
        this.Value = string.Empty;
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ImplicitThisFieldState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    private string? _value;

    [Ensures(""_value != null"")]
    public int Initialize()
    {
        _value = string.Empty;
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ThisMemberStateFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; private set; }

    [Ensures(""this.Value != null"")]
    public int Read()
    {
        this.Value = null;
        return {|SP0018:1|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_LocalVariableReference_IsRejected()
        {
            var test = @"
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
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_EmptyCondition_ReportsInvalidContractArgument()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0024:Ensures("""")|}]
    public int Value()
    {
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_VoidMethodMemberState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    private string? _value;

    [Ensures(""_value != null"")]
    public void Run()
    {
        _value = string.Empty;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_VoidMethodExplicitReturnFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    private string? _value;

    [Ensures(""_value != null"")]
    public void Run()
    {
        _value = null;
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ConstructorMemberState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; }

    [Ensures(""this.Value != null"")]
    public TestClass()
    {
        Value = string.Empty;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ConstructorExplicitReturnFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; }

    [Ensures(""this.Value != null"")]
    public TestClass()
    {
        Value = null;
        {|SP0018:return;|}
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ExpressionBodiedVoidMethodMemberState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    private string? _value;

    [Ensures(""_value != null"")]
    public void Run() => _value = string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ExpressionBodiedConstructorMemberState_Proven()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; }

    [Ensures(""this.Value != null"")]
    public TestClass() => Value = string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ExpressionBodiedVoidMethodFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    private string? _value;

    [Ensures(""_value != null"")]
    public void Run() => {|SP0018:_value = null|};
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_ExpressionBodiedConstructorFailure_ReportsSp0018()
        {
            var test = @"
#nullable enable
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    public string? Value { get; }

    [Ensures(""this.Value != null"")]
    public TestClass() => {|SP0018:Value = null|};
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_VoidMethodResultReference_IsRejected()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result == null"")]
    public void Run()
    {
    }
}");

            Assert.That(SingleDiagnostic(diagnostics, SharpProofDiagnostics.EnsuresUnsupportedId).GetMessage(), Does.Contain("result is not available"));
        }

        [Test]
        public async Task Ensures_MisplacedOnProperty_ReportsSp0020()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0020:Ensures(""true"")|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_SmtOff_RemainsConservative()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Normalize(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 1;
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "off"));

            var ensuresDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.EnsuresUnsupportedId)
                .ToArray();
            Assert.That(ensuresDiagnostics, Has.Length.EqualTo(2));
            Assert.That(ensuresDiagnostics.All(diagnostic => diagnostic.GetMessage().Contains("SMT")), Is.True);
        }

        [Test]
        public async Task Ensures_UnreachableReturnSite_DoesNotReport()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Value()
    {
        return 1;
        return -1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
