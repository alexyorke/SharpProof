public static class Subject
{
    public static long Target(long value, long divisor)
    {
        if (divisor == 0) return 0;
        return value / divisor;
    }
}
