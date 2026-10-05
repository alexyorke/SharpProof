// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject { public static int Target() {
    Contract.Ensures(false);
    while (true) { }
} }
