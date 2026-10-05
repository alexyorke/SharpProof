// golden-mode: Total
public static class C
{
    private static void Sink() { System.Console.WriteLine(1); }

    public static int Target(int x)
    {
        System.Action action = new System.Action(Sink);
        return x + 1;
    }
}
