// golden-mode: Total
public static class Subject
{
    public static int Target(int x, int y)
    {
        int value = checked(x++ & ++y);
        value &= x;
        return value;
    }
}
