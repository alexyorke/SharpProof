using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class ExceptionFlowPropagationRegressionTests
{
    [Test]
    public async Task Sp0010_ConstantFalseThrow_DoesNotReport()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        if (false)
        {
            throw new InvalidOperationException();
        }
        }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_CatchWhenTrue_SuppressesException()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException) when (true)
        {
        }
        }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_PropertySetter_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class Box
{
    public int Value
    {
        set
        {
            throw new InvalidOperationException();
        }
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var box = new Box();
        box.Value = 1;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_InterpolatedStringHandlerConstructor_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;
using System.Runtime.CompilerServices;

[InterpolatedStringHandler]
public struct ThrowingHandler
{
    public ThrowingHandler(int literalLength, int formattedCount) =>
        throw new InvalidOperationException();

    public void AppendLiteral(string value)
    {
    }
}

public class TestClass
{
    private static void Consume(ThrowingHandler handler)
    {
    }

    public void TestMethod() => Consume($""text"");
}
", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_PropertySetter_CheckedOnly_ReportsAssignmentSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class Box
{
    public int Value
    {
        set
        {
            throw new InvalidOperationException();
        }
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var box = new Box();
        box.Value = 1;
    }
}",
            reportExceptions: null);

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            !d.GetMessage().Contains("throw new InvalidOperationException()", StringComparison.Ordinal));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_UsingExistingLocalDispose_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var resource = new ThrowingResource();
        using (resource)
        {
        }
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_UsingNullLiteralResource_DoesNotPropagateDispose()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        using ((ThrowingResource)null)
        {
        }
    }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_UsingNullLocalResource_DoesNotPropagateDispose()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        ThrowingResource resource = null;
        using (resource)
        {
        }
    }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_UsingMaybeNullLocalResource_StillPropagatesDispose()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(ThrowingResource resource)
    {
        using (resource)
        {
        }
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_UsingResourceAfterNullOnlyContinuation_DoesNotPropagateDispose()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(ThrowingResource resource)
    {
        if (resource != null)
        {
            return;
        }

        using (resource)
        {
        }
    }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_UsingResourceAfterNonNullGuard_StillPropagatesDispose()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(ThrowingResource resource)
    {
        if (resource == null)
        {
            return;
        }

        using (resource)
        {
        }
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_UsingDeclarationNullResource_DoesNotPropagateDispose()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        using var resource = (ThrowingResource)null;
    }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    [Test]
    public async Task Sp0010_UsingDeclarationMaybeNullResource_StillPropagatesDispose()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(ThrowingResource maybe)
    {
        using var resource = maybe;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_UsingExistingLocalDispose_CheckedOnly_ReportsUsingSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var resource = new ThrowingResource();
        using (resource)
        {
        }
    }
}",
            reportExceptions: null);

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("using (resource)", StringComparison.Ordinal));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_UsingNullLocalDispose_CheckedOnly_DoesNotReportUsingSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public sealed class ThrowingResource : IDisposable
{
    public void Dispose()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        ThrowingResource resource = null;
        using (resource)
        {
        }
    }
}",
            reportExceptions: null);

        Assert.That(
            diagnostics.Any(d =>
                d.Id == "SP0011" &&
                d.GetMessage().Contains("using (resource)", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task Sp0011_PlainUsing_IgnoresDisposeAsync()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Threading.Tasks;

public sealed class Resource : IDisposable, IAsyncDisposable
{
    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var resource = new Resource();
        using (resource)
        {
        }
    }
}",
            reportExceptions: null);

        Assert.That(
            diagnostics.Any(d =>
                d.Id == "SP0011" &&
                d.GetMessage().Contains("using (resource)", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task Sp0010_ForeachGetEnumerator_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;
using System.Collections;
using System.Collections.Generic;

public sealed class ThrowingEnumerable : IEnumerable<int>
{
    public ThrowingEnumerator GetEnumerator()
    {
        throw new InvalidOperationException();
    }

    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public sealed class ThrowingEnumerator : IEnumerator<int>
{
    public int Current => 0;
    object IEnumerator.Current => 0;
    public bool MoveNext() => false;
    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}

public class TestClass
{
    public void TestMethod()
    {
        foreach (var value in new ThrowingEnumerable())
        {
        }
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_ForeachGetEnumerator_CheckedOnly_ReportsForeachSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections;
using System.Collections.Generic;

public sealed class ThrowingEnumerable : IEnumerable<int>
{
    public ThrowingEnumerator GetEnumerator()
    {
        throw new InvalidOperationException();
    }

    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public sealed class ThrowingEnumerator : IEnumerator<int>
{
    public int Current => 0;
    object IEnumerator.Current => 0;
    public bool MoveNext() => false;
    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}

public class TestClass
{
    public void TestMethod()
    {
        foreach (var value in new ThrowingEnumerable())
        {
        }
    }
}",
            reportExceptions: null);

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("new ThrowingEnumerable()", StringComparison.Ordinal));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_PlainForeach_IgnoresDisposeAsync()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class SafeEnumerable : IEnumerable<int>
{
    public SafeEnumerator GetEnumerator()
    {
        return new SafeEnumerator();
    }

    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public sealed class SafeEnumerator : IEnumerator<int>, IAsyncDisposable
{
    public int Current => 0;
    object IEnumerator.Current => 0;

    public bool MoveNext()
    {
        return false;
    }

    public void Reset()
    {
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        foreach (var value in new SafeEnumerable())
        {
        }
    }
}",
            reportExceptions: null);

        Assert.That(
            diagnostics.Any(d =>
                d.Id == "SP0011" &&
                d.GetMessage().Contains("new SafeEnumerable()", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task Sp0010_UserDefinedOperator_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public readonly struct Token
{
    public static Token operator +(Token left, Token right)
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public Token TestMethod(Token left, Token right)
    {
        return left + right;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_UserDefinedConversion_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public readonly struct Token
{
    public static implicit operator int(Token value)
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public int TestMethod(Token value)
    {
        int result = value;
        return result;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_ImplicitConversionOperator_ReportsOperatorSummary()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public readonly struct Token
{
    public static implicit operator int(Token value)
    {
        throw new InvalidOperationException();
    }
}");

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0010" &&
            ContainsMethodName(d, "op_Implicit"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_ImplicitConversionOperator_ReportsThrowSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public readonly struct Token
{
    public static implicit operator int(Token value)
    {
        throw new InvalidOperationException();
    }
}");

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("throw new InvalidOperationException()", StringComparison.Ordinal));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_LocalDelegateTarget_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Action action = Dangerous;
        action();
    }

    private static void Dangerous()
    {
        throw new InvalidOperationException();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_LocalDelegateTargetsAcrossBranches_AllPropagate()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class TestClass
{
    public void TestMethod(bool first)
    {
        Action action;
        if (first)
            action = First;
        else
            action = Second;

        action();
    }

    private static void First() => throw new ArgumentException();
    private static void Second() => throw new InvalidOperationException();
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.ArgumentException;System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_InterfaceMethodDispatch_DirectExactConcreteReceiver_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        ((IService)new ThrowingService()).Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_InterfaceMethodDispatch_DirectExactConcreteReceiver_ReportsCallSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        ((IService)new ThrowingService()).Work();
    }
}");

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("((IService)new ThrowingService()).Work()", StringComparison.Ordinal));
        Assert.That(diagnostic.GetMessage(), Does.Contain("((IService)new ThrowingService()).Work()"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_InterfaceMethodDispatch_AliasLocalExactConcreteReceiver_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        IService service = new ThrowingService();
        IService alias = service;
        alias.Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0011_InterfaceMethodDispatch_AliasLocalExactConcreteReceiver_ReportsCallSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        IService service = new ThrowingService();
        IService alias = service;
        alias.Work();
    }
}");

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("alias.Work()", StringComparison.Ordinal));
        Assert.That(diagnostic.GetMessage(), Does.Contain("alias.Work()"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_VirtualMethodDispatch_DirectExactConcreteReceiver_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public abstract class Worker
{
    public abstract void Work();
}

public sealed class ThrowingWorker : Worker
{
    public override void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        ((Worker)new ThrowingWorker()).Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_VirtualMethodDispatch_CastLocalExactConcreteReceiver_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public abstract class Worker
{
    public abstract void Work();
}

public sealed class ThrowingWorker : Worker
{
    public override void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var concrete = new ThrowingWorker();
        Worker worker = (Worker)concrete;
        worker.Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_OpenVirtualMethodDispatch_ReportsUnknownException()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public class Worker
{
    public virtual void Work()
    {
    }
}

public sealed class ThrowingWorker : Worker
{
    public override void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(Worker worker)
    {
        worker.Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty], Is.EqualTo("unknown"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionCategoriesProperty],
            Is.EqualTo("dynamic_dispatch"));
    }

    [Test]
    public async Task Sp0011_OpenVirtualMethodDispatch_ReportsUnknownCallSite()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class Worker
{
    public virtual void Work()
    {
    }
}

public sealed class ThrowingWorker : Worker
{
    public override void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(Worker worker)
    {
        worker.Work();
    }
}");

        var diagnostic = diagnostics.Single(d =>
            d.Id == "SP0011" &&
            d.GetMessage().Contains("worker.Work()", StringComparison.Ordinal));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty], Is.EqualTo("unknown"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionCategoriesProperty],
            Is.EqualTo("dynamic_dispatch"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionSourcesProperty],
            Does.Contain("unknown=dynamic_dispatch:Worker.Work()"));
    }

    [Test]
    public async Task Sp0010_OpenInterfaceMethodDispatch_ReportsUnknownException()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod(IService service)
    {
        service.Work();
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty], Is.EqualTo("unknown"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionCategoriesProperty],
            Is.EqualTo("dynamic_dispatch"));
    }

    [Test]
    public async Task Sp0010_InterfacePropertyGetter_DirectExactConcreteReceiver_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public interface IValueSource
{
    int Value { get; }
}

public sealed class ThrowingValueSource : IValueSource
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}

public class TestClass
{
    public int TestMethod()
    {
        return ((IValueSource)new ThrowingValueSource()).Value;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_InterfacePropertyGetter_SameConcreteConditionalLocal_Propagates()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
using System;

public interface IValueSource
{
    int Value { get; }
}

public sealed class ThrowingValueSource : IValueSource
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}

public class TestClass
{
    public int TestMethod(bool chooseLeft)
    {
        IValueSource conditional = chooseLeft
            ? new ThrowingValueSource()
            : new ThrowingValueSource();
        IValueSource fallback = new ThrowingValueSource();
        IValueSource source = conditional ?? fallback;
        return source.Value;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task Sp0010_PriorLocalNullAssignment_ReportsNullReferenceException()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        string value = null!;
        return value.Length;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.NullReferenceException"));
    }

    [Test]
    public async Task Sp0010_PriorLocalZeroAssignment_ReportsDivideByZeroException()
    {
        var diagnostic = await SingleExceptionDiagnosticAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        int divisor = 0;
        return value / divisor;
    }
}", "TestMethod");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.DivideByZeroException"));
    }

    [Test]
    public async Task Sp0010_ConstantFalseConditionalInvocation_DoesNotReport()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        return false ? Dangerous() : 0;
    }

    private static int Dangerous()
    {
        throw new InvalidOperationException();
        }
}");

        Assert.That(HasExceptionDiagnosticForMethod(diagnostics, "TestMethod"), Is.False);
    }

    private static async Task<Diagnostic> SingleExceptionDiagnosticAsync(string source, string methodName)
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        return diagnostics.Single(d =>
            d.Id == "SP0010" && ContainsMethodName(d, methodName));
    }

    private static bool HasExceptionDiagnosticForMethod(ImmutableArray<Diagnostic> diagnostics, string methodName)
    {
        return diagnostics.Any(d =>
            d.Id == "SP0010" && ContainsMethodName(d, methodName));
    }

    private static bool ContainsMethodName(Diagnostic diagnostic, string methodName)
    {
        return diagnostic.GetMessage().Contains("'" + methodName + "'", StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        bool? reportExceptions = true,
        bool? checkedExceptions = true)
    {
        return await AnalyzerTestHost.GetExceptionFlowDiagnosticsAsync(
            source,
            "ExceptionFlowPropagationRegressionTests",
            reportExceptions,
            checkedExceptions);
    }
}
