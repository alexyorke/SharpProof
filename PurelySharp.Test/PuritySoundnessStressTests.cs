using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class PuritySoundnessStressTests
    {
        [TestCaseSource(nameof(ImpureCases))]
        public async Task EnforcePure_ImpureStressCases_ReportPs0002(string name, string source, bool allowUnsafe)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, allowUnsafe);

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                name);
        }

        [TestCaseSource(nameof(PureCases))]
        public async Task EnforcePure_PureStressCases_DoNotReportPs0002(string name, string source)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, allowUnsafe: false);

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                name);
        }

        private static TestCaseData[] ImpureCases()
        {
            return new[]
            {
                Impure("RefParameterAssignment", @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ref int value)
    {
        value = 1;
    }
}"),
                Impure("OutParameterAssignment", @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(out int value)
    {
        value = 1;
    }
}"),
                Impure("ArrayParameterElementWrite", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringBuilder builder)
    {
        builder.Append(""x"");
    }
}"),
                Impure("StaticFieldWrite", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Random.Shared.Next();
    }
}"),
                Impure("DynamicDispatch", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Action action)
    {
        action();
    }
}"),
                Impure("LockWithoutAllowSynchronization", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public unsafe class TestClass
{
    [EnforcePure]
    public void TestMethod(int* value)
    {
        *value = 1;
    }
}", allowUnsafe: true),
                Impure("CallerVisibleRefReturnWrite", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return string.IsNullOrEmpty(value);
    }
}"),
                Pure("LocalTuple", @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
}
