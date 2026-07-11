public static class ComplexityCandidate
{
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            sum += i + j;
        return sum;
    }
}
