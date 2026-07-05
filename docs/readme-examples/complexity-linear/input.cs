public static class Example
{
    public static int Sum(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}
