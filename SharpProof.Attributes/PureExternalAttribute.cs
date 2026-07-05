using System;

namespace SharpProof.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class PureExternalAttribute : Attribute
    {
    }
}
