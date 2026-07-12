namespace SharpProof.Test;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class ReadmeExampleAttribute : Attribute
{
    public ReadmeExampleAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
