namespace SharpProof.Test;
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class ReadmeExampleAttribute(string id) : Attribute { public string Id { get; } = id; }
