using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class IOOperationsTests
{
    public sealed record IoOperationCase(string Name, string Source);

    private static readonly IoOperationCase[] IoOperationCasesPart1 =
    {
        new("StringReaderConstructor_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public StringReader {|SP0002:TestMethod|}()
    {
        return new StringReader(""text"");
    }
}"),
        new("StringWriterConstructor_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public StringWriter {|SP0002:TestMethod|}()
    {
        return new StringWriter();
    }
}"),
        new("MemoryStreamToArray_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(MemoryStream stream)
    {
        return stream.ToArray();
    }
}"),
        new("MemoryStreamConstructor_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemoryStream {|SP0002:TestMethod|}()
    {
        return new MemoryStream();
    }
}"),
        new("DirectoryGetFilesLength_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string path)
    {
        return Directory.GetFiles(path).Length;
    }
}"),
        new("StreamFlush_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Stream stream)
    {
        stream.Flush();
    }
}"),
        new("TextReaderReadToEnd_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(TextReader reader)
    {
        return reader.ReadToEnd();
    }
}"),
        new("StreamReaderReadLine_Diagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(StreamReader reader)
    {
        return reader.ReadLine();
    }
}"),
        new("StreamWriterWriteLine_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(StreamWriter writer)
    {
        writer.WriteLine(""line"");
    }
}"),
        new("StringWriterWrite_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(StringWriter writer)
    {
        writer.Write(""text"");
    }
}"),
        new("AsyncAwait_Diagnostic", @"
using System;
using SharpProof.Attributes;
using System.Threading.Tasks;



public class TestClass
{
    [EnforcePure]
    public async Task<int> {|SP0002:TestMethod|}()
    {
        // Task.FromResult now follows reviewed runtime evidence.
        return await Task.FromResult(42);
    }
}"),
        new("ConstantFalseBranch_IgnoresDeadIo", @"
using System;
using SharpProof.Attributes;
using System.IO;



public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        if (false) // Never executed branch
        {
            // Impure operation but in a branch that can never be executed
            File.WriteAllText(""log.txt"", input);
        }

        return input.ToUpperInvariant();
    }
}"),
        new("ConstantFalseConditionalExpression_IgnoresDeadImpureInvocation", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return false ? Console.Read() : 42;
    }
}"),
        new("DirectoryGetCurrentDirectory_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Directory.GetCurrentDirectory();
    }
}"),
        new("DirectorySetCurrentDirectory_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(string path)
    {
        Directory.SetCurrentDirectory(path);
    }
}"),
    };

    private static IEnumerable<TestCaseData> IoOperationCaseData()
    {
        var cases = IoOperationCasesPart1
            .Concat(IoOperationCasesPart2)
            .ToArray();

        if (cases.Length != 29 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 29)
        {
            throw new InvalidOperationException("IoOperation case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(IoOperationCaseData))]
    public async Task IoOperationCases(IoOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }





















    [Test]
    public async Task ClosureOverFieldImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;



public class TestClass
{
    private List<string> _log = new List<string>();

    [EnforcePure]
    // Captured field mutation inside the selector is impure and should be reported.
    public IEnumerable<int> TestMethod(int[] numbers)
    {
        // The closure captures _log field and modifies it
        return numbers.Select(n => {
            _log.Add($""Processing {n}"");
            return n * 2;
        });
    }
}
";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(15, 29, 15, 39)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task ComplexMemberAccess_Diagnostic()
    {
        var test = @"
using System;
using System.IO;
using SharpProof.Attributes;

public class HelperClass 
{ 
    public void DoSomethingImpure() => File.Create(""temp.txt""); 
}

public class TestClass
{
    // This helper method seems pure
    private HelperClass GetHelper() => new HelperClass();

    [EnforcePure]
    public void TestMethod()
    {
        // Accessing instance member of result of method call
        GetHelper().DoSomethingImpure();
    }
}";


        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(17, 17, 17, 27)
            .WithArguments("TestMethod");
        var expectedGetHelper = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(14, 25, 14, 34).WithArguments("GetHelper");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTestMethod, expectedGetHelper);
    }

    [Test]
    public async Task ConditionalImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool condition)
    {
        if (condition)
        {
            // This branch is impure
            Console.WriteLine(""Condition met"");
        }
        else if (DateTime.Now.Hour > 12)
        {
            // This branch has a more hidden impurity (DateTime.Now)
            Console.WriteLine(""Afternoon"");
        }
    }
}";


        var expectedDiagnostic = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedDiagnostic);
    }





    [Test]
    public async Task EnvironmentPathLookup_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        // Path.GetTempPath depends on environment state even though no file IO happens here.
        const string LogPath = ""C:\\logs\\app.log"";
        var tempPath = Path.GetTempPath();
        
        // Just concatenate strings, no actual IO
        return LogPath + "" is different from "" + tempPath;
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(11, 19, 11, 29).WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }





    private static readonly IoOperationCase[] IoOperationCasesPart2 =
    {
        new("PathGetRandomFileName_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Path.GetRandomFileName();
    }
}"),
        new("PathGetTempFileName_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Path.GetTempFileName();
    }
}"),
        new("ContextStaticFieldImpurity_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [ContextStatic]
    private static int _contextCounter;

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return _contextCounter;
    }
}"),
        new("PathGetExtension_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(string path)
    {
        return Path.GetExtension(path);
    }
}"),
        new("PathGetFileNameWithoutExtension_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }
}"),
        new("PathHasExtension_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return Path.HasExtension(path);
    }
}"),
        new("PathHelpers_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? DirectoryNameMethod(string path)
    {
        return Path.GetDirectoryName(path);
    }

    [EnforcePure]
    public string? FileNameMethod(string path)
    {
        return Path.GetFileName(path);
    }

    [EnforcePure]
    public string? ChangeExtensionMethod(string path)
    {
        return Path.ChangeExtension(path, "".txt"");
    }
}"),
        new("DirectoryInfoParent_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo? TestMethod(DirectoryInfo info)
    {
        return info.Parent;
    }
}"),
        new("FileInfoDirectoryName_NoDiagnostic", @"
#nullable enable
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(FileInfo info)
    {
        return info.DirectoryName;
    }
}"),
        new("DirectoryInfoName_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(DirectoryInfo info)
    {
        return info.Name;
    }
}"),
        new("FileInfoName_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(FileInfo info)
    {
        return info.Name;
    }
}"),
        new("FileInfoExtension_Diagnostic", @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(FileInfo info)
    {
        return info.Extension;
    }
}"),
        new("IoClassInheritanceButPureMethods_ShouldBePure", @"
using System;
using SharpProof.Attributes;
using System.IO;



public class PureStreamInfo : FileSystemInfo
{
    public override string Name => ""PureStream"";
    public override bool Exists => false;

    public override void Delete()
    {
        // Impure but never called
        throw new NotImplementedException();
    }
}

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        // Create an object of IO-derived class but only use pure properties
        var info = new PureStreamInfo();
        return info.Name;
    }
}"),
        new("UnusedIoFieldReference_ShouldBePure", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    private StreamReader _reader = null; // Impure type field

    [EnforcePure]
    public int TestMethod(int x)
    {
        var y = _reader; // Read the field, but don't use it in a way that causes impurity
        return x * 2; // Pure operation
    }
}
"),
    };



    [Test]
    public async Task DynamicDispatchImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass 
{
    [EnforcePure]
    public void TestMethod(dynamic d) 
    { 
        d.ImpureCall(); // Simple impure dynamic call 
    } 
}";
        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(8, 17, 8, 27).WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task ExtensionMethodImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public static class Extensions
{
    public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
    {
        foreach (var item in items)
        {
            action(item);
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<string> items)
    {
        // Extension method invokes an impure lambda, so the call is reported.
        items.ForEach(item => Console.WriteLine(item));
    }
}";


        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(22, 17, 22, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTestMethod);
    }

    [Test]
    public async Task ImpureMethodWithConsoleWrite_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace ConsoleApplication1
{
    class TestClass
    {
        [EnforcePure]
        public void TestMethod()
        {
            Console.WriteLine(""Hello, World!"");
        }
    }
}
";


        var expected6 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(10, 21, 10, 31)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected6);
    }

    [Test]
    public async Task ImpureMethodWithFileOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



