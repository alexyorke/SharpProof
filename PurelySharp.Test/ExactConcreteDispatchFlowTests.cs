using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ExactConcreteDispatchFlowTests
    {
        [Test]
        public async Task InterfaceMethodDispatch_AliasedExactConcreteLocal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public interface IWorker
{
    [EnforcePure]
    int Compute(int value);
}

public class ExactWorker : IWorker
{
    [EnforcePure]
    public virtual int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|PS0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        IWorker worker = new ExactWorker();
        IWorker alias = worker;
        return alias.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualMethodDispatch_CastExactConcreteLocal_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|PS0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        Worker worker = (Worker)new ExactWorker();
        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualMethodDispatch_SameConcreteConditionalMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|PS0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(bool chooseLeft, int value)
    {
        Worker worker = chooseLeft ? new ExactWorker() : new ExactWorker();
        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualPropertyDispatch_SameConcreteConditionalMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|PS0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue(bool chooseLeft)
    {
        BaseValue value = chooseLeft ? new ExactValue() : new ExactValue();
        return value.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualMethodDispatch_SameConcreteIfElseMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|PS0002:Compute|}(int value)
    {
        System.Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(bool chooseLeft, int value)
    {
        Worker worker;
        if (chooseLeft)
        {
            worker = new ExactWorker();
        }
        else
        {
            worker = new ExactWorker();
        }

        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualPropertyDispatch_SameConcreteIfElseMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|PS0002:Value|}
    {
        [EnforcePure]
        get
        {
            System.Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue(bool chooseLeft)
    {
        BaseValue value;
        if (chooseLeft)
        {
            value = new ExactValue();
        }
        else
        {
            value = new ExactValue();
        }

        return value.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualMethodDispatch_SameConcreteCoalesceMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|PS0002:Compute|}(int value)
    {
        System.Console.WriteLine(value);
        return value + 2;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Process(int value)
    {
        ExactWorker primary = new ExactWorker();
        ExactWorker fallback = new ExactWorker();
        Worker worker = primary ?? fallback;
        return worker.Compute(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VirtualPropertyDispatch_SameConcreteCoalesceMerge_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|PS0002:Value|}
    {
        [EnforcePure]
        get
        {
            System.Console.WriteLine(1);
            return 2;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue()
    {
        ExactValue primary = new ExactValue();
        ExactValue fallback = new ExactValue();
        BaseValue value = primary ?? fallback;
        return value.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

    }
}
