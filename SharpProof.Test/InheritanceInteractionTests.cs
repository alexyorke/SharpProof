using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class InheritanceInteractionTests
{
    [Test]
    public async Task DeepInheritanceAndAbstractState_MissingAttributeDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;
using System;

public abstract class Device
{
    public Guid DeviceId { get; } // SP0004 get
    protected Device(Guid id) { DeviceId = id; } // SP0004 .ctor
    [EnforcePure] public abstract string GetStatus();
    // [EnforcePure] // Removed - Expect SP0004
    public Guid GetDeviceId() => DeviceId; // SP0004 Method
}

public abstract class NetworkedDevice : Device
{
    public string IPAddress { get; } // SP0004 get
    protected NetworkedDevice(Guid id, string ip) : base(id) { IPAddress = ip; } // SP0004 .ctor
    [EnforcePure] public override string GetStatus() => $""Device {base.DeviceId} online at {IPAddress}"";
    [EnforcePure] public abstract bool Ping();
    // [EnforcePure] // Removed - Expect SP0004
    public string GetIpAddress() => IPAddress; // SP0004 Method
}

public class SmartLight : NetworkedDevice
{
    public int Brightness { get; } // SP0004 get
    public SmartLight(Guid id, string ip, int brightness) : base(id, ip) { Brightness = brightness; } // SP0004 .ctor
    [EnforcePure] public override bool Ping() => IPAddress != null && IPAddress.Length > 0;
    // [EnforcePure] // Removed - Expect SP0004
    public int GetBrightness() => Brightness; // SP0004 Method
}

public class TestManager
{
    [EnforcePure]
    public string CheckLightStatus(SmartLight light) => light.GetStatus();

    [EnforcePure]
    public bool PingLight(SmartLight light) => light.Ping();

    [EnforcePure]
    public string GetFullLightDetails(SmartLight light)
    {
        Guid id = light.GetDeviceId();
        string ip = light.GetIpAddress();
        int brightness = light.GetBrightness();
        return $""ID: {id}, IP: {ip}, Brightness: {brightness}"";
    }
}
";

        var expectedGetDeviceIdProp = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(7, 17, 7, 25).WithArguments("get_DeviceId");
        var expectedCtorDevice = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(8, 15, 8, 21).WithArguments(".ctor");
        var expectedGetDeviceIdMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 17, 11, 28).WithArguments("GetDeviceId");
        var expectedGetIpAddressProp = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(16, 19, 16, 28).WithArguments("get_IPAddress");
        var expectedCtorNetworked = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(17, 15, 17, 30).WithArguments(".ctor");
        var expectedGetIpAddressMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(21, 19, 21, 31).WithArguments("GetIpAddress");
        var expectedGetBrightnessProp = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(26, 16, 26, 26).WithArguments("get_Brightness");
        var expectedCtorLight = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(27, 12, 27, 22).WithArguments(".ctor");
        var expectedGetBrightnessMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(30, 16, 30, 29).WithArguments("GetBrightness");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedGetDeviceIdProp,
            expectedCtorDevice,
            expectedGetDeviceIdMethod,
            expectedGetIpAddressProp,
            expectedCtorNetworked,
            expectedGetIpAddressMethod,
            expectedGetBrightnessProp,
            expectedCtorLight,
            expectedGetBrightnessMethod);
    }

    [Test]
    public async Task AbstractClassWithMixedPurity_Diagnostics()
    {
        var testCode = @"
using SharpProof.Attributes;
using System;

public abstract class DataProcessor
{
    public abstract string Name { get; }

    [EnforcePure] // Abstract declaration itself is not diagnosed; callers still stay conservative when overrides can vary.
    public abstract int Process(int data);

    [EnforcePure] // The base implementation is impure because int.ToString() is culture-sensitive.
    public virtual string Format(int data) => data.ToString();

    [EnforcePure] // Calls through the abstract slot, so downstream dispatch remains conservative.
    public int ProcessAndDouble(int data)
    {
        return Process(data) * 2;
    }

    // Concrete impure method
    [EnforcePure]
    public void LogStatus(string status)
    {
        Console.WriteLine($""{Name}: {status}""); // Impure
    }
}

public class DoublingProcessor : DataProcessor
{
    public override string Name => ""Doubler"";

    // Pure implementation of abstract method
    [EnforcePure]
    public override int Process(int data) => data * 2;

    // Pure override of virtual method
    [EnforcePure]
    public override string Format(int data) => $""Data={data}"";
}

public class AddingProcessor : DataProcessor
{
    public override string Name => ""Adder"";
    private int _offset = 5; // Instance state

    // Impure implementation of abstract method
    [EnforcePure]
    public override int Process(int data)
    {
        _offset++; // Modifies state
        return data + _offset;
    }

    // Impure override of virtual method
    [EnforcePure]
    public override string Format(int data)
    {
        Console.WriteLine(""Formatting...""); // Impure call
        return $""Value: {data}"";
    }
}

public class TestUsage
{
     [EnforcePure]
     public int UseProcessorPurely(DataProcessor p, int value)
     {
         int processed = p.ProcessAndDouble(value);
         string formatted = p.Format(processed);
         return formatted.Length;
     }

     [EnforcePure]
     public void UseProcessorImpurely(DataProcessor p, string msg)
     {
         // Calls impure LogStatus
         p.LogStatus(msg);
     }
}
";

        var expected = new[]
        {
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(13, 27, 13, 33)
                .WithArguments("Format"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(16, 16, 16, 32)
                .WithArguments("ProcessAndDouble"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(23, 17, 23, 26)
                .WithArguments("LogStatus"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(49, 25, 49, 32)
                .WithArguments("Process"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(57, 28, 57, 34)
                .WithArguments("Format"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(67, 17, 67, 35)
                .WithArguments("UseProcessorPurely"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(75, 18, 75, 38)
                .WithArguments("UseProcessorImpurely")
        };

        await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
    }

    [Test]
    public async Task PrivateProtectedVirtualDispatch_ResolvesWithinCompilationAndCanBePure()
    {
        var test = @"
using SharpProof.Attributes;

public class BaseComponent
{
    [EnforcePure]
    private protected virtual int Compute(int value)
    {
        return value + 1;
    }

    [EnforcePure]
    public int ReadValue(int value)
    {
        return Compute(value) * 2;
    }
}

public class DerivedComponent : BaseComponent
{
    [EnforcePure]
    private protected override int Compute(int value)
    {
        return value * 3;
    }
}

public class Consumer
{
    [EnforcePure]
    public int Snapshot(int input)
    {
        var component = new DerivedComponent();
        return component.ReadValue(input);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ProtectedVirtualDispatch_OnOpenReceiver_ConservativeImpure()
    {
        var test = @"
using SharpProof.Attributes;

public class BaseWorker
{
    protected virtual int Compute(int value)
    {
        return value;
    }
}

public class WorkerHost : BaseWorker
{
    [EnforcePure]
    public int {|SP0002:ComputeWithProtectedVirtual|}(int value)
    {
        return Compute(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PublicMethodCallingPrivateProtectedVirtualDispatch_CanBePure()
    {
        var test = @"
using SharpProof.Attributes;

public class BaseComponent
{
    [EnforcePure]
    private protected virtual int Compute(int value)
    {
        return value + 1;
    }

    [EnforcePure]
    public int Snapshot(int value)
    {
        return Compute(value) * 2;
    }
}

public class DerivedComponent : BaseComponent
{
    [EnforcePure]
    private protected override int Compute(int value)
    {
        return value * 3;
    }
}

public class Consumer
{
    [EnforcePure]
    public int ReadValue(BaseComponent component, int value)
    {
        return component.Snapshot(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SealedOverride_ConcreteReceiverCanBePure()
    {
        var test = @"
using SharpProof.Attributes;
using System;

public class BaseWorker
{
    public virtual int Compute(int value)
    {
        Console.WriteLine(value);
        return value;
    }
}

public class PureWorker : BaseWorker
{
    public sealed override int Compute(int value)
    {
        return value + 1;
    }
}

public class BadWorker : BaseWorker
{
    public override int Compute(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}

public class WorkerHost
{
    [EnforcePure]
    public int Process(PureWorker worker, int value)
    {
        return worker.Compute(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ReadonlyAbstractField_ConcreteInitializerCanBePure()
    {
        var test = @"
using SharpProof.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public sealed class PureWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class WorkerHost
{
    private readonly Worker _worker = new PureWorker();

    [EnforcePure]
    public int Process(int value)
    {
        return _worker.Compute(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MutableAbstractField_InitializerRemainsConservative()
    {
        var test = @"
using SharpProof.Attributes;

public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public sealed class PureWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public sealed class ImpureWorker : Worker
{
    [EnforcePure]
    public override int {|SP0002:Compute|}(int value)
    {
        System.Console.WriteLine(value);
        return value;
    }
}

public class WorkerHost
{
    private Worker _worker = new PureWorker();

    [EnforcePure]
    public int {|SP0002:Process|}(int value)
    {
        return _worker.Compute(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}