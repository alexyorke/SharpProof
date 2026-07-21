using SharpProof.Attributes;

public static class TestClass
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int SumPairs(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}
