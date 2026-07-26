namespace SharpProof.Attributes;

[Obsolete(
    "Complexity analysis was removed from SharpProof 0.2. " +
    "This enum exists only to compile code while the legacy annotation is removed.")]
public enum ComplexityKind {
    Constant = 0,
    Linear = 1,
    Quadratic = 2,
    Logarithmic = 3,
    Linearithmic = 4,
    Product = 5,
    Max = 6
}
