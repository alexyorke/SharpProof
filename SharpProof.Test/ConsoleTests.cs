using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ConsoleTests
{
    [Test]
    public async Task ConsoleOut_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleError_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleIn_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleReadLine_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Console.ReadLine();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWriteString_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.Write(""impure"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWriteLineString_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.WriteLine(""impure"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleBackgroundColor_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConsoleColor {|SP0002:TestMethod|}()
    {
        return Console.BackgroundColor;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleForegroundColor_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConsoleColor {|SP0002:TestMethod|}()
    {
        return Console.ForegroundColor;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleBufferWidth_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.BufferWidth;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWindowWidth_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowWidth;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWindowHeight_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowHeight;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleCursorLeft_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorLeft;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleCursorTop_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorTop;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleKeyAvailable_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.KeyAvailable;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWindowLeft_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowLeft;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleWindowTop_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.WindowTop;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleCursorVisible_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.CursorVisible;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleCursorSize_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.CursorSize;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleIsOutputRedirected_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsOutputRedirected;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleIsInputRedirected_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsInputRedirected;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleIsErrorRedirected_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.IsErrorRedirected;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleCapsLock_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.CapsLock;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleNumberLock_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.NumberLock;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleInputEncoding_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOutputEncoding_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleLargestWindowHeight_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.LargestWindowHeight;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleLargestWindowWidth_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.LargestWindowWidth;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleTreatControlCAsInput_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Console.TreatControlCAsInput;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleBeep_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Console.Beep();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleBufferHeight_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Console.BufferHeight;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleBufferHeightSet_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int bufferHeight)
    {
        Console.BufferHeight = bufferHeight;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleTitle_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Console.Title;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleTitleSet_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(string title)
    {
        Console.Title = title;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardInput_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardInputWithBufferSize_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardOutput_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardOutputWithBufferSize_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardError_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleOpenStandardErrorWithBufferSize_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleSetIn_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleSetOut_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConsoleSetError_Diagnostic()
    {
        var test = @"
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
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}