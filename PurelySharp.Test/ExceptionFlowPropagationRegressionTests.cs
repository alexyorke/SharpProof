using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ExceptionFlowPropagationRegressionTests
    {
        [Test]
        public async Task Ps0010_ConstantFalseThrow_DoesNotReport()
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
        public async Task Ps0010_CatchWhenTrue_SuppressesException()
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
        public async Task Ps0010_PropertySetter_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_UsingExistingLocalDispose_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_ForeachGetEnumerator_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_UserDefinedOperator_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_UserDefinedConversion_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_ImplicitConversionOperator_ReportsOperatorSummary()
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
                d.Id == PurelySharpDiagnostics.ExceptionSummaryId &&
                ContainsMethodName(d, "op_Implicit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0011_ImplicitConversionOperator_ReportsThrowSite()
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
                d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId &&
                d.GetMessage().Contains("throw new InvalidOperationException()", StringComparison.Ordinal));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_LocalDelegateTarget_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_InterfaceMethodDispatch_DirectExactConcreteReceiver_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0011_InterfaceMethodDispatch_DirectExactConcreteReceiver_ReportsCallSite()
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
                d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId &&
                d.GetMessage().Contains("((IService)new ThrowingService()).Work()", StringComparison.Ordinal));
            Assert.That(diagnostic.GetMessage(), Does.Contain("((IService)new ThrowingService()).Work()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_InterfaceMethodDispatch_AliasLocalExactConcreteReceiver_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0011_InterfaceMethodDispatch_AliasLocalExactConcreteReceiver_ReportsCallSite()
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
                d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId &&
                d.GetMessage().Contains("alias.Work()", StringComparison.Ordinal));
            Assert.That(diagnostic.GetMessage(), Does.Contain("alias.Work()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_VirtualMethodDispatch_DirectExactConcreteReceiver_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_VirtualMethodDispatch_CastLocalExactConcreteReceiver_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_InterfacePropertyGetter_DirectExactConcreteReceiver_Propagates()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_InterfacePropertyGetter_SameConcreteConditionalLocal_Propagates()
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
        IValueSource source = chooseLeft
            ? new ThrowingValueSource()
            : new ThrowingValueSource();
        return source.Value;
    }
}", "TestMethod");

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_PriorLocalNullAssignment_ReportsNullReferenceException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
        }

        [Test]
        public async Task Ps0010_PriorLocalZeroAssignment_ReportsDivideByZeroException()
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

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_ConstantFalseConditionalInvocation_DoesNotReport()
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
            return diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId && ContainsMethodName(d, methodName));
        }

        private static bool HasExceptionDiagnosticForMethod(ImmutableArray<Diagnostic> diagnostics, string methodName)
        {
            return diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId && ContainsMethodName(d, methodName));
        }

        private static bool ContainsMethodName(Diagnostic diagnostic, string methodName)
        {
            return diagnostic.GetMessage().Contains("'" + methodName + "'", StringComparison.Ordinal);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "ExceptionFlowPropagationRegressionTests",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerOptions = new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(
                    ImmutableDictionary<string, string>.Empty
                        .Add("purelysharp_report_exceptions", "true")
                        .Add("purelysharp_checked_exceptions", "true")));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out value!))
                {
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
