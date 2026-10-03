// golden-scenario: native-resource
// The directive checks that completed native work remains charged after resource exhaustion.
public static class Subject
{
    public static bool Target(bool value) => value;
}
