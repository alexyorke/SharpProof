// golden-scenario: core-advisory
public static class Subject
{
    public static int Target(int value) => value + 1;

    public static int Branch(int value)
    {
        if (value < 0)
        {
            return 0;
        }
        return value;
    }
}
