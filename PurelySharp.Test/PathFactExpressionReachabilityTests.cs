using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class PathFactExpressionReachabilityTests
    {
        [Test]
        public async Task ConditionalExpression_ImpossibleArmWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > 0 ? value : Impure();
    }

    [Impure]
    private static int Impure() => 1;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalAnd_ImpossibleRightOperandWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int value)
    {
        if (value <= 0)
        {
            return false;
        }

        return value <= 0 && Impure();
    }

    [Impure]
    private static bool Impure() => true;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalOr_ImpossibleRightOperandWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int value)
    {
        if (value <= 0)
        {
            return true;
        }

        return value > 0 || Impure();
    }

    [Impure]
    private static bool Impure() => true;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Coalesce_ImpossibleWhenNullWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value ?? Impure();
    }

    [Impure]
    private static string Impure() => string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Coalesce_WhenNullBranchReceivesNullFact_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return value ?? (value is null ? string.Empty : Impure());
    }

    [Impure]
    private static string Impure() => string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalAccess_ImpossibleWhenNotNullWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Worker worker)
    {
        if (worker != null)
        {
            return string.Empty;
        }

        return worker?.Impure() ?? string.Empty;
    }
}

public sealed class Worker
{
    [Impure]
    public string Impure() => string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalAccess_WhenNotNullBranchReceivesNonNullFact_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Worker worker)
    {
        return worker?.Echo(worker == null ? Impure() : string.Empty) ?? string.Empty;
    }

    [Impure]
    private static string Impure() => string.Empty;
}

public sealed class Worker
{
    [Pure]
    public string Echo(string value) => value;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReassignedLocal_ImpossibleConditionalArmWithImpureFieldRead_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    private static int s_state;

    [EnforcePure]
    public int TestMethod(int value)
    {
        value = 1;
        return value == 0 ? s_state : 0;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReassignedLocal_ImpossibleShortCircuitOperandWithImpureFieldRead_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    private static int s_state;

    [EnforcePure]
    public bool TestMethod(int value)
    {
        value = 0;
        return value != 0 && s_state == 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReassignedLocal_ReachableConditionalArmWithImpureFieldRead_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    private static int s_state;

    [EnforcePure]
    public int TestMethod(int value)
    {
        value = 0;
        return value == 0 ? s_state : 0;
    }
}";

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(test);

            Assert.That(
                diagnostics.Any(diagnostic => diagnostic.Id == PurelySharp.Analyzer.PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True);
        }

        [Test]
        public async Task DoesNotReturnIfGuard_ImpossibleConditionalArmWithImpureCall_NoDiagnostic()
        {
            var test = @"
using System.Diagnostics.CodeAnalysis;
using PurelySharp.Attributes;

public static class Guard
{
    [Pure]
    public static void ThrowIf([DoesNotReturnIf(true)] bool condition)
    {
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        Guard.ThrowIf(value <= 0);

        return value > 0 ? value : Impure();
    }

    [Impure]
    private static int Impure() => 1;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpossibleBranchWithImpureForeachEnumeratorRuntime_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value, Sequence values)
    {
        if (value <= 0)
        {
            return;
        }

        if (value <= 0)
        {
            foreach (var item in values)
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task UnknownBranchWithImpureForeachEnumeratorRuntime_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(int value, Sequence values)
    {
        if (value <= 0)
        {
            foreach (var item in values)
            {
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

    }
}
