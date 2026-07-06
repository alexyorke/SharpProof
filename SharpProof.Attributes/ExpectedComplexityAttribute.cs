using System;

namespace SharpProof.Attributes
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    public sealed class ExpectedComplexityAttribute : Attribute
    {
        public ExpectedComplexityAttribute(ComplexityKind maximumComplexity)
        {
            MaximumComplexity = maximumComplexity;
        }

        public ComplexityKind MaximumComplexity { get; }
    }
}
