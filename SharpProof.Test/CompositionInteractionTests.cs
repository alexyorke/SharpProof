using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;

namespace SharpProof.Test
{
    [TestFixture]
    public class CompositionInteractionTests
    {
        [Test]
        public async Task CompositionWithPureAndImpureCalls_Diagnostics()
        {
            var test = @"
using SharpProof.Attributes;
using System;

public class Engine
{
    [EnforcePure]
    public int GetHorsepower() => 150; // Pure

    [EnforcePure]
    public void Start() // Impure
    {
        Console.WriteLine(""Engine started"");
    }
}

public class Wheels
{
    [EnforcePure]
    public int GetDiameter() => 20; // Pure
}

public class Car
{
    private readonly Engine _engine = new Engine();
    private readonly Wheels _wheels = new Wheels();

    [EnforcePure]
    public int GetCarInfoPure() // Pure
    {
        return _engine.GetHorsepower() + _wheels.GetDiameter();
    }

    [EnforcePure]
    public void StartCar() // Impure (calls Engine.Start)
    {
        _engine.Start();
    }

    [EnforcePure]
    public int GetPowerToWheelRatio() // Impure (calls Engine.Start indirectly via StartCar) -> This is debatable, depends on analysis depth. Assume direct call impurity.
    {
        // Let's assume StartCar is impure, making this impure too if called.
        // For simplicity, let's test direct impure call:
        _engine.Start();
        return _engine.GetHorsepower() / _wheels.GetDiameter();
    }
}
";




            var expectedEngineStart = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                              .WithSpan(11, 17, 11, 22)
                                              .WithArguments("Start");
            var expectedCarStartCar = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                              .WithSpan(35, 17, 35, 25)
                                              .WithArguments("StartCar");
            var expectedGetPowerToWheelRatio = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                                       .WithSpan(41, 16, 41, 36)
                                                       .WithArguments("GetPowerToWheelRatio");

            await VerifyCS.VerifyAnalyzerAsync(test,
                                             expectedEngineStart,
                                             expectedCarStartCar,
                                             expectedGetPowerToWheelRatio);
        }
    }
}