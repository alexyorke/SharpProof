namespace SharpProof.Test;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class ReadmeExampleAttribute : Attribute
{
    public ReadmeExampleAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
