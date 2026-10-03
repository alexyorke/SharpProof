// golden-scenario: native-infrastructure
// A native failure after an assertion makes the persistent candidate unavailable.
public static class Subject
{
    public static int Target(int value) { return value; }
}
