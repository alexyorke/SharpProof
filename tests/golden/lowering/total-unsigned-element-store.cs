// golden-mode: Total
// golden-opaque-calls: true
public static class Subject
{
    public static int Target(int[] values, uint index, int value)
    {
        values[index] = value;
        values[index++] &= (value = 5);
        return value;
    }
}
