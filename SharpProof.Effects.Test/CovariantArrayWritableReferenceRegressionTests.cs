namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CovariantArrayWritableReferenceRegressionTests
{
    [Test]
    public void WritableArrayElementReferencesReportArrayTypeMismatch()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static object s_cell = new object();

                private static void SetOut(out object value) =>
                    value = new object();

                private static void Touch(ref object value) { }

                public static void OutArgument(object[] values) =>
                    SetOut(out values[0]);

                public static void RefArgument(object[] values) =>
                    Touch(ref values[0]);

                public static void RefLocal(object[] values) {
                    ref object value = ref values[0];
                }

                public static void RefReadonlyLocal(object[] values) {
                    ref readonly object value = ref values[0];
                }

                public static ref object RefReturn(object[] values) =>
                    ref values[0];

                public static void RefReturnThroughHelper(object[] values) {
                    ref object value = ref RefReturn(values);
                }

                public static void MultiDimensionalRef(object[,] values) {
                    ref object value = ref values[0, 0];
                }

                public static void RefAssignment(object[] values) {
                    ref object value = ref s_cell;
                    value = ref values[0];
                }

                public static void ExactArrayRef() {
                    object[] values = new object[1];
                    ref object value = ref values[0];
                }

                public static void CovariantArrayRef() {
                    object[] values = new string[1];
                    ref object value = ref values[0];
                }

                public static void InArgument(object[] values) {
                    TouchIn(in values[0]);
                }

                private static void TouchIn(in object value) { }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
            "OutArgument", "RefArgument", "RefLocal", "RefReturn",
            "RefReturnThroughHelper", "RefAssignment", "MultiDimensionalRef",
            "CovariantArrayRef"
        })
        {
            var summary = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName)).Summary;
            Assert.That(
                summary.Throws.Types.Select(static type => type.ToDisplayString()),
                Does.Contain("System.ArrayTypeMismatchException"),
                methodName);
        }

        foreach (var methodName in new[] { "InArgument", "RefReadonlyLocal", "ExactArrayRef" })
        {
            Assert.That(
                session.Analyze(EffectTestHost.SampleMethod(compilation, methodName))
                    .Summary.Throws.Types.Select(static type => type.ToDisplayString()),
                Does.Not.Contain("System.ArrayTypeMismatchException"),
                methodName);
        }
    }
}
