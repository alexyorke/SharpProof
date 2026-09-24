namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullableValueTypeReceiverRegressionTests
{
    [Test]
    public void NullableCallsSkipReferenceChecksButBoxedCallsKeepThem()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static bool HasValue(int? value) => value.HasValue;
                public static int GetDefault(int? value) =>
                    value.GetValueOrDefault();
                public static int Unwrap(int? value) => value.Value;
                public static System.Type GetTypeOfNullable(int? value) =>
                    value.GetType();
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var nullReference = EffectTestHost.RequireType(
            compilation,
            "System.NullReferenceException");
        var invalidOperation = EffectTestHost.RequireType(
            compilation,
            "System.InvalidOperationException");

        foreach (var methodName in new[] { "HasValue", "GetDefault" })
        {
            Assert.That(
                session.Analyze(
                    EffectTestHost.SampleMethod(compilation, methodName))
                    .Summary.Throws.Types,
                Does.Not.Contain(nullReference),
                methodName);
        }

        var unwrap = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Unwrap"));
        Assert.That(unwrap.Summary.Throws.Types, Does.Contain(invalidOperation));
        Assert.That(unwrap.Summary.Throws.Types, Does.Not.Contain(nullReference));

        var getType = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "GetTypeOfNullable"));
        Assert.That(getType.Summary.Throws.Types, Does.Contain(nullReference));
    }
}
