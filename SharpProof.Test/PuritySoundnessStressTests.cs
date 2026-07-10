using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class PuritySoundnessStressTests
{
    [TestCaseSource(nameof(ImpureCases))]
    public async Task EnforcePure_ImpureStressCases_ReportSp0002(string name, string source, bool allowUnsafe)
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, allowUnsafe);

        Assert.That(
            diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.True,
            name);
    }

    [TestCaseSource(nameof(PureCases))]
    public async Task EnforcePure_PureStressCases_DoNotReportSp0002(string name, string source)
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, false);

        Assert.That(
            diagnostics.Any(d => d.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False,
            name);
    }

    private static TestCaseData[] ImpureCases()
    {
        return new[]
        {
            Impure("RefParameterAssignment", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ref int value)
    {
        value = 1;
    }
}"),
            Impure("OutParameterAssignment", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(out int value)
    {
        value = 1;
    }
}"),
            Impure("ArrayParameterElementWrite", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        values[0] = 1;
    }
}"),
            Impure("SpanParameterElementWrite", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Span<int> values)
    {
        values[0] = 1;
    }
}"),
            Impure("ListAdd", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> values)
    {
        values.Add(1);
    }
}"),
            Impure("DictionaryIndexerSet", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> values)
    {
        values[""a""] = 1;
    }
}"),
            Impure("StringBuilderAppend", @"
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringBuilder builder)
    {
        builder.Append(""x"");
    }
}"),
            Impure("StaticFieldWrite", @"
using SharpProof.Attributes;

public class TestClass
{
    private static int s_value;

    [EnforcePure]
    public void TestMethod()
    {
        s_value = 1;
    }
}"),
            Impure("InstanceFieldWrite", @"
using SharpProof.Attributes;

public class TestClass
{
    private int _value;

    [EnforcePure]
    public void TestMethod()
    {
        _value = 1;
    }
}"),
            Impure("StaticPropertyEnvironmentRead", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.TickCount;
    }
}"),
            Impure("DateTimeNow", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTime TestMethod()
    {
        return DateTime.Now;
    }
}"),
            Impure("GuidNewGuid", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid TestMethod()
    {
        return Guid.NewGuid();
    }
}"),
            Impure("RandomSharedNext", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Random.Shared.Next();
    }
}"),
            Impure("DynamicDispatch", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(dynamic value)
    {
        return value.ToString();
    }
}"),
            Impure("DelegateInvocation", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Action action)
    {
        action();
    }
}"),
            Impure("LockWithoutAllowSynchronization", @"
using SharpProof.Attributes;

public class TestClass
{
    private readonly object _gate = new object();

    [EnforcePure]
    public int TestMethod()
    {
        lock (_gate)
        {
            return 1;
        }
    }
}"),
            Impure("PointerWrite", @"
using SharpProof.Attributes;

public unsafe class TestClass
{
    [EnforcePure]
    public void TestMethod(int* value)
    {
        *value = 1;
    }
}", true),
            Impure("CallerVisibleRefReturnWrite", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ref int value)
    {
        ref int alias = ref value;
        alias++;
    }
}")
        };
    }

    private static TestCaseData[] PureCases()
    {
        return new[]
        {
            Pure("ArithmeticOnly", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var doubled = value * 2;
        return doubled + 1;
    }
}"),
            Pure("MathAbs", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Math.Abs(value);
    }
}"),
            Pure("StringIsNullOrEmpty", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return string.IsNullOrEmpty(value);
    }
}"),
            Pure("LocalTuple", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var pair = (value, value + 1);
        return pair.Item1 + pair.Item2;
    }
}"),
            Pure("PureSourceCallee", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Callee(value);
    }

    [EnforcePure]
    private int Callee(int value)
    {
        return value + 1;
    }
}")
        };
    }

    private static TestCaseData Impure(string name, string source, bool allowUnsafe = false)
    {
        return new TestCaseData(name, source, allowUnsafe).SetName("Impure_" + name);
    }

    private static TestCaseData Pure(string name, string source)
    {
        return new TestCaseData(name, source).SetName("Pure_" + name);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, bool allowUnsafe)
    {
        return await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            allowUnsafe: allowUnsafe,
            compilationName: "PuritySoundnessStressTests");
    }
}