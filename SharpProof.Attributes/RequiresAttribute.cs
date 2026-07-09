using System;

namespace SharpProof.Attributes
{
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class RequiresAttribute : Attribute
    {
        public RequiresAttribute(string condition)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        public string Condition { get; }
    }
}
