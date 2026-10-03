// golden-scenario: native-cancellation
// Cancel after a real native check completes, before its resource charge is published.
public static class Subject
{
    public static int Target(int value) { return value; }
}
