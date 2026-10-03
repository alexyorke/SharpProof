// golden-mode: Total
// golden-inline-source: true
public class Base { public virtual void Sink() { } }
public class Receiver : Base { public sealed override void Sink() { } }
public static class C
{
    public static System.Action Target(Receiver receiver) => new System.Action(receiver.Sink);
}
