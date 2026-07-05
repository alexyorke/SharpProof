using System;

namespace SharpProof.Test
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class ReadmeExampleAttribute : Attribute
    {
        public ReadmeExampleAttribute(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }
}
