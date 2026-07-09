using System;

namespace SharpProof.Attributes
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = false)]
    public sealed class DoesNotThrowAttribute : Attribute
    {
    }
}
