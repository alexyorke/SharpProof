using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ImpureOperationTests
    {
        [Test]
        public async Task ThrowOnlyMethod_ReportsSP0002()
        {


            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:ThrowingMethod|}()
    {
        throw new InvalidOperationException();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateInvocationOfImpureAction_ReportsSP0002()
        {


            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    // Static readonly field hiding an impure Console.WriteLine call inside the delegate.
    private static readonly Action ImpureAction = () => Console.WriteLine(""Side-effect"");

    [EnforcePure]
    public void {|SP0002:CallImpureDelegate|}()
    {
        // Invoking the delegate causes side-effects and is correctly flagged.
        ImpureAction();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LazyValueWithImpureFactory_ReportsSP0002()
        {


            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static int counter = 0;
    private readonly Lazy<int> _lazyValue = new Lazy<int>(() => {
        Console.WriteLine(""Impure factory executed""); // Impure action
        counter++; // Impure action
        return counter;
    });

    [EnforcePure]
    public int {|SP0002:GetLazyValue|}()
    {
        // Accessing .Value triggers the impure factory on first call.
        return _lazyValue.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConcurrentDictGetOrAddImpureFactory_ReportsSP0002()
        {


            var test = @"
using System;
using System.Collections.Concurrent;
using SharpProof.Attributes;

public class TestClass
{
    private readonly ConcurrentDictionary<string, int> _dict = new ConcurrentDictionary<string, int>();
    private static int _seed = 0;

    [EnforcePure]
    public int {|SP0002:GetValue|}(string key)
    {
        // The factory delegate () => { Console.WriteLine(); return ++_seed; } is impure.
        return _dict.GetOrAdd(key, k =>
        {
            Console.WriteLine($""Adding key {k}""); // Impure
            return ++_seed; // Impure
        });
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReturnRefToMutableField_ReportsSP0002()
        {


            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private int _mutableField = 0;

    [EnforcePure]
    public ref int {|SP0002:GetMutableFieldRef|}()
    {
        // Returning a ref to a mutable field is impure.
        return ref _mutableField;
    }

    // Example of use making it impure:
    // var tester = new TestClass();
    // ref int fieldRef = ref tester.GetMutableFieldRef();
    // fieldRef = 100; // Modifies the internal state via the returned ref
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task VolatileRead_ReportsSP0002()
        {


            var test = @"
using System;
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    private int _volatileField = 0;

    [EnforcePure]
    public int {|SP0002:ReadVolatile|}()
    {
        // Volatile.Read is impure.
        return Volatile.Read(ref _volatileField);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterlockedRead_ReportsSP0002()
        {
            var test = @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    private long _value;

    [EnforcePure]
    public long {|SP0002:ReadInterlocked|}()
    {
        return Interlocked.Read(ref _value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventSubscription_ReportsSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class Button 
{
    public event EventHandler Clicked;
    public void OnClick() => Clicked?.Invoke(this, EventArgs.Empty);
}

public class TestForm
{
    private Button _button = new Button();

    [EnforcePure]
    public void SetupForm()
    {
        _button.Clicked += Button_Clicked;
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        Console.WriteLine(""Button clicked"");
    }
}";



            var expectedSetup = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(16, 17, 16, 26).WithArguments("SetupForm");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedSetup });
        }

        [Test]
        public async Task ImpureStaticConstructorTrigger_ReportsSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class Config
{
    private static readonly string Setting;

    [EnforcePure] // Static ctors with side effects are impure
    static Config()
    {
        Console.WriteLine(""Initializing Config...""); // Impure
        Setting = ""InitializedValue"";
    }
}

public class TestClass
{
    [EnforcePure] // Accessing Config triggers static ctor
    public void TriggerStaticConstructor()
    {
        string value = Config.Setting; // CS0122 Error Here
    }
}";

            var expectedCctor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(10, 12, 10, 18).WithArguments(".cctor");
            var expectedTrigger = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(20, 17, 20, 41).WithArguments("TriggerStaticConstructor");
            var compilerError = DiagnosticResult.CompilerError("CS0122").WithSpan(22, 31, 22, 38).WithArguments("Config.Setting");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedCctor, expectedTrigger, compilerError });
        }

        [Test]
        public async Task SuppressFinalizeCall_ReportsDisposeImpurityDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class DisposableResource : IDisposable
{
    [EnforcePure]
    public void Dispose() { GC.SuppressFinalize(this); }
}

public class TestClass
{
    public void UseResource()
    {
        using (var res = new DisposableResource()) 
        { 
        }
    }
}";

            var expectedDispose = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(8, 17, 8, 24).WithArguments("Dispose");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedDispose);
        }

        [Test]
        public async Task ImpureImplicitConversion_ReportsMissingAttributeAndImpurityDiagnostics()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class ImpureConverter
{
    public int Value { get; }
    public ImpureConverter(int value) { Value = value; }

    public static implicit operator int(ImpureConverter ic)
    {
        Console.WriteLine($""Converting ImpureConverter({{ic.Value}}) to int"" + Environment.NewLine);
        return ic.Value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int ConvertIt(ImpureConverter ic)
    {
        int result = ic;
        return result;
    }
}";
            var diagGetValue = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                          .WithSpan(7, 16, 7, 21)
                                          .WithArguments("get_Value");
            var diagCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                      .WithSpan(8, 12, 8, 27)
                                      .WithArguments(".ctor");
            var diagConvertIt = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                           .WithSpan(20, 16, 20, 25)
                                           .WithArguments("ConvertIt");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { diagGetValue, diagCtor, diagConvertIt });
        }




        [Test]

        public async Task ImpureDelegateViaParameter_ReportsSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass 
{
    private static void ImpureTarget() => Console.WriteLine(""Impure Target Called""); // Line 7

    private void InvokeDelegate(Action action) => action(); // Line 9

    [EnforcePure]
    public void CallImpureDelegateViaParam() // Line 12 
    {{
        InvokeDelegate(ImpureTarget);
    }}
}
";
            var expectedCallImpure = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(12, 17, 12, 43)
                                   .WithArguments("CallImpureDelegateViaParam");







            await VerifyCS.VerifyAnalyzerAsync(test, expectedCallImpure);
        }


        [Test]

        public async Task ImpureDelegateViaReturnValue_ReportsSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass 
{
    private Action GetImpureAction() // Line 7
    {{
        return () => Console.WriteLine(""Impure action returned and called"");
    }}

    [EnforcePure]
    public void CallImpureDelegateViaReturn() // Line 13
    {{
        Action impure = GetImpureAction();
        impure();
    }}
}
";
            var expectedCallImpureReturn = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                          .WithSpan(13, 17, 13, 44)
                                          .WithArguments("CallImpureDelegateViaReturn");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedCallImpureReturn);
        }

        [Test]
        public async Task ImpureImplicitConversionViaMethodArg_ReportsMissingAttributeAndImpurityDiagnostics()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class ImpureConverterArg
{
    public int Value { get; }
    public ImpureConverterArg(int value) { Value = value; }

    public static implicit operator int(ImpureConverterArg ic)
    {
        Console.WriteLine($""Converting ImpureConverterArg({{ic.Value}}) to int"" + Environment.NewLine);
        return ic.Value;
    }
}

public class TestClass
{
    private void TakesInt(int i) { /* Does nothing */ }

    [EnforcePure]
    public void ConvertItViaArg(ImpureConverterArg ic)
    {
        TakesInt(ic);
    }
}";
            var diagGetValue = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                          .WithSpan(7, 16, 7, 21)
                                          .WithArguments("get_Value");
            var diagCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                      .WithSpan(8, 12, 8, 30)
                                      .WithArguments(".ctor");
            var diagTakesInt = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                        .WithSpan(19, 18, 19, 26)
                                        .WithArguments("TakesInt");
            var diagConvertItViaArg = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                           .WithSpan(22, 17, 22, 32)
                                           .WithArguments("ConvertItViaArg");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] { diagGetValue, diagCtor, diagTakesInt, diagConvertItViaArg });
        }

        [Test]
        public async Task IndirectStaticConstructorTrigger_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public static class Helper
{
    private static readonly string InitializedValue;
    [EnforcePure]
    static Helper()
    {
        Console.WriteLine(""Initializing Helper...""); // Impure
        InitializedValue = ""HelperValue"";
    }

    [EnforcePure]
    public static string GetValue() => InitializedValue;
}

