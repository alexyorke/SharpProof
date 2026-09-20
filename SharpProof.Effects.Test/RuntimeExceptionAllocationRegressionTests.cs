namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class RuntimeExceptionAllocationRegressionTests
{
    [Test]
    public void RuntimeAndExistingExceptionThrowsChargeManagedAllocation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable

            public sealed class Node
            {
                public int Value;
            }

            public static class Sample
            {
                public static int Divide(int left, int right)
                {
                    try
                    {
                        return left / right;
                    }
                    catch (System.DivideByZeroException)
                    {
                        return 0;
                    }
                }

                public static int NullReceiver(Node? node)
                {
                    try
                    {
                        return node.Value;
                    }
                    catch (System.NullReferenceException)
                    {
                        return 0;
                    }
                }

                public static int Index(int[] values, int index)
                {
                    try
                    {
                        return values[index];
                    }
                    catch (System.IndexOutOfRangeException)
                    {
                        return 0;
                    }
                }

                public static int Checked(int value)
                {
                    try
                    {
                        return checked(value + 1);
                    }
                    catch (System.OverflowException)
                    {
                        return int.MaxValue;
                    }
                }

                public static int Existing(System.Exception exception)
                {
                    try
                    {
                        throw exception;
                    }
                    catch (System.Exception)
                    {
                        return 0;
                    }
                }

                public static void Rethrow(System.Exception exception)
                {
                    try
                    {
                        throw exception;
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "Divide",
                     "NullReceiver",
                     "Index",
                     "Checked",
                     "Existing",
                     "Rethrow"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Allocation,
                    Is.EqualTo(EffectAllocationKind.Managed),
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
            }
        }
    }
}
