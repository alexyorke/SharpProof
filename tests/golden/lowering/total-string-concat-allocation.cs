// golden-mode: Total
// golden-inline-source: true
// golden-framework-models: true
public static class C
{
    public static int Target(string left, string right, string third)
    {
        string pair = string.Concat(left, right);
        string triple = left + right + third;
        return pair.Length + triple.Length;
    }
}
