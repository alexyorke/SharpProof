using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class DictionaryOpaqueCountOracleTests
{
    [TestCase("integer")]
    [TestCase("throwing-reference")]
    [TestCase("null-reference")]
    public void ConstrainedOpaqueValueStorageChangesCountWithoutObservingTheValue(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var (type, value) = domain switch
        {
            "integer" => (typeof(int), (object?)17),
            "throwing-reference" => (typeof(CallbackValue), (object?)new CallbackValue()),
            "null-reference" => (typeof(CallbackValue), null),
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };
        const string source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { " +
            "public static int Target<T>(T value) where T : System.IComparable<T> { Contract.Ensures(true); " +
            "var data = new Dictionary<int, T>(); data.Add(17, value); return data.Count; } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var parameter = total.Parameters.Single().Entry;
        var parameterType = factory.GetVariableInfo(parameter).Type;
        var inputs = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(parameter,
            !type.IsValueType && value == null ? factory.CreateNullValue(parameterType)
                : factory.CreateReferenceValue(parameterType, new IrObjectState()));
        IrValue? SelectNormal(IrHavocRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        }

        // Merely selecting a successful initialization path never establishes
        // native replay authority, even though copying the opaque atom is exact.
        var selected = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 10000,
            new IrProgramReplayOptions(SelectNormal));
        Check(selected, approximation: true);

        // Reuse only the existing fixed primitive-key observation. The value
        // receives no observer, interpretation, conversion or callback authority.
        var observedInitializations = 0;
        var observedOptions = new IrProgramReplayOptions(SelectNormal)
        {
            ConcreteHavocValueProvider = request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, primitive =>
                {
                    ArgumentNullException.ThrowIfNull(primitive);
                    observedInitializations++;
                    return PassiveCallableVcPlan.WarmDefaultDictionary(primitive);
                });
            }
        };
        var observed = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 10000, observedOptions);
        Check(observed, approximation: false);
        Assert.That(observedInitializations, Is.GreaterThan(0));

        // A failed observation cannot certify the same selected normal path.
        var rejectedInitializations = 0;
        var rejectedOptions = new IrProgramReplayOptions(SelectNormal)
        {
            ConcreteHavocValueProvider = request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, primitive =>
                {
                    ArgumentNullException.ThrowIfNull(primitive);
                    rejectedInitializations++;
                    return false;
                });
            }
        };
        var rejected = new IrProgramInterpreter(factory).Execute(total.Program, inputs, 10000, rejectedOptions);
        Check(rejected, approximation: true);
        Assert.That(rejectedInitializations, Is.GreaterThan(0));

        var compilation = TestCompilation.CreateExact("DictionaryOpaqueCountOracle", [("Probe.cs", source)]);
        var syntax = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(syntax,
            syntax.WithRootAndOptions(syntax.GetRoot(), ((CSharpParseOptions)syntax.Options).WithPreprocessorSymbols(ImmutableArray<string>.Empty)));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("DictionaryOpaqueCountOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.MakeGenericMethod(type);
            Assert.That(target.Invoke(null, [value]), Is.EqualTo(1));
        }
        finally
        { runtime.Unload(); }
    }

    private static void Check(IrProgramExecutionResult execution, bool approximation)
    {
        ArgumentNullException.ThrowIfNull(execution);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), execution.Unsupported?.Detail);
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(1));
        Assert.That(execution.ConsumedApproximation, Is.EqualTo(approximation));
    }

    private sealed class CallbackValue : IComparable<CallbackValue>
    {
        public int CompareTo(CallbackValue? other)
        {
            Assert.Fail("Value comparison must not run during dictionary storage.");
            return 0;
        }

        public override bool Equals(object? obj)
        {
            Assert.Fail("Value equality must not run during dictionary storage.");
            return false;
        }

        public override int GetHashCode()
        {
            Assert.Fail("Value hashing must not run during dictionary storage.");
            return 0;
        }
    }
}
