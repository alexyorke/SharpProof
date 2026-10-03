// golden-mode: Total
public static class C
{
    public static System.Action Target(Receiver receiver)
    {
        return new System.Action(Pass(receiver).Sink);
    }
    private static Receiver Pass(Receiver receiver)
    {
        return receiver;
    }
}
public class Receiver
{
    public void Sink() { }
}