public class TestClass
{
    [EnforcePure]
    public void TestMethod(string path)
    {
        File.WriteAllText(path, ""test""); // Impure: File I/O
    }
}";


        var expected7 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 17, 11, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected7);
    }

    [Test]
    public async Task IndirectImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        // Impurity is hidden behind multiple layers
        PureWrapper();
    }

    private void PureWrapper()
    {
        Console.WriteLine(""Hello"");
    }
}";


        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTestMethod);
    }

    [Test]
    public async Task StaticFieldAccess_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private static string sharedState = """";
    
    [EnforcePure]
    public string TestMethod()
    {
        // Reading from static field is detected as impure.
        return sharedState;
    }
}";


        var expected9 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(12, 19, 12, 29)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected9);
    }

    [Test]
    public async Task MemoryAllocationImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class TestClass
{
    [EnforcePure]
    public List<int> TestMethod()
    {
        var result = new List<int>(); // Creates a mutable collection
        for (int i = 0; i < 100; i++)
        {
            result.Add(i); // Modifies heap memory
        }
        return result;
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(11, 22, 11, 32).WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task ReflectionImpurity_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Reflection;


public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        // Reflection can get types
        var type = Type.GetType(""System.Console"");
    }
}";

        var expected10 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 17, 11, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected10);
    }

    [Test]
    public async Task UnsafeCodeImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public unsafe void TestMethod(int* ptr)
    {
        // This operation is inherently impure due to pointer manipulation.
        *ptr = 42;
    }
}";


        var expected11 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(10, 24, 10, 34)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected11);
    }

    [Test]
    public async Task LocalFunctionImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        // Local function with impure operation
        int LocalImpure(int x)
        {
            Console.WriteLine(x);
            return x * 2;
        }

        LocalImpure(42);
    }
}";


        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTestMethod);
    }

    [Test]
    public async Task LambdaCapturing_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;



public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] numbers)
    {
        int sum = 0;
        // Lambda that captures and modifies a local variable
        numbers.ToList().ForEach(n => sum += n);
        return sum;
    }
}";


        var expected13 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 16, 11, 26)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected13);
    }

    [Test]
    public async Task IoPathUriManipulation_WithImpureWriteLine_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



public class TestClass
{
    [EnforcePure]
    public string TestMethod(string path)
    {
        // Path helpers are mixed, but Console.WriteLine is the blocking impurity here
        string dir = Path.GetDirectoryName(path);
        string ext = Path.GetExtension(path);
        string fileName = Path.GetFileName(path);

        // Uri manipulation is also pure
        var uri = new Uri(""file://""+ path);
        string scheme = uri.Scheme;

        // Console.WriteLine is impure
        Console.WriteLine(""Testing"");

        return $""{dir}/{fileName} has extension {ext} and scheme {scheme}"";
    }
}";


        var expected14 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 19, 11, 29)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected14);
    }

    [Test]
    public async Task ThreadStaticFieldImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [ThreadStatic]
    private static int _threadCounter;

    [EnforcePure]
    public int TestMethod()
    {
        // Reading/writing thread-static fields is impure
        return _threadCounter++;
    }
}";


        var expected15 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(13, 16, 13, 26)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected15);
    }



    [Test]
    public async Task LazyInitializationImpurity_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private static object _syncRoot = new object();
    private static string _cache;

    [EnforcePure]
    public string TestMethod()
    {
        if (_cache == null)
        {
            lock (_syncRoot) // Lock statement is impure
            {
                if (_cache == null)
                {
                    _cache = ""Initialized""; // Static field modification is impure
                }
            }
        }
        return _cache;
    }
}";


        var expected16 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(13, 19, 13, 29)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected16);
    }

    [Test]
    public async Task IoWrapperPureCombinePaths_ReportsSP0004OnHelper()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



public class IoWrapper
{
    // This class does IO in other methods, but not in this one
    public static string CombinePaths(string path1, string path2)
    {
        return Path.Combine(path1, path2);
    }
}

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string folder, string file)
    {
        // Path.Combine is pure, it just manipulates strings
        return IoWrapper.CombinePaths(folder, file);
    }
}";

        var expectedCombine = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 26, 11, 38).WithArguments("CombinePaths");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedCombine);
    }



















    [Test]
    public async Task UnusedMemoryStreamCreation_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        // Create stream object but don't perform any IO
        var stream = new MemoryStream();
        
        // Just return its type, no actual IO
        stream.GetType();
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(11, 17, 11, 27).WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }



    [Test]
    public async Task MockIoInterface_PureMembersReportSP0004()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public interface IFileSystem
{
    string ReadAllText(string path);
}

public class PureInMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
    
    public string ReadAllText(string path)
    {
        return _files.TryGetValue(path, out var content) ? content : string.Empty;
    }
}

public class TestClass
{
    private readonly IFileSystem _fileSystem;
    
    public TestClass(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }
    
    [EnforcePure]
    public string TestMethod(string path)
    {
        // Using an interface that looks like IO but with pure implementation
        return _fileSystem.ReadAllText(path);
    }
}";

        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(27, 12, 27, 21).WithArguments(".ctor");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedCtor);
    }



    [Test]
    public async Task DirectConsoleWriteLineCall_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod(string message)
    {
        // Direct Console.WriteLine is detected as impure.
        Console.WriteLine(message);
    }
}";


        var expected17 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected17);
    }


    [Test]
    public async Task IoFileWriteAllText_ShouldBeImpure_Test1()
    {
        var test = @"
using System;
using System.IO;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod(string path, string content)
    {
        // File.WriteAllText is impure
        File.WriteAllText(path, content);
    }
}";


        var expected18 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 17, 11, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected18);
    }

    [Test]
    public async Task IoFileReadAllText_ShouldBeImpure_Test1()
    {
        var test = @"
using System;
using System.IO;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public string TestMethod(string path)
    {
        // File.ReadAllText is impure
        return File.ReadAllText(path);
    }
}";


        var expected19 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(11, 19, 11, 29)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected19);
    }

    [Test]
    public async Task IoDirectoryExists_ShouldBeImpure_Test1()
    {
        var test = @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return Directory.Exists(path);
    }
}";

        var expected20 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(8, 17, 8, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected20);
    }

    [Test]
    public async Task IoFileExists_ShouldBeImpure_Test1()
    {
        var test = @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return File.Exists(path);
    }
}";

        var expected21 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(8, 17, 8, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected21);
    }

    [Test]
    public async Task IoDirectoryCreateDirectory_ShouldBeImpure_Test1()
    {
        var test = @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo TestMethod(string path)
    {
        return Directory.CreateDirectory(path);
    }
}";

        var expected22 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(8, 26, 8, 36)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected22);
    }

    [Test]
    public async Task IoDirectoryCreateTempSubdirectory_ShouldBeImpure_Test1()
    {
        var test = @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo TestMethod()
    {
        return Directory.CreateTempSubdirectory(""sharpproof-test-"");
    }
}";

        var expected23 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(8, 26, 8, 36)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected23);
    }
}