public class AnotherClass
{
    // Calling Helper.GetValue implicitly runs Helper's static constructor
    [EnforcePure]
    public string TriggerIndirectStaticConstructor()
    {
        return Helper.GetValue();
    }
}
";

            var expectedCctor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(9, 12, 9, 18).WithArguments(".cctor");
            var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(16, 26, 16, 34).WithArguments("GetValue");
            var expectedTrigger = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(23, 19, 23, 51).WithArguments("TriggerIndirectStaticConstructor");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedCctor, expectedGetValue, expectedTrigger });
        }

        [Test]
        public async Task DelegateInvocation_PureDelegate_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int PureCalculation(int a, int b) => a + b;

    [EnforcePure]
    public int TestMethod()
    {
        Func<int, int, int> operation = PureCalculation;
        return operation(5, 10);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateInvocation_ConditionalPureDelegateAssignment_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int First() => 1;

    [EnforcePure]
    public int Second() => 2;

    [EnforcePure]
    public int TestMethod(bool useFirst)
    {
        Func<int> operation = useFirst ? new Func<int>(First) : new Func<int>(Second);
        return operation();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DelegateInvocation_RemovedDelegateTarget_RemainsConservativeDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int First() => 1;

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        Func<int> operation = First;
        operation -= First;
        return operation();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task GenericType_DefaultConstructor_ReportsSP0002()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestFactory<T> where T : new()
{
    [EnforcePure]
    public T {|SP0002:Create|}() 
    {
        return new T();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExternMethodWithoutBody_ReportsSP0002()
        {
            var test = @"
using System;
using System.Runtime.InteropServices;
using SharpProof.Attributes;

public static class NativeInterop
{
    [EnforcePure]
    [DllImport(""kernel32.dll"")]
    public static extern int {|SP0002:NativeMethod|}();

    [EnforcePure]
    public static int {|SP0002:UsesNativeMethod|}(int input)
    {
        return NativeMethod() + input;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
