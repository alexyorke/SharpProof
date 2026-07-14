using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ConsoleTests
{
    public sealed record ConsoleOperationCase(string Name, string Source);

    private static readonly ConsoleOperationCase[] Cases =
    {
        new("ConsoleOut_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TextWriter {|SP0002:TestMethod|}()
    {
        return Console.Out;
    }
}"),
        new("ConsoleError_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TextWriter {|SP0002:TestMethod|}()
    {
        return Console.Error;
    }
}"),
        new("ConsoleIn_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TextReader {|SP0002:TestMethod|}()
    {
        return Console.In;
    }
}"),
        new("ConsoleReadLine_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Console.ReadLine();
    }
}"),
        new("ConsoleWriteString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.Write(""impure"");
    }
}"),
        new("ConsoleWriteLineString_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.WriteLine(""impure"");
    }
}"),
        new("ConsoleBackgroundColor_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConsoleColor {|SP0002:TestMethod|}()
    {
        return Console.BackgroundColor;
    }
}"),
        new("ConsoleForegroundColor_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConsoleColor {|SP0002:TestMethod|}()
    {
        return Console.ForegroundColor;
    }
}"),
        new("ConsoleBufferWidth_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.BufferWidth;
    }
}"),
        new("ConsoleWindowWidth_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowWidth;
    }
}"),
        new("ConsoleWindowHeight_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowHeight;
    }
}"),
        new("ConsoleCursorLeft_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorLeft;
    }
}"),
        new("ConsoleCursorTop_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorTop;
    }
}"),
        new("ConsoleKeyAvailable_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.KeyAvailable;
    }
}"),
        new("ConsoleWindowLeft_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowLeft;
    }
}"),
        new("ConsoleWindowTop_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowTop;
    }
}"),
        new("ConsoleCursorVisible_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.CursorVisible;
    }
}"),
        new("ConsoleCursorSize_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorSize;
    }
}"),
        new("ConsoleIsOutputRedirected_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsOutputRedirected;
    }
}"),
        new("ConsoleIsInputRedirected_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsInputRedirected;
    }
}"),
        new("ConsoleIsErrorRedirected_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsErrorRedirected;
    }
}"),
        new("ConsoleCapsLock_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.CapsLock;
    }
}"),
        new("ConsoleNumberLock_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.NumberLock;
    }
}"),
        new("ConsoleInputEncoding_Diagnostic", @"
using System;
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding {|SP0002:TestMethod|}()
    {
        return Console.InputEncoding;
    }
}"),
        new("ConsoleOutputEncoding_Diagnostic", @"
using System;
using System.Text;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Encoding {|SP0002:TestMethod|}()
    {
        return Console.OutputEncoding;
    }
}"),
        new("ConsoleLargestWindowHeight_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.LargestWindowHeight;
    }
}"),
        new("ConsoleLargestWindowWidth_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.LargestWindowWidth;
    }
}"),
        new("ConsoleTreatControlCAsInput_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.TreatControlCAsInput;
    }
}"),
        new("ConsoleBeep_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.Beep();
    }
}"),
        new("ConsoleBufferHeight_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.BufferHeight;
    }
}"),
        new("ConsoleBufferHeightSet_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int bufferHeight)
    {
        Console.BufferHeight = bufferHeight;
    }
}"),
        new("ConsoleTitle_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Console.Title;
    }
}"),
        new("ConsoleTitleSet_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(string title)
    {
        Console.Title = title;
    }
}"),
        new("ConsoleOpenStandardInput_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardInput();
    }
}"),
        new("ConsoleOpenStandardInputWithBufferSize_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardInput(256);
    }
}"),
        new("ConsoleOpenStandardOutput_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardOutput();
    }
}"),
        new("ConsoleOpenStandardOutputWithBufferSize_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardOutput(256);
    }
}"),
        new("ConsoleOpenStandardError_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardError();
    }
}"),
        new("ConsoleOpenStandardErrorWithBufferSize_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stream {|SP0002:TestMethod|}()
    {
        return Console.OpenStandardError(256);
    }
}"),
        new("ConsoleSetIn_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(TextReader reader)
    {
        Console.SetIn(reader);
    }
}"),
        new("ConsoleSetOut_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(TextWriter writer)
    {
        Console.SetOut(writer);
    }
}"),
        new("ConsoleSetError_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(TextWriter writer)
    {
        Console.SetError(writer);
    }
}"),
    };

    private static IEnumerable<TestCaseData> ConsoleOperationCaseData()
    {
        if (Cases.Length != 42 ||
            Cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 42)
        {
            throw new InvalidOperationException("ConsoleTests case invariants failed.");
        }

        return Cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(ConsoleOperationCaseData))]
    public async Task ConsoleOperationCaseCases(ConsoleOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }










































}
