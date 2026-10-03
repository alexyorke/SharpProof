// golden-scenario: model-u16
// The directive verifies an exact native bitvector model and concrete typed identity replay.
public static class Subject
{
    public static ushort Target(ushort value) => value;
}
