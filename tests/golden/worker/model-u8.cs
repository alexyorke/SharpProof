// golden-scenario: model-u8
// The directive verifies an exact native bitvector model and concrete typed identity replay.
public static class Subject
{
    public static byte Target(byte value) => value;
}
