// golden-mode: Total
// golden-inline-source: true
public class Receiver { public void Sink() { } }
public static class C
{
    private static Receiver Pass(Receiver receiver, int value) { return receiver; }

    public static int Target(Receiver receiver, int x)
    {
        try
        {
            System.Action action = new System.Action(Pass(receiver, 10 / x).Sink);
            return x;
        }
        catch (System.ArgumentException)
        {
            return 7;
        }
    }
}
