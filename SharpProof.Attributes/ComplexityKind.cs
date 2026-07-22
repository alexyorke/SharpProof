namespace SharpProof.Attributes;
/// <summary>
///     Declarable asymptotic complexity bounds for <see cref="ExpectedComplexityAttribute" />.
/// </summary>
/// <remarks>
///     The numeric values are stable for backward compatibility and do not encode growth order.
///     The analyzer ranks these shapes with an explicit partial order, so <c>Constant</c>,
///     <c>Logarithmic</c>, <c>Linear</c>, <c>Linearithmic</c>, and <c>Quadratic</c> form a total
///     chain, while <c>Product</c> (<c>O(n*m)</c>) and <c>Max</c> (<c>O(max(n, m))</c>) involve
///     independent size parameters and are only comparable to themselves and to <c>Constant</c>.
///     Unknown and recursive-unknown are reported states, not declarable bounds.
/// </remarks>
public enum ComplexityKind {
    /// <summary><c>O(1)</c>.</summary>
    Constant = 0,
    /// <summary><c>O(n)</c>.</summary>
    Linear = 1,
    /// <summary><c>O(n^2)</c>.</summary>
    Quadratic = 2,
    /// <summary><c>O(log n)</c>.</summary>
    Logarithmic = 3,
    /// <summary><c>O(n log n)</c>.</summary>
    Linearithmic = 4,
    /// <summary><c>O(n*m)</c> over independent size parameters.</summary>
    Product = 5,
    /// <summary><c>O(max(n, m))</c> over independent size parameters.</summary>
    Max = 6
}
