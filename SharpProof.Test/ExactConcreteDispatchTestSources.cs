using NUnit.Framework;

namespace SharpProof.Test;

internal static class ExactConcreteDispatchTestSources
{
    internal const string InterfaceMethodHierarchy = """
public interface IWorker
{
    [EnforcePure]
    int Compute(int value);
}

public class ExactWorker : IWorker
{
    [EnforcePure]
    public virtual int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|SP0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}
""";

    internal const string VirtualMethodHierarchy = """
public abstract class Worker
{
    [EnforcePure]
    public abstract int Compute(int value);
}

public class ExactWorker : Worker
{
    [EnforcePure]
    public override int Compute(int value) => value + 1;
}

public class ImpureWorker : ExactWorker
{
    [EnforcePure]
    public override int {|SP0002:Compute|}(int value)
    {
        Console.WriteLine(value);
        return value + 2;
    }
}
""";

    internal const string VirtualPropertyHierarchy = """
public abstract class BaseValue
{
    public abstract int Value { get; }
}

public class ExactValue : BaseValue
{
    public override int Value => 1;
}

public class ImpureValue : ExactValue
{
    public override int {|SP0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}
""";

    internal const string InterfacePropertyHierarchy = """
public interface IValueProvider
{
    int Value { get; }
}

public class ExactValueProvider : IValueProvider
{
    public virtual int Value => 1;
}

public class ImpureValueProvider : ExactValueProvider
{
    public override int {|SP0002:Value|}
    {
        [EnforcePure]
        get
        {
            Console.WriteLine(1);
            return 2;
        }
    }
}
""";

    internal static TestCaseData Scenario(string name, string hierarchy, string signature, string body)
    {
        return new TestCaseData(hierarchy, signature, body).SetName(name);
    }

    internal static string CreateSource(string hierarchy, string signature, string body)
    {
        var indentedBody = body.Replace("\n", "\n        ", StringComparison.Ordinal);
        return $@"
using System;
using SharpProof.Attributes;

{hierarchy}

public class TestClass
{{
    [EnforcePure]
    public int {signature}
    {{
        {indentedBody}
    }}
}}";
    }
}
