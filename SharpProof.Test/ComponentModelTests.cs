using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ComponentModelTests
{
    [Test]
    public async Task TypeDescriptorGetConverter_Diagnostic()
    {
        var test = @"
using System;
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeConverter {|SP0002:TestMethod|}(Type type)
    {
        return TypeDescriptor.GetConverter(type);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TypeDescriptorGetProperties_Diagnostic()
    {
        var test = @"
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyDescriptorCollection {|SP0002:TestMethod|}(object value)
    {
        return TypeDescriptor.GetProperties(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CancelEventArgsCancel_Diagnostic()
    {
        var test = @"
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(CancelEventArgs args)
    {
        return args.Cancel;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CancelEventArgsCancelSetter_Diagnostic()
    {
        var test = @"
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(CancelEventArgs args)
    {
        args.Cancel = true;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AddingNewEventArgsConstructor_NoDiagnostic()
    {
        var test = @"
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public AddingNewEventArgs TestMethod()
    {
        return new AddingNewEventArgs();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ComponentDispose_Diagnostic()
    {
        var test = @"
using System.ComponentModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Component component)
    {
        component.Dispose();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}