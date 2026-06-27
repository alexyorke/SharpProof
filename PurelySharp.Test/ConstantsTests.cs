using System;
using System.Collections.Immutable;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ConstantsTests
    {
        private static readonly Lazy<ImmutableArray<AdditionalText>> CheckedInEffectSummaryAdditionalFiles =
            new Lazy<ImmutableArray<AdditionalText>>(CreateCheckedInEffectSummaryAdditionalFiles);

        [Test]
        public void StaticConstructor_DoesNotThrow_WhenInitialized()
        {
            var pureMembers = Constants.KnownPureBCLMembers;
            var impureMethods = Constants.KnownImpureMethods;

            Assert.That(pureMembers, Is.Not.Null, "KnownPureBCLMembers should be loaded.");
            Assert.That(impureMethods, Is.Not.Null, "KnownImpureMethods should be loaded.");

            var overlappingMethods = pureMembers.Intersect(impureMethods).ToList();

            Assert.That(overlappingMethods, Is.Empty,
                $"KnownImpureMethods and KnownPureBCLMembers should not overlap. Found overlaps: {string.Join(", ", overlappingMethods)}");
        }

        [Test]
        public void GuidCatalog_UsesGeneratedEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownImpureTypeNames, Does.Not.Contain("System.Guid"));
            AssertNotInManualCatalogs("System.Guid.NewGuid()");
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.CompareTo(System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Equals(System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Guid(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Guid(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.Parse(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ParseExact(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.TryParse(string?, out System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.TryParseExact(string?, string?, out System.Guid)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToString()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToString(string?)"));
        }

        [Test]
        public void ArrayEmpty_IsSourcedFromGeneratedPurityEvidence_NotTheStaticPureCatalog()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Array.Empty<T>()"));
        }

        [Test]
        public void GuidToByteArray_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToByteArray()"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Guid.ToByteArray(bool)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Guid.ToByteArray()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Guid.ToByteArray(bool)"));
        }

        [Test]
        public void RandomMembers_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownImpureTypeNames, Does.Not.Contain("System.Random"));
            AssertNotInManualCatalogs("System.Random.Shared.get");
            AssertNotInManualCatalogs("System.Random.Next()");
            AssertNotInManualCatalogs("System.Random.Next(int)");
            AssertNotInManualCatalogs("System.Random.NextDouble()");
            AssertNotInManualCatalogs("System.Random.NextInt64()");
            AssertNotInManualCatalogs("System.Random.NextInt64(long)");
            AssertNotInManualCatalogs("System.Random.NextInt64(long, long)");
        }

        [Test]
        public void StringBuilderMutators_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Text.StringBuilder.Append(string?)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Append(char)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Append(object)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.AppendLine(string)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.AppendJoin(string, object[])");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Clear()");
            AssertNotInManualCatalogs("System.Text.StringBuilder.EnsureCapacity(int)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Insert(int, string)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Remove(int, int)");
            AssertNotInManualCatalogs("System.Text.StringBuilder.Replace(string, string)");
        }

        [Test]
        public void ArrayReverseAndManualSortRemainder_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Array.Reverse(System.Array)");
            AssertNotInManualCatalogs("System.Array.Reverse<T>(T[])");
            AssertNotInManualCatalogs("System.Array.Reverse<T>(T[], int, int)");
            AssertNotInManualCatalogs("System.Array.Sort(System.Array)");
            AssertNotInManualCatalogs("System.Array.Sort<T>(T[], System.Comparison<T>)");
        }

        [Test]
        public void ThreadingSynchronizationMembers_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Threading.Monitor.Enter(object)",
                "System.Threading.Monitor.Pulse(object)",
                "System.Threading.Monitor.Wait(object)",
                "System.Threading.Monitor.TryEnter(object)",
                "System.Threading.SemaphoreSlim.Release()",
                "System.Threading.SemaphoreSlim.Wait()",
                "System.Threading.Thread.ManagedThreadId.get",
                "System.Threading.Thread.Sleep(int)",
                "System.Threading.Thread.Sleep(System.TimeSpan)",
                "System.Threading.ReaderWriterLockSlim.EnterReadLock()",
                "System.Threading.ReaderWriterLockSlim.ExitReadLock()",
                "System.Threading.SpinWait.SpinOnce()",
                "System.Threading.Timer.Timer(System.Threading.TimerCallback)",
                "System.Threading.Timer.Change(int, int)",
                "System.Threading.Barrier.SignalAndWait()",
                "System.Threading.CountdownEvent.Signal()",
                "System.Threading.CountdownEvent.Wait()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void GeneratedPathHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.IO.Path.Combine(string, string)",
                "System.IO.Path.HasExtension(string)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void UnresolvedPathWrappers_AreNotBackedByStaticCatalogs()
        {
            var members = new[]
            {
                "System.IO.Path.ChangeExtension(string, string)",
                "System.IO.Path.ChangeExtension(string?, string?)",
                "System.IO.Path.GetDirectoryName(string)",
                "System.IO.Path.GetDirectoryName(string?)",
                "System.IO.Path.GetExtension(string)",
                "System.IO.Path.GetExtension(string?)",
                "System.IO.Path.GetFileName(string)",
                "System.IO.Path.GetFileName(string?)",
                "System.IO.Path.GetFileNameWithoutExtension(string)",
                "System.IO.Path.GetFileNameWithoutExtension(string?)",
                "System.IO.Path.HasExtension(string)",
                "System.IO.Path.HasExtension(string?)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void GuidNewGuidAndPathEnvironmentHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Guid.NewGuid()",
                "System.IO.Path.GetFullPath(string)",
                "System.IO.Path.GetRandomFileName()",
                "System.IO.Path.GetTempFileName()",
                "System.IO.Path.GetTempPath()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnvironmentPathStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class EnvironmentCatalogSignatureSamples
{
    public static string Sample()
    {
        _ = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "EnvironmentCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs("System.Environment.CurrentDirectory.get");
            AssertNotInManualCatalogs("System.Environment.CurrentDirectory.set");
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None)"));
        }

        [Test]
        public void EnvironmentProcessStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Environment.CommandLine.get",
                "System.Environment.ExpandEnvironmentVariables(string)",
                "System.Environment.GetEnvironmentVariable(string)",
                "System.Environment.GetEnvironmentVariable(string, System.EnvironmentVariableTarget)",
                "System.Environment.GetEnvironmentVariables()",
                "System.Environment.GetEnvironmentVariables(System.EnvironmentVariableTarget)",
                "System.Environment.MachineName.get",
                "System.Environment.OSVersion.get",
                "System.Environment.ProcessId.get",
                "System.Environment.ProcessorCount.get",
                "System.Environment.ProcessPath.get",
                "System.Environment.SystemDirectory.get",
                "System.Environment.SystemPageSize.get",
                "System.Environment.UserDomainName.get",
                "System.Environment.UserInteractive.get",
                "System.Environment.UserName.get",
                "System.Environment.Version.get",
                "System.Environment.WorkingSet.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnvironmentVariableMutationHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Environment.SetEnvironmentVariable(string, string)",
                "System.Environment.SetEnvironmentVariable(string, string, System.EnvironmentVariableTarget)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnvironmentVolatileStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Environment.CurrentManagedThreadId.get",
                "System.Environment.ExitCode.get",
                "System.Environment.Exit(int)",
                "System.Environment.TickCount.get",
                "System.Environment.TickCount64.get",
                "System.Environment.StackTrace.get",
                "System.Threading.Thread.CurrentThread.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ProcessHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Diagnostics.Process.GetCurrentProcess()",
                "System.Diagnostics.Process.Id.get",
                "System.Diagnostics.Process.StartInfo.get",
                "System.Diagnostics.Process.Start(string)",
                "System.Diagnostics.Process.GetProcessesByName(string)",
                "System.Diagnostics.Process.ExitCode.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DebugWriteLine_IsNotBackedByAnExactImpureMethodCatalogRow()
        {
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain("System.Diagnostics.Debug.WriteLine(string)"));
        }

        [Test]
        public void CultureAndRegionAmbientStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Globalization.CultureInfo.CurrentCulture.get",
                "System.Globalization.CultureInfo.CurrentUICulture.get",
                "System.Globalization.CultureInfo.DefaultThreadCurrentCulture.get",
                "System.Globalization.CultureInfo.DefaultThreadCurrentUICulture.get",
                "System.Globalization.DateTimeFormatInfo.CurrentInfo.get",
                "System.Globalization.CultureInfo.InstalledUICulture.get",
                "System.Globalization.NumberFormatInfo.CurrentInfo.get",
                "System.Globalization.RegionInfo.CurrentRegion.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DateTimeAndDateTimeOffsetAmbientStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.DateTime.Now.get",
                "System.DateTime.Today.get",
                "System.DateTime.ToLocalTime()",
                "System.DateTime.UtcNow.get",
                "System.DateTimeOffset.Now.get",
                "System.DateTimeOffset.UtcNow.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ConsoleAmbientStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Console.ReadLine()",
                "System.Console.Error.get",
                "System.Console.In.get",
                "System.Console.InputEncoding.get",
                "System.Console.IsErrorRedirected.get",
                "System.Console.IsInputRedirected.get",
                "System.Console.IsOutputRedirected.get",
                "System.Console.Out.get",
                "System.Console.OutputEncoding.get",
                "System.Console.OpenStandardError()",
                "System.Console.OpenStandardInput()",
                "System.Console.OpenStandardOutput()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ConsoleObservableGetterHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Console.BackgroundColor.get",
                "System.Console.BufferHeight.get",
                "System.Console.BufferWidth.get",
                "System.Console.CapsLock.get",
                "System.Console.CursorLeft.get",
                "System.Console.CursorSize.get",
                "System.Console.CursorTop.get",
                "System.Console.CursorVisible.get",
                "System.Console.ForegroundColor.get",
                "System.Console.LargestWindowHeight.get",
                "System.Console.LargestWindowWidth.get",
                "System.Console.NumberLock.get",
                "System.Console.Title.get",
                "System.Console.TreatControlCAsInput.get",
                "System.Console.WindowHeight.get",
                "System.Console.WindowLeft.get",
                "System.Console.WindowTop.get",
                "System.Console.WindowWidth.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ConsoleOutputHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Console.SetError(System.IO.TextWriter)",
                "System.Console.SetOut(System.IO.TextWriter)",
                "System.Console.Write(object)",
                "System.Console.Write(string)",
                "System.Console.WriteLine()",
                "System.Console.WriteLine(object)",
                "System.Console.WriteLine(string)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ConsoleControlHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Console.Beep()",
                "System.Console.Clear()",
                "System.Console.ReadKey()",
                "System.Console.SetCursorPosition(int, int)",
                "System.Console.SetIn(System.IO.TextReader)",
                "System.Console.get_KeyAvailable()",
                "System.Console.set_BufferHeight(int)",
                "System.Console.set_Title(string)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void AbstractDispatchFallbacks_AreNotBackedByManualImpureCatalogEntries()
        {
            AssertNotInManualCatalogs("object.ToString()");
            AssertNotInManualCatalogs("System.IDisposable.Dispose()");
        }

        [Test]
        public void DirectoryCurrentDirectoryHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.IO.Directory.GetCurrentDirectory()");
            AssertNotInManualCatalogs("System.IO.Directory.SetCurrentDirectory(string)");
        }

        [Test]
        public void FileSystemStateHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.IO.Directory.CreateDirectory(string)",
                "System.IO.Directory.CreateTempSubdirectory(string)",
                "System.IO.Directory.Exists(string)",
                "System.IO.File.Exists(string)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void FileSystemPathGetters_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.IO;

public static class FileSystemPathGetterCatalogSignatureSamples
{
    public static string? Sample(DirectoryInfo directory, FileInfo file)
    {
        _ = directory.Parent;
        _ = file.DirectoryName;
        return directory.Name + file.Name + file.Extension;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "FileSystemPathGetterCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "directory.Parent"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "file.DirectoryName"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "directory.Name"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "file.Name"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "file.Extension"));
        }

        [Test]
        public void WebUtilityHelpers_AreNotBackedByStaticCatalogs()
        {
            var members = new[]
            {
                "System.Net.WebUtility.HtmlDecode(string)",
                "System.Net.WebUtility.HtmlEncode(string)",
                "System.Net.WebUtility.UrlDecode(string)",
                "System.Net.WebUtility.UrlEncode(string)",
                "System.Net.WebUtility.UrlDecodeToBytes(byte[], int, int)",
                "System.Net.WebUtility.UrlEncodeToBytes(byte[], int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CryptographyHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)");
        }

        [Test]
        public void ListMutatorsAndCollectionsMarshalAsSpan_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.List<T>.Add(T)",
                "System.Collections.Generic.List<T>.AddRange(System.Collections.Generic.IEnumerable<T>)",
                "System.Collections.Generic.List<T>.Clear()",
                "System.Collections.Generic.List<T>.ForEach(System.Action<T>)",
                "System.Collections.Generic.List<T>.Insert(int, T)",
                "System.Collections.Generic.List<T>.InsertRange(int, System.Collections.Generic.IEnumerable<T>)",
                "System.Collections.Generic.List<T>.Remove(T)",
                "System.Collections.Generic.List<T>.RemoveAll(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.RemoveAt(int)",
                "System.Collections.Generic.List<T>.RemoveRange(int, int)",
                "System.Collections.Generic.List<T>.Reverse()",
                "System.Collections.Generic.List<T>.Sort()",
                "System.Collections.Generic.List<T>.Sort(System.Comparison<T>)",
                "System.Collections.Generic.List<T>.Sort(System.Collections.Generic.IComparer<T>?)",
                "System.Collections.Generic.List<T>.Sort(int, int, System.Collections.Generic.IComparer<T>?)",
                "System.Runtime.InteropServices.CollectionsMarshal.AsSpan<T>(System.Collections.Generic.List<T>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void HashSetMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.HashSet<T>.Add(T)",
                "System.Collections.Generic.HashSet<T>.Clear()",
                "System.Collections.Generic.HashSet<T>.Remove(T)",
                "System.Collections.Generic.HashSet<T>.UnionWith(System.Collections.Generic.IEnumerable<T>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DictionaryMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.Dictionary<TKey, TValue>.Add(TKey, TValue)",
                "System.Collections.Generic.Dictionary<TKey, TValue>.Clear()",
                "System.Collections.Generic.Dictionary<TKey, TValue>.Remove(TKey)",
                "System.Collections.Generic.Dictionary<TKey, TValue>.TryAdd(TKey, TValue)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void LinkedListMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Collections.Generic;

public static class LinkedListMutatorCatalogSignatureSamples
{
    public static void Sample(LinkedList<int> list, LinkedListNode<int> node, int value)
    {
        list.AddFirst(value);
        node.Value = value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinkedListMutatorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.AddFirst(value)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "node.Value", preferSetter: true));
        }

        [Test]
        public void DelegateCombine_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Delegate.Combine(System.Delegate, System.Delegate)");
        }

        [Test]
        public void QueueAndStackHelpers_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.Queue<T>.Clear()",
                "System.Collections.Generic.Queue<T>.ToArray()",
                "System.Collections.Generic.Queue<T>.Dequeue()",
                "System.Collections.Generic.Queue<T>.Enqueue(T)",
                "System.Collections.Generic.Stack<T>.Clear()",
                "System.Collections.Generic.Stack<T>.ToArray()",
                "System.Collections.Generic.Stack<T>.Pop()",
                "System.Collections.Generic.Stack<T>.Push(T)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void PriorityQueueMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Collections.Generic;

public static class PriorityQueueMutatorCatalogSignatureSamples
{
    public static void Sample(PriorityQueue<int, int> queue, int value, int priority)
    {
        queue.Enqueue(value, priority);
        _ = queue.Dequeue();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "PriorityQueueMutatorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Enqueue(value, priority)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Dequeue()"));
        }

        [Test]
        public void ConcurrentQueueMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Collections.Concurrent;

public static class ConcurrentQueueMutatorCatalogSignatureSamples
{
    public static void Sample(ConcurrentQueue<int> queue, int value)
    {
        queue.Enqueue(value);
        _ = queue.TryDequeue(out _);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ConcurrentQueueMutatorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Enqueue(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.TryDequeue(out _)"));
        }

        [Test]
        public void AdditionalConcurrentCollectionMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Collections.Concurrent;

public static class AdditionalConcurrentCollectionMutatorCatalogSignatureSamples
{
    public static void Sample(
        ConcurrentDictionary<int, int> dictionary,
        BlockingCollection<int> blockingCollection,
        ConcurrentBag<int> bag)
    {
        _ = dictionary.TryAdd(1, 2);
        blockingCollection.Add(1);
        _ = blockingCollection.Take();
        bag.Add(1);
        _ = bag.TryTake(out _);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "AdditionalConcurrentCollectionMutatorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "dictionary.TryAdd(1, 2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "blockingCollection.Add(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "blockingCollection.Take()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "bag.Add(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "bag.TryTake(out _)"));
        }

        [Test]
        public void DiagnosticsHelpers_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Diagnostics;
using System.Reflection;

public static class DiagnosticsCatalogSignatureSamples
{
    public static MethodBase? Sample(FileVersionInfo fileVersionInfo, StackFrame stackFrame)
    {
        Debug.Assert(true);
        _ = new DiagnosticListener(""test"");
        _ = fileVersionInfo.FileVersion;
        return stackFrame.GetMethod();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DiagnosticsCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Debug.Assert(true)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new DiagnosticListener(\"test\")"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "fileVersionInfo.FileVersion"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stackFrame.GetMethod()"));
        }

        [Test]
        public void StaticCustomAttributeHelpers_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System;
using System.Reflection;

public static class StaticCustomAttributeCatalogSignatureSamples
{
    public static void Sample(MemberInfo member, Type attributeType)
    {
        _ = Attribute.GetCustomAttribute(member, attributeType);
        _ = Attribute.IsDefined(member, attributeType);
        _ = CustomAttributeData.GetCustomAttributes(member);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StaticCustomAttributeCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Attribute.GetCustomAttribute(member, attributeType)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Attribute.IsDefined(member, attributeType)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "CustomAttributeData.GetCustomAttributes(member)"));
        }

        [Test]
        public void SortedCollectionAndBitArrayMutators_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Collections;
using System.Collections.Generic;

public static class SortedCollectionAndBitArrayMutatorCatalogSignatureSamples
{
    public static void Sample(SortedDictionary<int, string> dictionary, SortedSet<int> set, BitArray bits)
    {
        dictionary.Add(1, ""one"");
        set.Add(1);
        bits.Set(0, true);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SortedCollectionAndBitArrayMutatorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "dictionary.Add(1, \"one\")"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "set.Add(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "bits.Set(0, true)"));
        }

        [Test]
        public void ArrayConvertAllAndComparerSort_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System;
using System.Collections.Generic;

public static class ArrayConvertAllAndComparerSortCatalogSignatureSamples
{
    public static void Sample(int[] values, IComparer<int> comparer)
    {
        _ = Array.ConvertAll(values, static value => value + 1);
        Array.Sort(values, comparer);
        Array.Sort(values, 0, values.Length, comparer);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayConvertAllAndComparerSortCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.ConvertAll(values, static value => value + 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.Sort(values, comparer)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.Sort(values, 0, values.Length, comparer)"));
        }

        [Test]
        public void ArrayCopyWriteHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Array.Clear(System.Array)",
                "System.Array.Clear(System.Array, int, int)",
                "System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)",
                "System.Array.Copy(System.Array, System.Array, int)",
                "System.Array.Copy(System.Array, int, System.Array, int, int)",
                "System.Array.CopyTo(System.Array, int)",
                "System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)",
                "System.Array.Fill<T>(T[], T)",
                "System.Array.Fill<T>(T[], T, int, int)",
                "System.Array.Resize<T>(ref T[], int)",
                "System.Span<T>.Clear()",
                "System.Span<T>.Fill(T)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void Utf8ParserAndCrc32Helpers_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Buffers.Text;
using System.IO.Hashing;

public static class Utf8ParserAndCrc32CatalogSignatureSamples
{
    public static bool Sample(ReadOnlySpan<byte> bytes)
    {
        _ = Utf8Parser.TryParse(bytes, out int value, out int consumed);
        _ = Crc32.Hash(bytes);
        return value >= 0 && consumed >= 0;
    }
}";

            var packageAssemblyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages",
                "system.io.hashing",
                "8.0.0",
                "lib",
                "net8.0",
                "System.IO.Hashing.dll");
            Assert.That(File.Exists(packageAssemblyPath), Is.True, packageAssemblyPath);

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "Utf8ParserAndCrc32GeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences()
                    .Add(MetadataReference.CreateFromFile(packageAssemblyPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Utf8Parser.TryParse(bytes, out int value, out int consumed)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Crc32.Hash(bytes)"));
        }

        [Test]
        public void ArrayPredicateHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class ArrayPredicateCatalogSignatureSamples
{
    public static int Sample(int[] values)
    {
        _ = Array.Exists(values, static value => value > 0);
        _ = Array.Find(values, static value => value > 0);
        _ = Array.TrueForAll(values, static value => value > 0);
        return Array.FindIndex(values, static value => value > 0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayFindCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.Exists(values, static value => value > 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.Find(values, static value => value > 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.FindIndex(values, static value => value > 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.TrueForAll(values, static value => value > 0)"));
        }

        [Test]
        public void ArrayIndexOfAndLengthHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class ArrayIndexOfLengthCatalogSignatureSamples
{
    public static int Sample(Array values, object target)
    {
        return Array.IndexOf(values, target) + values.Length;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayIndexOfLengthCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.IndexOf(values, target)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "values.Length"));
        }

        [Test]
        public void ArrayGetEnumerator_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Collections;

public static class ArrayGetEnumeratorCatalogSignatureSamples
{
    public static IEnumerator Sample(Array values)
    {
        return values.GetEnumerator();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayGetEnumeratorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetInvocationSignature(compilation, syntaxTree, "values.GetEnumerator()");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "values.GetEnumerator()");
            var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(methodSymbol, compilation);

            Assert.That(signature, Is.EqualTo("System.Array.GetEnumerator()"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve System.Array.GetEnumerator() from runtime metadata evidence.");
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public void GenericArrayIndexLookupHelpers_AreNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System;

public static class GenericArrayIndexLookupCatalogSignatureSamples
{
    public static int Sample(int[] values, int target)
    {
        _ = Array.IndexOf(values, target);
        return Array.LastIndexOf(values, target);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GenericArrayIndexLookupCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.IndexOf(values, target)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.LastIndexOf(values, target)"));
        }

        [Test]
        public void AggregateExceptionHelpers_AreNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System;
using System.Collections.Generic;

public static class AggregateExceptionCatalogSignatureSamples
{
    public static AggregateException Sample(IEnumerable<Exception> values, AggregateException aggregate)
    {
        _ = new AggregateException(values);
        return aggregate.Flatten();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "AggregateExceptionCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new AggregateException(values)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "aggregate.Flatten()"));
        }

        [Test]
        public void LinqDeferredEnumerableHelpers_AreNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;

public static class LinqDeferredCatalogSignatureSamples
{
    public static IEnumerable<int> Sample(IEnumerable<int> values)
    {
        _ = values.Distinct();
        _ = values.Reverse();
        return values.TakeWhile(static value => value > 0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinqDeferredCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.Distinct()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.Reverse()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.TakeWhile(static value => value > 0)"));
        }

        [Test]
        public void LinqDeferredEnumerableFactoriesAndAdapters_AreNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;

public static class LinqDeferredFactoryCatalogSignatureSamples
{
    public static IEnumerable<int[]> Sample(object[] values, int[] numbers)
    {
        _ = Enumerable.Empty<int>();
        _ = Enumerable.Range(0, 4);
        _ = Enumerable.Repeat(1, 4);
        _ = values.Cast<int>();
        _ = values.OfType<string>();
        return numbers.Chunk(2);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinqDeferredFactoryCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Enumerable.Empty<int>()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Enumerable.Range(0, 4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Enumerable.Repeat(1, 4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.Cast<int>()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.OfType<string>()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "numbers.Chunk(2)"));
        }

        [Test]
        public void DeadEnumerablePrefixPlaceholders_AreNotPresentInManualPureCatalogs()
        {
            var deadRows = new[]
            {
                "System.Linq.Enumerable.Aggregate",
                "System.Linq.Enumerable.GroupBy",
                "System.Linq.Enumerable.OrderBy",
                "System.Linq.Enumerable.Sum",
                "System.Linq.Enumerable.Average",
                "System.Linq.Enumerable.Max",
                "System.Linq.Enumerable.Min",
                "System.Linq.Enumerable.OrderByDescending",
                "System.Linq.Enumerable.ThenBy",
                "System.Linq.Enumerable.Zip",
            };

            foreach (var deadRow in deadRows)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(deadRow));
            }
        }

        [Test]
        public void ArrayGetLength_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class ArrayGetLengthCatalogSignatureSamples
{
    public static int Sample(Array values)
    {
        return values.GetLength(0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayGetLengthCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.GetLength(0)"));
        }

        [Test]
        public void ArrayBinarySearchHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Collections;

public static class ArrayBinarySearchCatalogSignatureSamples
{
    public static int Sample(Array values, object target, IComparer comparer)
    {
        _ = Array.BinarySearch(values, target);
        return Array.BinarySearch(values, target, comparer);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ArrayBinarySearchCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.BinarySearch(values, target)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Array.BinarySearch(values, target, comparer)"));
        }

        [Test]
        public void SortedSetGetViewBetween_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class SortedSetCatalogSignatureSamples
{
    public static int Sample(SortedSet<int> values, int lower, int upper)
    {
        return values.GetViewBetween(lower, upper).Count;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SortedSetCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.GetViewBetween(lower, upper)"));
        }

        [Test]
        public void SortedListAndLinkedListReadHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class SortedListAndLinkedListCatalogSignatureSamples
{
    public static int Sample(SortedList<int, int> values, int key, LinkedListNode<int> node)
    {
        return values.IndexOfKey(key) + node.Value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SortedListAndLinkedListCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.IndexOfKey(key)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "node.Value"));
        }

        [Test]
        public void SortedDictionaryLookupHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class SortedDictionaryCatalogSignatureSamples
{
    public static bool Sample(SortedDictionary<int, string> values, int key, string target)
    {
        return values.ContainsKey(key) &&
            values.ContainsValue(target) &&
            values.TryGetValue(key, out var resolved) &&
            resolved == target;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SortedDictionaryCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ContainsKey(key)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ContainsValue(target)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.TryGetValue(key, out var resolved)"));
        }

        [Test]
        public void DictionaryAndSortedDictionaryViewGetters_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class CollectionViewCatalogSignatureSamples
{
    public static Dictionary<int, string>.KeyCollection DictionaryKeys(Dictionary<int, string> values) => values.Keys;
    public static Dictionary<int, string>.ValueCollection DictionaryValues(Dictionary<int, string> values) => values.Values;
    public static SortedDictionary<int, string>.KeyCollection SortedKeys(SortedDictionary<int, string> values) => values.Keys;
    public static SortedDictionary<int, string>.ValueCollection SortedValues(SortedDictionary<int, string> values) => values.Values;
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CollectionViewCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var trackedMembers = new (string methodName, string expressionText, string signature)[]
            {
                ("DictionaryKeys", "values.Keys", "System.Collections.Generic.Dictionary<TKey, TValue>.Keys.get"),
                ("DictionaryValues", "values.Values", "System.Collections.Generic.Dictionary<TKey, TValue>.Values.get"),
                ("SortedKeys", "values.Keys", "System.Collections.Generic.SortedDictionary<TKey, TValue>.Keys.get"),
                ("SortedValues", "values.Values", "System.Collections.Generic.SortedDictionary<TKey, TValue>.Values.get"),
            };

            foreach (var trackedMember in trackedMembers)
            {
                var memberAccess = syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node =>
                        node.ToString() == trackedMember.expressionText &&
                        string.Equals(
                            node.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText,
                            trackedMember.methodName,
                            StringComparison.Ordinal));
                var propertySymbol = (IPropertySymbol?)semanticModel.GetSymbolInfo(memberAccess).Symbol;
                Assert.That(propertySymbol, Is.Not.Null, trackedMember.signature);
                var getter = propertySymbol!.GetMethod;
                Assert.That(getter, Is.Not.Null, trackedMember.signature);
                var (matched, classification) = GetGeneratedPurityClassification(getter!, compilation);

                AssertNotInManualCatalogs(trackedMember.signature);
                Assert.That(matched, Is.True, trackedMember.signature);
                Assert.That(classification, Is.EqualTo("impure"), trackedMember.signature);
            }
        }

        [Test]
        public void InterfaceCollectionLookupHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class InterfaceCollectionLookupCatalogSignatureSamples
{
    public static bool Sample(ICollection<int> collection, IList<int> list, int value)
    {
        return collection.Contains(value) && list.IndexOf(value) >= 0 && collection.Count >= 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "InterfaceCollectionLookupCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "collection.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.IndexOf(value)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "collection.Count"));
        }

        [Test]
        public void InterfaceEnumeratorContracts_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class InterfaceEnumeratorCatalogSignatureSamples
{
    public static IEnumerator<int> Enumerate(IEnumerable<int> values)
    {
        return values.GetEnumerator();
    }

    public static int Current(IEnumerator<int> enumerator)
    {
        return enumerator.Current;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "InterfaceEnumeratorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var getEnumeratorSignature = GetInvocationSignature(compilation, syntaxTree, "values.GetEnumerator()");
            var currentSignature = GetPropertySignature(compilation, syntaxTree, "enumerator.Current");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var getEnumeratorMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetEnumerator()"))
                .Symbol!;
            var currentGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "enumerator.Current"))
                .Symbol!).GetMethod!;

            AssertNotInManualCatalogs(getEnumeratorSignature);
            AssertNotInManualCatalogs(currentSignature);

            var (enumeratorMatched, enumeratorClassification) = GetGeneratedPurityClassification(getEnumeratorMethod, compilation);
            Assert.That(enumeratorMatched, Is.True, getEnumeratorSignature);
            Assert.That(enumeratorClassification, Is.EqualTo("conservative_unknown"), getEnumeratorSignature);

            var (currentMatched, currentClassification) = GetGeneratedPurityClassification(currentGetter, compilation);
            Assert.That(currentMatched, Is.True, currentSignature);
            Assert.That(currentClassification, Is.EqualTo("conservative_unknown"), currentSignature);
        }

        [Test]
        public void HashtableCompareInfoAndSortedListHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections;
using System.Globalization;

public static class HashtableCompareInfoAndSortedListCatalogSignatureSamples
{
    public static bool ContainsKey(Hashtable values, object key)
    {
        return values.ContainsKey(key);
    }

    public static int Compare(CompareInfo compareInfo, string left, string right)
    {
        return compareInfo.Compare(left, right);
    }

    public static object GetKey(SortedList values, int index)
    {
        return values.GetKey(index);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "HashtableCompareInfoAndSortedListCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new (string Signature, IMethodSymbol Symbol, string ExpectedClassification)[]
            {
                (
                    GetInvocationSignature(compilation, syntaxTree, "values.ContainsKey(key)"),
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsKey(key)"))
                        .Symbol!,
                    "impure"),
                (
                    GetInvocationSignature(compilation, syntaxTree, "compareInfo.Compare(left, right)"),
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "compareInfo.Compare(left, right)"))
                        .Symbol!,
                    "pure"),
                (
                    GetInvocationSignature(compilation, syntaxTree, "values.GetKey(index)"),
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.GetKey(index)"))
                        .Symbol!,
                    "impure"),
            };

            foreach (var trackedMethod in trackedMethods)
            {
                AssertNotInManualCatalogs(trackedMethod.Signature);
                var (matched, classification) = GetGeneratedPurityClassification(trackedMethod.Symbol, compilation);
                Assert.That(matched, Is.True, trackedMethod.Signature);
                Assert.That(classification, Is.EqualTo(trackedMethod.ExpectedClassification), trackedMethod.Signature);
            }
        }

        [Test]
        public void SortedDictionaryCount_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class SortedDictionaryCountCatalogSignatureSamples
{
    public static int Sample(SortedDictionary<int, string> values)
    {
        return values.Count;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SortedDictionaryCountCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "values.Count"));
        }

        [Test]
        public void KeyedCollectionContains_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.ObjectModel;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public static class KeyedCollectionCatalogSignatureSamples
{
    public static bool Sample(NameCollection values, string key)
    {
        return values.Contains(key);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "KeyedCollectionCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.Contains(key)"));
        }

        [Test]
        public void ListHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.List<T>.Contains(T)",
                "System.Collections.Generic.List<T>.Count.get",
                "System.Collections.Generic.List<T>.Find(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.FindIndex(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.FindLast(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.Exists(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.TrueForAll(System.Predicate<T>)",
                "System.Collections.Generic.List<T>.this[int].get",
                "System.Collections.Generic.List<T>.get_Count()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ImmutableCollectionHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Immutable.ImmutableDictionary.Create<TKey, TValue>()",
                "System.Collections.Immutable.ImmutableList.Create<T>()",
                "System.Collections.Immutable.ImmutableList<T>.Count.get",
                "System.Collections.Immutable.ImmutableList<T>.this[int].get",
                "System.Collections.Immutable.ImmutableHashSet.Create<T>()",
                "System.Collections.Immutable.ImmutableDictionary.CreateRange<TKey, TValue>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>>)",
                "System.Collections.Immutable.ImmutableHashSet.CreateRange<T>(System.Collections.Generic.IEnumerable<T>)",
                "System.Collections.Immutable.ImmutableHashSet.CreateRange<T>(System.Collections.Generic.IEqualityComparer<T>, System.Collections.Generic.IEnumerable<T>)",
                "System.Collections.Immutable.ImmutableHashSet<T>.Count.get",
                "System.Collections.Immutable.ImmutableHashSet<T>.IsEmpty.get",
                "System.Collections.Immutable.ImmutableHashSet<T>.KeyComparer.get",
                "System.Collections.Immutable.ImmutableQueue<T>.Enqueue(T)",
                "System.Collections.Immutable.ImmutableQueue<T>.Dequeue()",
                "System.Collections.Immutable.ImmutableQueue<T>.Clear()",
                "System.Collections.Immutable.ImmutableStack<T>.Clear()",
                "System.Collections.Immutable.ImmutableStack<T>.Pop()",
                "System.Collections.Immutable.ImmutableStack<T>.Push(T)",
                "System.Collections.Immutable.ImmutableStack<T>.IsEmpty.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ImmutableCollectionConcreteHelpers_AreDirectlyBackedByGeneratedCatalogRows()
        {
            const string source = @"
using System.Collections.Immutable;

public static class ImmutableConcreteCatalogSignatureSamples
{
    public static int Count(ImmutableList<int> list) => list.Count;
    public static int First(ImmutableList<int> list) => list[0];
    public static ImmutableDictionary<int, string> DictionaryRange(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int, string>> items) => ImmutableDictionary.CreateRange(items);
    public static ImmutableHashSet<int> SetRange(System.Collections.Generic.IEnumerable<int> values) => ImmutableHashSet.CreateRange(values);
    public static ImmutableQueue<int> Enqueue(ImmutableQueue<int> queue, int value) => queue.Enqueue(value);
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ImmutableConcreteCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var trackedMembers = new (string expressionText, string signature, Func<SyntaxNode, IMethodSymbol?> resolve)[]
            {
                ("list.Count", "System.Collections.Immutable.ImmutableList<T>.Count.get", node => ((IPropertySymbol?)semanticModel.GetSymbolInfo((MemberAccessExpressionSyntax)node).Symbol)?.GetMethod),
                ("list[0]", "System.Collections.Immutable.ImmutableList<T>.this[int].get", node => ((IPropertySymbol?)semanticModel.GetSymbolInfo((ElementAccessExpressionSyntax)node).Symbol)?.GetMethod),
                ("ImmutableDictionary.CreateRange(items)", "System.Collections.Immutable.ImmutableDictionary.CreateRange<TKey, TValue>(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<TKey, TValue>>)", node => semanticModel.GetSymbolInfo((InvocationExpressionSyntax)node).Symbol as IMethodSymbol),
                ("ImmutableHashSet.CreateRange(values)", "System.Collections.Immutable.ImmutableHashSet.CreateRange<T>(System.Collections.Generic.IEnumerable<T>)", node => semanticModel.GetSymbolInfo((InvocationExpressionSyntax)node).Symbol as IMethodSymbol),
                ("queue.Enqueue(value)", "System.Collections.Immutable.ImmutableQueue<T>.Enqueue(T)", node => semanticModel.GetSymbolInfo((InvocationExpressionSyntax)node).Symbol as IMethodSymbol),
            };

            foreach (var trackedMember in trackedMembers)
            {
                var node = syntaxTree.GetRoot()
                    .DescendantNodes()
                    .Single(candidate => candidate.ToString() == trackedMember.expressionText);
                var methodSymbol = trackedMember.resolve(node);
                Assert.That(methodSymbol, Is.Not.Null, trackedMember.signature);
                var (matched, classification) = GetGeneratedPurityClassification(methodSymbol!, compilation);

                AssertNotInManualCatalogs(trackedMember.signature);
                Assert.That(matched, Is.True, trackedMember.signature);
                Assert.That(classification, Is.EqualTo("pure"), trackedMember.signature);
            }
        }

        [Test]
        public void ImmutableCollectionInterfaceHelpers_UseDispatchToReachGeneratedEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Immutable.ImmutableQueue<T>.System.Collections.Immutable.IImmutableQueue<T>.Dequeue()",
                "System.Collections.Immutable.ImmutableStack<T>.System.Collections.Immutable.IImmutableStack<T>.Pop()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ImmutableCollectionInterfaceHelpers_AreNotDirectlyBackedByGeneratedCatalogRows()
        {
            var source = @"
using System.Collections.Immutable;

public static class ImmutableInterfaceCatalogSignatureSamples
{
    public static IImmutableQueue<int> QueueSample(IImmutableQueue<int> queue)
    {
        return queue.Dequeue();
    }

    public static IImmutableStack<int> StackSample(IImmutableStack<int> stack)
    {
        return stack.Pop();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ImmutableInterfaceCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var queueInvocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "queue.Dequeue()");
            var queueMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(queueInvocation).Symbol!;
            var (queueMatched, _) = GetGeneratedPurityClassification(queueMethod, compilation);

            var stackInvocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "stack.Pop()");
            var stackMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(stackInvocation).Symbol!;
            var (stackMatched, _) = GetGeneratedPurityClassification(stackMethod, compilation);

            Assert.That(queueMatched, Is.False, "Interface Dequeue should resolve through dispatch, not direct generated catalog lookup.");
            Assert.That(stackMatched, Is.False, "Interface Pop should resolve through dispatch, not direct generated catalog lookup.");
        }

        [Test]
        public void ImmutableListEqualityHelpers_AreSourcedFromSemanticAnalysis_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Immutable.ImmutableList<T>.Contains(T)",
                "System.Collections.Immutable.ImmutableList<T>.Remove(T)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DictionaryAndImmutableHashSetEqualityHelpers_AreSourcedFromSemanticAnalysis_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.Dictionary<TKey, TValue>.ContainsValue(TValue)",
                "System.Collections.Generic.Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)",
                "System.Collections.Immutable.ImmutableHashSet<T>.Add(T)",
                "System.Collections.Immutable.ImmutableHashSet<T>.Contains(T)",
                "System.Collections.Immutable.ImmutableHashSet<T>.Remove(T)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void LinqToLookupHelpers_AreSourcedFromSemanticAnalysis_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain("System.Linq.Enumerable.ToLookup"));

            var source = @"
using System.Collections.Generic;
using System.Linq;

public static class LinqCatalogSignatureSamples
{
    public static void Sample(IEnumerable<string> values)
    {
        values.ToLookup(value => value.Length);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinqCatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ToLookup(value => value.Length)"));
        }

        [Test]
        public void LinqPureHelpers_AreSourcedFromSemanticAnalysis_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;

public static class LinqPureCatalogSignatureSamples
{
    public static int Sample(IEnumerable<int> values, IEnumerable<int> other)
    {
        _ = values.All(value => value > 0);
        _ = values.Any();
        _ = values.Contains(1);
        _ = values.Count();
        _ = values.ElementAt(0);
        _ = values.First();
        _ = values.FirstOrDefault();
        _ = values.Last();
        _ = values.SequenceEqual(other);
        _ = values.Single();
        _ = values.Skip(1);
        _ = values.Take(2);
        return values.Where(value => value > 0).Select(value => value + 1).Count();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinqPureCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var invocations = new[]
            {
                "values.All(value => value > 0)",
                "values.Any()",
                "values.Contains(1)",
                "values.Count()",
                "values.ElementAt(0)",
                "values.First()",
                "values.FirstOrDefault()",
                "values.Last()",
                "values.SequenceEqual(other)",
                "values.Single()",
                "values.Skip(1)",
                "values.Take(2)",
                "values.Where(value => value > 0)",
                "values.Where(value => value > 0).Select(value => value + 1)",
            };

            foreach (var invocation in invocations)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, invocation));
            }
        }

        [Test]
        public void LinqMaterializationHelpers_AreSourcedFromReturnEscapeAnalysis_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;
using System.Linq;

public static class LinqMaterializationCatalogSignatureSamples
{
    public static void Sample(IEnumerable<string> values)
    {
        values.ToList();
        values.ToHashSet();
        values.ToDictionary(value => value.Length);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "LinqMaterializationCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ToList()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ToHashSet()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ToDictionary(value => value.Length)"));
        }

        [Test]
        public void ListMaterializationHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class ListMaterializationCatalogSignatureSamples
{
    public static void Sample(List<int> values)
    {
        values.FindAll(static value => value > 0);
        values.ConvertAll(static value => value + 1);
        values.ToArray();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ListMaterializationCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.FindAll(static value => value > 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ConvertAll(static value => value + 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "values.ToArray()"));
        }

        [Test]
        public void MutableCollectionReadHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class MutableCollectionCatalogSignatureSamples
{
    public static int Sample(
        Dictionary<string, int> dictionary,
        HashSet<int> set,
        Queue<int> queue,
        Stack<int> stack,
        List<int> list,
        string key,
        int value)
    {
        _ = dictionary.ContainsKey(key);
        _ = dictionary.Count;
        _ = set.Contains(value);
        _ = queue.Contains(value);
        _ = queue.Peek();
        _ = queue.TryPeek(out value);
        _ = stack.Contains(value);
        _ = stack.Peek();
        _ = list.BinarySearch(value);
        _ = list.IndexOf(value);
        return list.LastIndexOf(value);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "MutableCollectionCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "dictionary.ContainsKey(key)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "dictionary.Count"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "set.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.Peek()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "queue.TryPeek(out value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stack.Contains(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stack.Peek()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.BinarySearch(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.IndexOf(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "list.LastIndexOf(value)"));
        }

        [Test]
        public void StaticCacheGetterHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Text;

public static class StaticCacheGetterCatalogSignatureSamples
{
    public static object Sample()
    {
        _ = Comparer<int>.Default;
        _ = EqualityComparer<int>.Default;
        _ = StringComparer.Ordinal;
        _ = StringComparer.OrdinalIgnoreCase;
        _ = Task.CompletedTask;
        _ = Encoding.ASCII;
        return CultureInfo.InvariantCulture;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StaticCacheGetterCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Comparer<int>.Default"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "EqualityComparer<int>.Default"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "StringComparer.Ordinal"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "StringComparer.OrdinalIgnoreCase"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Task.CompletedTask"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Encoding.ASCII"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "CultureInfo.InvariantCulture"));
        }

        [Test]
        public void TaskWrappers_AreNotSourcedFromStaticCatalogs()
        {
            var source = @"
using System.Threading.Tasks;

public static class TaskWrapperCatalogSignatureSamples
{
    public static Task<int> FromResult()
    {
        return Task.FromResult(42);
    }

    public static Task<int> AsTask()
    {
        return new ValueTask<int>(42).AsTask();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "TaskWrapperCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Task.FromResult(42)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "new ValueTask<int>(42).AsTask()"));
        }

        [Test]
        public void TaskSchedulingHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Threading.Tasks;

public static class TaskSchedulingCatalogSignatureSamples
{
    public static Task DelayMilliseconds()
    {
        return Task.Delay(100);
    }

    public static Task DelayTimeSpan()
    {
        return Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    public static Task RunAction()
    {
        return Task.Run(static () => { });
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "TaskSchedulingCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Task.Delay(100)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Task.Delay(TimeSpan.FromMilliseconds(100))"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Task.Run(static () => { })"));
        }

        [Test]
        public void ThreadingStateReaders_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Threading;
using System.Threading.Tasks;

public static class ThreadingStateCatalogSignatureSamples
{
    public static bool IsCanceled(CancellationToken token)
    {
        return token.IsCancellationRequested;
    }

    public static bool IsCompleted(Task task)
    {
        return task.IsCompleted;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ThreadingStateCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "token.IsCancellationRequested"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "task.IsCompleted"));
        }

        [Test]
        public void ValueTaskResultConstructor_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Threading.Tasks;

public static class ValueTaskConstructorCatalogSignatureSamples
{
    public static ValueTask<int> FromResult()
    {
        return new ValueTask<int>(42);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ValueTaskConstructorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new ValueTask<int>(42)");
            var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(objectCreation).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(methodSymbol, compilation);

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ValueTask<int>(42)"));
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve the ValueTask<TResult>(TResult) constructor from runtime metadata evidence.");
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public void CancellationTokenNone_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Threading;

public static class CancellationTokenCatalogSignatureSamples
{
    public static CancellationToken Sample()
    {
        return CancellationToken.None;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CancellationTokenCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "CancellationToken.None"));
        }

        [Test]
        public void DoublePositiveInfinity_IsNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System;

public static class FloatingPointCatalogSignatureSamples
{
    public static double Sample()
    {
        return double.PositiveInfinity;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "FloatingPointCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "double.PositiveInfinity");
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol;

            Assert.That(symbol, Is.AssignableTo<IFieldSymbol>());
            Assert.That(((IFieldSymbol)symbol!).IsConst, Is.True);
            AssertNotInManualCatalogs(symbol.OriginalDefinition.ToDisplayString());
            AssertNotInManualCatalogs("double.PositiveInfinity.get");
        }

        [Test]
        public void NullableComparisonHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class NullableComparisonCatalogSignatureSamples
{
    public static bool Sample(int? left, int? right)
    {
        _ = Nullable.Compare(left, right);
        return Nullable.Equals(left, right);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "NullableComparisonCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Nullable.Compare(left, right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Nullable.Equals(left, right)"));
        }

        [Test]
        public void NullableGetValueOrDefaultHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class NullableGetValueOrDefaultCatalogSignatureSamples
{
    public static int Sample(int? value, int fallback)
    {
        return value.GetValueOrDefault() + value.GetValueOrDefault(fallback);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "NullableGetValueOrDefaultCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.GetValueOrDefault()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.GetValueOrDefault(fallback)"));
        }

        [Test]
        public void ExceptionStateAccessors_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class ExceptionAccessorCatalogSignatureSamples
{
    public static int Sample(Exception error)
    {
        _ = error.Message;
        _ = error.InnerException;
        return error.HResult;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ExceptionAccessorCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "error.Message"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "error.InnerException"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "error.HResult"));
        }

        [Test]
        public void FileNotFoundExceptionStringConstructor_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.IO.FileNotFoundException.FileNotFoundException(string?)");
        }

        [Test]
        public void PureExceptionAndAttributeConstructors_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.ArgumentNullException.ArgumentNullException(string)");
            AssertNotInManualCatalogs("System.ArgumentOutOfRangeException.ArgumentOutOfRangeException(string)");
            AssertNotInManualCatalogs("System.AttributeUsageAttribute.AttributeUsageAttribute(System.AttributeTargets)");
            AssertNotInManualCatalogs("System.BadImageFormatException.BadImageFormatException(string)");
            AssertNotInManualCatalogs("System.ObjectDisposedException.ObjectDisposedException(string)");
        }

        [Test]
        public void ContractHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Diagnostics.Contracts.Contract.Ensures(bool)");
            AssertNotInManualCatalogs("System.Diagnostics.Contracts.Contract.Requires(bool)");
        }

        [Test]
        public void CultureInfoGetCultureInfo_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Globalization.CultureInfo.GetCultureInfo(string)");
        }

        [Test]
        public void TupleArraySegmentAndReferenceEqualsHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("object.ReferenceEquals(object, object)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Tuple.Create"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.ValueTuple.Create"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.ArraySegment<T>.ArraySegment(T[], int, int)"));
        }

        [Test]
        public void PureCoreConstructorsAndValueTypes_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ArgumentException.ArgumentException(string, string)",
                "System.DivideByZeroException.DivideByZeroException()",
                "System.FlagsAttribute.FlagsAttribute()",
                "System.FormatException.FormatException(string)",
                "System.Index.Index(int, bool)",
                "System.IO.EndOfStreamException.EndOfStreamException()",
                "System.InvalidOperationException.InvalidOperationException(string)",
                "System.NotImplementedException.NotImplementedException()",
                "System.NotSupportedException.NotSupportedException(string)",
                "System.ObsoleteAttribute.ObsoleteAttribute(string)",
                "System.OverflowException.OverflowException()",
                "System.PlatformNotSupportedException.PlatformNotSupportedException()",
                "System.Range.Range(System.Index, System.Index)",
                "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute.CallerArgumentExpressionAttribute(string)",
                "System.Runtime.CompilerServices.MethodImplAttribute.MethodImplAttribute(System.Runtime.CompilerServices.MethodImplOptions)",
                "System.Security.AllowPartiallyTrustedCallersAttribute.AllowPartiallyTrustedCallersAttribute()",
                "System.SerializableAttribute.SerializableAttribute()",
                "System.UIntPtr.UIntPtr(uint)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringNullHelpers_AndOrdinalComparerGetter_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.IsNullOrEmpty(string)",
                "string.IsNullOrWhiteSpace(string)",
                "System.StringComparer.Ordinal.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringNullOrWhiteSpaceSpanOverload_IsNotOnNet80Surface_AndNotStaticCataloged()
        {
            var overloads = typeof(string)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, nameof(string.IsNullOrWhiteSpace), StringComparison.Ordinal))
                .Select(method => method.ToString())
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToArray();

            Assert.That(overloads, Is.EqualTo(new[]
            {
                "Boolean IsNullOrWhiteSpace(System.String)",
            }));

            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.IsNullOrWhiteSpace(System.ReadOnlySpan<char>)"));
        }

        [Test]
        public void StringFormattingHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class StringFormattingCatalogSignatureSamples
{
    public static string One(int value)
    {
        return string.Format(""{0:D}"", value);
    }

    public static string Two(int left, int right)
    {
        return string.Format(""{0} {1}"", left, right);
    }

    public static string Three(int first, int second, int third)
    {
        return string.Format(""{0} {1} {2}"", first, second, third);
    }

    public static string Params(int a, int b, int c, int d)
    {
        return string.Format(""{0} {1} {2} {3}"", a, b, c, d);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StringFormattingCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Format(\"{0:D}\", value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Format(\"{0} {1}\", left, right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Format(\"{0} {1} {2}\", first, second, third)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Format(\"{0} {1} {2} {3}\", a, b, c, d)"));
        }

        [Test]
        public void StringLengthAndTrimHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Length.get",
                "string.Trim()",
                "string.TrimEnd()",
                "string.TrimStart()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringEqualsHelpers_AreSourcedFromGeneratedPurityEvidence_AndSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Equals(object)",
                "string.Equals(string)",
                "string.Equals(string, string)",
                "string.Equals(string, System.StringComparison)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringInvariantCasingAndHashCodeHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.GetHashCode()",
                "string.ToLowerInvariant()",
                "string.ToUpperInvariant()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringConcatReplaceSubstringHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Concat(string, string)",
                "string.Concat(params string[])",
                "string.Replace(string, string)",
                "string.Substring(int)",
                "string.Substring(int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringSplitAndArrayJoinHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.String.Split(char)",
                "System.String.Split(char, System.StringSplitOptions)",
                "System.String.Split(params char[])",
                "System.String.Split(char[])",
                "System.String.Split(char[], System.StringSplitOptions)",
                "System.String.Split(string[], System.StringSplitOptions)",
                "System.String.Split(char[], int, System.StringSplitOptions)",
                "System.String.Split(string[], int, System.StringSplitOptions)",
                "System.String.Join(string, string[])",
                "System.String.Join(string, params string[])",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringEnumerableJoinHelpers_AreNotBackedByStaticPureCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class StringJoinCatalogSignatureSamples
{
    public static string Sample(IEnumerable<string> strings, IEnumerable<int> values)
    {
        _ = string.Join("" "", strings);
        return string.Join("","", values);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StringJoinCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Join(\" \", strings)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "string.Join(\",\", values)"));
        }

        [Test]
        public void StringIndexOfCloneCompareToAndToStringHelpers_AreSourcedFromGeneratedPurityEvidence_AndSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Clone()",
                "string.CompareTo(string)",
                "string.IndexOf(char)",
                "string.IndexOf(string)",
                "string.ToString()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringInsertPadLeftAndRemoveHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "string.Insert(int, string)",
                "string.PadLeft(int)",
                "string.Remove(int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringPrefixSuffix_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.StartsWith(System.String)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.StartsWith(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.String.EndsWith(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.StartsWith(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.EndsWith(string)"));
        }

        [Test]
        public void StringContainsHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(char)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(char, System.StringComparison)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.Contains(string, System.StringComparison)"));
        }

        [Test]
        public void EncodingLookupHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.UTF8.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetString(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetBytes(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Encoding.GetEncoding(string)"));
        }

        [Test]
        public void StringBuilderToString_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Text.StringBuilder.ToString()");
        }

        [Test]
        public void StringBuilderLengthAndHttpResponseSuccessStatusCode_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Net.Http.HttpResponseMessage.IsSuccessStatusCode.get",
                "System.Text.StringBuilder.Length.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DateTimeToFileTimeAndMemberwiseClone_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.DateTime.ToFileTime()",
                "object.MemberwiseClone()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void BooleanAndCharToStringHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.ToString()",
                "char.ToString()",
                "char.ToString(char)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void BooleanCompareAndCharClassificationHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.CompareTo(bool)",
                "char.ConvertFromUtf32(int)",
                "char.ConvertToUtf32(char, char)",
                "char.GetNumericValue(char)",
                "char.IsControl(char)",
                "char.IsDigit(char)",
                "char.IsLetter(char)",
                "char.IsLower(char)",
                "char.IsNumber(char)",
                "char.IsPunctuation(char)",
                "char.IsSeparator(char)",
                "char.IsSymbol(char)",
                "char.IsUpper(char)",
                "char.IsWhiteSpace(char)",
                "char.ToLowerInvariant(char)",
                "char.ToUpperInvariant(char)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void IndexAndHashCodeHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.HashCode.ToHashCode()",
                "System.Index.End.get",
                "System.Index.Start.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void SpanAndMemoryMarshalHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Runtime.InteropServices.MemoryMarshal.AsBytes<T>(System.Span<T>)",
                "System.ReadOnlySpan<T>.Length.get",
                "System.ReadOnlySpan<T>.IsEmpty.get",
                "System.ReadOnlySpan<T>.Slice(int, int)",
                "System.Span<T>.Length.get",
                "System.Span<T>.IsEmpty.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void MemorySlice_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class MemorySliceCatalogSignatureSamples
{
    public static Memory<int> Sample(Memory<int> memory)
    {
        return memory.Slice(0, 0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "MemorySliceGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "memory.Slice(0, 0)"));
        }

        [Test]
        public void ReadOnlySequenceHelpersAndSlice_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Buffers.ReadOnlySequence<T>.End.get",
                "System.Buffers.ReadOnlySequence<T>.IsEmpty.get",
                "System.Buffers.ReadOnlySequence<T>.Length.get",
                "System.Buffers.ReadOnlySequence<T>.Start.get",
                "System.Buffers.ReadOnlySequence<T>.Slice(long)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ListCapacity_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Collections.Generic.List<T>.Capacity.get");
            AssertNotInManualCatalogs("System.Collections.Generic.List<T>.Capacity.set");
        }

        [Test]
        public void EmailAddressConstructor_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ComponentModel.DataAnnotations.EmailAddressAttribute.EmailAddressAttribute()",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CoreComponentModelAttributeConstructors_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ComponentModel.BrowsableAttribute.BrowsableAttribute(bool)",
                "System.ComponentModel.DescriptionAttribute.DescriptionAttribute(string)",
                "System.Diagnostics.ConditionalAttribute.ConditionalAttribute(string)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void RegularExpressionAttributeConstructor_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ComponentModel.DataAnnotations.RegularExpressionAttribute.RegularExpressionAttribute(string)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CoreDataAnnotationsConstructors_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.ComponentModel.DataAnnotations.RangeAttribute.RangeAttribute(double, double)",
                "System.ComponentModel.DataAnnotations.RequiredAttribute.RequiredAttribute()",
                "System.ComponentModel.DataAnnotations.StringLengthAttribute.StringLengthAttribute(int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void DecimalNegate_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("decimal.Negate(decimal)");
        }

        [Test]
        public void DecimalComparisonAndConversions_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
public static class DecimalComparisonAndConversionCatalogSignatureSamples
{
    public static int Compare(decimal left, decimal right)
    {
        return decimal.Compare(left, right);
    }

    public static double Convert(decimal value)
    {
        return decimal.ToDouble(value);
    }

    public static int Narrow(decimal value)
    {
        return decimal.ToInt32(value);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DecimalComparisonAndConversionCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var resolutions = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "decimal.Compare(left, right)" ||
                    node.ToString() == "decimal.ToDouble(value)" ||
                    node.ToString() == "decimal.ToInt32(value)")
                .Select(invocation =>
                {
                    var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
                    var (matched, classification) = GetGeneratedPurityClassification(methodSymbol, compilation);
                    return (
                        invocation: invocation.ToString(),
                        matched,
                        classification);
                })
                .ToDictionary(
                    result => result.invocation,
                    result => (result.matched, result.classification),
                    StringComparer.Ordinal);

            Assert.That(resolutions.Count, Is.EqualTo(3));

            AssertNotInManualCatalogs("System.Decimal.Compare(decimal, decimal)");
            AssertNotInManualCatalogs("System.Decimal.ToDouble(decimal)");
            AssertNotInManualCatalogs("System.Decimal.ToInt32(decimal)");

            Assert.That(resolutions["decimal.Compare(left, right)"].matched, Is.True);
            Assert.That(resolutions["decimal.Compare(left, right)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToDouble(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToDouble(value)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToInt32(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToInt32(value)"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public void KeyValuePairCtorAndAccessors_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Collections.Generic;

public static class KeyValuePairCatalogSignatureSamples<TKey, TValue>
{
    public static TValue Sample(KeyValuePair<TKey, TValue> pair, TKey key, TValue value)
    {
        var created = new KeyValuePair<TKey, TValue>(key, value);
        _ = pair.Key;
        return created.Value;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "KeyValuePairCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var ctorSignature = GetObjectCreationSignature(compilation, syntaxTree, "new KeyValuePair<TKey, TValue>(key, value)");
            var keySignature = GetPropertySignature(compilation, syntaxTree, "pair.Key");
            var valueSignature = GetPropertySignature(compilation, syntaxTree, "created.Value");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Signature, IMethodSymbol Symbol)[]
            {
                (
                    ctorSignature,
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == "new KeyValuePair<TKey, TValue>(key, value)"))
                        .Symbol!),
                (
                    keySignature,
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "pair.Key"))
                        .Symbol!).GetMethod!),
                (
                    valueSignature,
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "created.Value"))
                        .Symbol!).GetMethod!),
            };

            foreach (var trackedMember in trackedMembers)
            {
                var (matched, classification) = GetGeneratedPurityClassification(trackedMember.Symbol, compilation);
                AssertNotInManualCatalogs(trackedMember.Signature);
                Assert.That(matched, Is.True, trackedMember.Signature);
                Assert.That(classification, Is.EqualTo("pure"), trackedMember.Signature);
            }
        }

        [Test]
        public void BooleanParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "bool.Parse(string)",
                "bool.TryParse(string, out bool)",
                "bool.TryParse(string?, out bool)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnumTryParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Enum.TryParse<TEnum>(string, out TEnum)",
                "System.Enum.TryParse<TEnum>(string, bool, out TEnum)",
                "System.Enum.TryParse<TEnum>(string?, out TEnum)",
                "System.Enum.TryParse<TEnum>(string?, bool, out TEnum)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnumParseHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Enum.Parse(System.Type, string)");
        }

        [Test]
        public void EnumValueHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Enum.HasFlag(System.Enum)");
            AssertNotInManualCatalogs("System.Enum.ToString()");
        }

        [Test]
        public void HashSetRelationHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Collections.Generic.HashSet<T>.IsSubsetOf(System.Collections.Generic.IEnumerable<T>)");
        }

        [Test]
        public void UriIsWellFormedUriString_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Uri.IsWellFormedUriString(string, System.UriKind)");
        }

        [Test]
        public void UriEscapeAndUnescapeDataString_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Uri.EscapeDataString(string)");
            AssertNotInManualCatalogs("System.Uri.UnescapeDataString(string)");
        }

        [Test]
        public void UriToString_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;

public static class UriCatalogSignatureSamples
{
    public static string Sample(Uri value)
    {
        return value.ToString();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "UriToStringCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.ToString()"));
        }

        [Test]
        public void AppContextImpureHelpers_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.AppContext.BaseDirectory.get");
            AssertNotInManualCatalogs("System.AppContext.GetData(string)");
            AssertNotInManualCatalogs("System.AppContext.SetData(string, object?)");
            AssertNotInManualCatalogs("System.AppContext.TryGetSwitch(string, out bool)");
            AssertNotInManualCatalogs("System.AppContext.SetSwitch(string, bool)");
        }

        [Test]
        public void AppDomainCurrentDomainAndBaseDirectory_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.AppDomain.CurrentDomain.get");
            AssertNotInManualCatalogs("System.AppDomain.BaseDirectory.get");
        }

        [Test]
        public void MonitorExit_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Threading.Monitor.Exit(object)");
        }

        [Test]
        public void SafeHandleDispose_IsSourcedFromNamespaceOrGeneratedEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Runtime.InteropServices.SafeHandle.Dispose()");
        }

        [Test]
        public void AppDomainFriendlyName_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.AppDomain.FriendlyName.get");
        }

        [Test]
        public void ReflectionPathMetadataGetters_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Reflection;

public static class ReflectionPathMetadataCatalogSignatureSamples
{
    public static string Sample(Assembly assembly, Module module)
    {
        var location = assembly.Location;
        var fullyQualifiedName = module.FullyQualifiedName;
        var name = module.Name;
        return location + fullyQualifiedName + name + module.ScopeName;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ReflectionPathMetadataCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new[]
            {
                ("assembly.Location", "System.Reflection.Assembly.Location.get"),
                ("module.FullyQualifiedName", "System.Reflection.Module.FullyQualifiedName.get"),
                ("module.Name", "System.Reflection.Module.Name.get"),
                ("module.ScopeName", "System.Reflection.Module.ScopeName.get"),
            };

            foreach (var trackedMember in trackedMembers)
            {
                var memberAccess = syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == trackedMember.Item1);
                var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
                var signature = GetPropertySignature(compilation, syntaxTree, trackedMember.Item1);
                var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

                Assert.That(signature, Is.EqualTo(trackedMember.Item2));
                AssertNotInManualCatalogs(signature);
                Assert.That(matched, Is.True, trackedMember.Item2);
                Assert.That(classification, Is.EqualTo("impure"), trackedMember.Item2);
            }
        }

        [Test]
        public void StopwatchGetTimestamp_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Diagnostics.Stopwatch.GetTimestamp()");
        }

        [Test]
        public void StopwatchMembers_AreSourcedFromGeneratedEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Diagnostics;

public static class StopwatchCatalogSignatureSamples
{
    public static long Sample(Stopwatch stopwatch)
    {
        stopwatch.Start();
        stopwatch.Stop();
        _ = stopwatch.Elapsed;
        _ = stopwatch.ElapsedMilliseconds;
        _ = stopwatch.IsRunning;
        return stopwatch.ElapsedTicks + new Stopwatch().ElapsedTicks;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StopwatchCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Stopwatch()"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "stopwatch.Elapsed"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "stopwatch.ElapsedMilliseconds"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "stopwatch.ElapsedTicks"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "stopwatch.IsRunning"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stopwatch.Start()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "stopwatch.Stop()"));
        }

        [Test]
        public void StopwatchStaticFields_AreSourcedFromGeneratedStaticConstructorEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Diagnostics.Stopwatch.Frequency");
            AssertNotInManualCatalogs("System.Diagnostics.Stopwatch.IsHighResolution");
        }

        [Test]
        public void OperatingSystemAndApplicationModelPureHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.AppContext.TargetFrameworkName.get",
                "System.AppDomain.Id.get",
                "System.OperatingSystem.IsAndroid()",
                "System.OperatingSystem.IsAndroidVersionAtLeast(int, int, int, int)",
                "System.OperatingSystem.IsBrowser()",
                "System.OperatingSystem.IsFreeBSD()",
                "System.OperatingSystem.IsFreeBSDVersionAtLeast(int, int, int, int)",
                "System.OperatingSystem.IsIOS()",
                "System.OperatingSystem.IsIOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsLinux()",
                "System.OperatingSystem.IsMacCatalyst()",
                "System.OperatingSystem.IsMacCatalystVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsMacOS()",
                "System.OperatingSystem.IsMacOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsOSPlatform(string)",
                "System.OperatingSystem.IsOSPlatformVersionAtLeast(string, int, int, int, int)",
                "System.OperatingSystem.Platform.get",
                "System.OperatingSystem.IsTvOS()",
                "System.OperatingSystem.IsTvOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsWasi()",
                "System.OperatingSystem.IsWatchOS()",
                "System.OperatingSystem.IsWatchOSVersionAtLeast(int, int, int)",
                "System.OperatingSystem.IsWindows()",
                "System.OperatingSystem.IsWindowsVersionAtLeast(int, int, int, int)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EnvironmentStablePureGetters_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Environment.Is64BitOperatingSystem.get",
                "System.Environment.Is64BitProcess.get",
                "System.Environment.HasShutdownStarted.get",
                "System.Environment.NewLine.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void VersionPureMembers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Version.Version(int, int)",
                "System.Version.Version(int, int, int)",
                "System.Version.Version(int, int, int, int)",
                "System.Version.CompareTo(System.Version?)",
                "System.Version.Equals(System.Version?)",
                "System.Version.Major.get",
                "System.Version.Minor.get",
                "System.Version.Build.get",
                "System.Version.Revision.get",
                "System.Version.MajorRevision.get",
                "System.Version.MinorRevision.get",
                "System.Version.GetHashCode()",
                "System.Version.op_Equality(System.Version?, System.Version?)",
                "System.Version.op_Inequality(System.Version?, System.Version?)",
                "System.Version.op_GreaterThan(System.Version?, System.Version?)",
                "System.Version.op_GreaterThanOrEqual(System.Version?, System.Version?)",
                "System.Version.op_LessThan(System.Version?, System.Version?)",
                "System.Version.op_LessThanOrEqual(System.Version?, System.Version?)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TypeGetTypeFromHandle_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)");
        }

        [Test]
        public void TypeEqualsType_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Type.Equals(System.Type)");
        }

        [Test]
        public void TypeEqualsObject_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Type.Equals(object)");
        }

        [Test]
        public void TypeGetHashCode_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Type.GetHashCode()");
        }

        [Test]
        public void AssemblyLoadContextMembers_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Runtime.Loader.AssemblyLoadContext.All.get",
                "System.Runtime.Loader.AssemblyLoadContext.CurrentContextualReflectionContext.get",
                "System.Runtime.Loader.AssemblyLoadContext.Default.get",
                "System.Runtime.Loader.AssemblyLoadContext.EnterContextualReflection()",
                "System.Runtime.Loader.AssemblyLoadContext.EnterContextualReflection(System.Reflection.Assembly?)",
                "System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(System.Reflection.Assembly)",
                "System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyName(System.Reflection.AssemblyName)",
                "System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath(string)",
                "System.Runtime.Loader.AssemblyLoadContext.LoadFromNativeImagePath(string, string?)",
                "System.Runtime.Loader.AssemblyLoadContext.LoadFromStream(System.IO.Stream)",
                "System.Runtime.Loader.AssemblyLoadContext.LoadFromStream(System.IO.Stream, System.IO.Stream?)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ReflectionNamespaceRuntimeMembers_AreSourcedFromNamespaceFallback_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Reflection.Assembly.GetExecutingAssembly()",
                "System.Reflection.Assembly.GetTypes()",
                "System.Reflection.Assembly.Load(string)",
                "System.Reflection.Assembly.LoadFrom(string)",
                "System.Reflection.FieldInfo.SetValue(object, object)",
                "System.Reflection.MethodBase.GetCurrentMethod()",
                "System.Reflection.MethodInfo.Invoke(object, object[])",
                "System.Reflection.PropertyInfo.SetValue(object, object)",
                "System.Reflection.IntrospectionExtensions.GetTypeInfo(System.Type)",
                "System.Reflection.MemberInfo.GetCustomAttributes(bool)",
                "System.Reflection.Module.Assembly.get",
                "System.Reflection.PropertyInfo.PropertyType.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TypeGetTypeAndObjectEquals_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "object.Equals(object)",
                "System.Type.GetType(string)",
                "System.Type.GetType(string, bool)",
                "System.Type.GetType(string, bool, bool)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void InterlockedAndVolatileMembers_AreSourcedFromSemanticRules_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Threading.Interlocked.CompareExchange(ref int, int, int)",
                "System.Threading.Interlocked.CompareExchange(ref long, long, long)",
                "System.Threading.Interlocked.CompareExchange(ref object, object, object)",
                "System.Threading.Interlocked.Increment(ref int)",
                "System.Threading.Interlocked.Increment(ref long)",
                "System.Threading.Interlocked.Decrement(ref int)",
                "System.Threading.Interlocked.Decrement(ref long)",
                "System.Threading.Interlocked.Add(ref int, int)",
                "System.Threading.Interlocked.Add(ref long, long)",
                "System.Threading.Interlocked.Exchange(ref int, int)",
                "System.Threading.Interlocked.Exchange(ref long, long)",
                "System.Threading.Interlocked.Exchange(ref object, object)",
                "System.Threading.Volatile.Write",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringComparerInvocationMethods_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.StringComparer.InvariantCultureIgnoreCase.Compare(string, string)");
            AssertNotInManualCatalogs("System.StringComparer.Ordinal.Equals(string, string)");
        }

        [Test]
        public void FormattableStringInvocationMethods_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.FormattableString.Invariant(System.FormattableString)");
            AssertNotInManualCatalogs("System.FormattableString.Format.get");
            AssertNotInManualCatalogs("System.FormattableString.ToString(System.IFormatProvider)");
        }

        [Test]
        public void ObjectTypeMetadataHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("object.GetType()");
            AssertNotInManualCatalogs("System.Type.ToString()");
        }

        [Test]
        public void TypePureMetadataGetters_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System;
using System.Reflection;

public static class TypeMetadataCatalogSignatureSamples
{
    public static MethodBase? DeclaringMethod(Type type) => type.DeclaringMethod;
    public static Type? DeclaringType(Type type) => type.DeclaringType;
    public static bool IsContextful(Type type) => type.IsContextful;
    public static bool IsGenericType(Type type) => type.IsGenericType;
    public static bool IsGenericTypeDefinition(Type type) => type.IsGenericTypeDefinition;
    public static bool IsGenericParameter(Type type) => type.IsGenericParameter;
    public static bool IsMarshalByRef(Type type) => type.IsMarshalByRef;
    public static MemberTypes MemberType(Type type) => type.MemberType;
    public static Type? ReflectedType(Type type) => type.ReflectedType;
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "TypeMetadataCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new[]
            {
                ("type.DeclaringMethod", "System.Type.DeclaringMethod.get"),
                ("type.DeclaringType", "System.Type.DeclaringType.get"),
                ("type.IsContextful", "System.Type.IsContextful.get"),
                ("type.IsGenericType", "System.Type.IsGenericType.get"),
                ("type.IsGenericTypeDefinition", "System.Type.IsGenericTypeDefinition.get"),
                ("type.IsGenericParameter", "System.Type.IsGenericParameter.get"),
                ("type.IsMarshalByRef", "System.Type.IsMarshalByRef.get"),
                ("type.MemberType", "System.Type.MemberType.get"),
                ("type.ReflectedType", "System.Type.ReflectedType.get"),
            };

            foreach (var trackedMember in trackedMembers)
            {
                var memberAccess = syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == trackedMember.Item1);
                var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
                var signature = GetPropertySignature(compilation, syntaxTree, trackedMember.Item1);
                var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

                Assert.That(signature, Is.EqualTo(trackedMember.Item2));
                AssertNotInManualCatalogs(signature);
                Assert.That(matched, Is.True, trackedMember.Item2);
                Assert.That(classification, Is.EqualTo("pure"), trackedMember.Item2);
            }
        }

        [Test]
        public void MemberInfoName_IsNotManualCataloged_AndRequiresConcreteImplementationEvidence()
        {
            const string source = @"
using System.Reflection;

public static class MemberInfoNameCatalogSignatureSamples
{
    public static string Sample(MemberInfo member)
    {
        return member.Name;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "MemberInfoNameCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetPropertySignature(compilation, syntaxTree, "member.Name");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "member.Name");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

            Assert.That(signature, Is.EqualTo("System.Reflection.MemberInfo.Name.get"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.False,
                "Generated purity catalog should not treat the abstract System.Reflection.MemberInfo.Name.get slot as reviewed runtime evidence without a concrete implementation body.");
            Assert.That(classification, Is.Empty);
        }

        [Test]
        public void CultureInfoName_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Globalization;

public static class CultureInfoNameCatalogSignatureSamples
{
    public static string Sample(CultureInfo culture)
    {
        return culture.Name;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CultureInfoNameCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetPropertySignature(compilation, syntaxTree, "culture.Name");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "culture.Name");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

            Assert.That(signature, Is.EqualTo("System.Globalization.CultureInfo.Name.get"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve System.Globalization.CultureInfo.Name.get from runtime metadata evidence.");
            Assert.That(classification, Is.EqualTo("impure"));
        }

        [Test]
        public void TypeAssemblyGetter_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System;
using System.Reflection;

public static class TypeAssemblyCatalogSignatureSamples
{
    public static Assembly Sample(Type type)
    {
        return type.Assembly;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "TypeAssemblyCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetPropertySignature(compilation, syntaxTree, "type.Assembly");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "type.Assembly");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

            Assert.That(signature, Is.EqualTo("System.Type.Assembly.get"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve System.Type.Assembly.get from runtime metadata evidence.");
            Assert.That(classification, Is.EqualTo("conservative_unknown"));
        }

        [Test]
        public void ArgumentGuardHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var legacyMembers = new[]
            {
                "System.ArgumentNullException.ThrowIfNull(object)",
                "System.ArgumentNullException.ThrowIfNull(object, string)",
                "System.ArgumentException.ThrowIfNullOrEmpty(string)",
                "System.ArgumentException.ThrowIfNullOrWhiteSpace(string)",
                "System.ArgumentOutOfRangeException.ThrowIfNegative<T>(T)",
                "System.ArgumentOutOfRangeException.ThrowIfZero<T>(T)",
                "System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero<T>(T)",
                "System.ArgumentOutOfRangeException.ThrowIfLessThan<T>(T, T)",
                "System.ArgumentOutOfRangeException.ThrowIfLessThanOrEqual<T>(T, T)",
                "System.ArgumentOutOfRangeException.ThrowIfGreaterThan<T>(T, T)",
                "System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual<T>(T, T)",
            };

            foreach (var member in legacyMembers)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member), member);
            }

            var source = @"
using System;

public class GuardSignatureSamples
{
    public void Sample(string text, int number, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfZero(number);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(number, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, 0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GuardHelperCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentNullException.ThrowIfNull(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentException.ThrowIfNullOrEmpty(text)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentException.ThrowIfNullOrWhiteSpace(text)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfNegative(number)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfZero(number)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfLessThan(number, 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(number, 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, 0)"));
        }

        [Test]
        public void DateTimeAndDateTimeOffsetStableMembers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.DateTime.DateTime(long)",
                "System.DateTime.DateTime(int, int, int)",
                "System.DateTime.IsLeapYear(int)",
                "System.DateTime.Day.get",
                "System.DateTime.DayOfWeek.get",
                "System.DateTime.DayOfYear.get",
                "System.DateTime.Hour.get",
                "System.DateTime.Kind.get",
                "System.DateTime.Millisecond.get",
                "System.DateTime.Minute.get",
                "System.DateTime.Month.get",
                "System.DateTime.Second.get",
                "System.DateTime.Ticks.get",
                "System.DateTime.TimeOfDay.get",
                "System.DateTimeOffset.DateTimeOffset(long, System.TimeSpan)",
                "System.DateTimeOffset.DateTime.get",
                "System.DateTimeOffset.Day.get",
                "System.DateTimeOffset.DayOfWeek.get",
                "System.DateTimeOffset.DayOfYear.get",
                "System.DateTimeOffset.Hour.get",
                "System.DateTimeOffset.Millisecond.get",
                "System.DateTimeOffset.Minute.get",
                "System.DateTimeOffset.Month.get",
                "System.DateTimeOffset.Second.get",
                "System.DateTimeOffset.Ticks.get",
                "System.DateTimeOffset.UtcDateTime.get",
                "System.DateTimeOffset.UtcTicks.get",
                "System.DateTimeOffset.Year.get",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TimeProviderAndTimeZoneInfoGlobals_AreSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.TimeProvider.LocalTimeZone.get");
            AssertNotInManualCatalogs("System.TimeProvider.System.get");
            AssertNotInManualCatalogs("System.TimeProvider.TimestampFrequency.get");
            AssertNotInManualCatalogs("System.TimeZoneInfo.Local.get");
            AssertNotInManualCatalogs("System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)");
            AssertNotInManualCatalogs("System.TimeZoneInfo.FindSystemTimeZoneById(string)");
            AssertNotInManualCatalogs("System.TimeZoneInfo.ClearCachedData()");
        }

        [Test]
        public void IPAddressParseHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Net.IPAddress.Parse(string)",
                "System.Net.IPAddress.Parse(System.ReadOnlySpan<char>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void IPAddressIsLoopback_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Net.IPAddress.IsLoopback(System.Net.IPAddress)");
        }

        [Test]
        public void IPEndPointConstructor_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            var source = @"
using System.Net;

public static class IPEndPointCatalogSignatureSamples
{
    public static IPEndPoint Sample(IPAddress address)
    {
        return new IPEndPoint(address, 80);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "IPEndPointCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new IPEndPoint(address, 80)"));
        }

        [Test]
        public void JsonSerializerSerialize_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, System.Text.Json.JsonSerializerOptions?)"));
        }

        [Test]
        public void RegexHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Regex(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.IsMatch(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Match(string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Replace(string, string, string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.IsMatch(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Match(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Text.RegularExpressions.Regex.Replace(string, string)"));
        }

        [Test]
        public void UnsafeUnalignedHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(ref byte)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.ReadUnaligned(void*)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(ref byte, !!0)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.WriteUnaligned(void*, !!0)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.As<TFrom, TTo>(ref TFrom)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Runtime.CompilerServices.Unsafe.SizeOf<T>()"));
        }

        [Test]
        public void BitOperationsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Numerics.BitOperations.LeadingZeroCount(uint)",
                "System.Numerics.BitOperations.LeadingZeroCount(ulong)",
                "System.Numerics.BitOperations.Log2(uint)",
                "System.Numerics.BitOperations.Log2(ulong)",
                "System.Numerics.BitOperations.PopCount(uint)",
                "System.Numerics.BitOperations.PopCount(ulong)",
                "System.Numerics.BitOperations.RotateLeft(uint, int)",
                "System.Numerics.BitOperations.RotateLeft(ulong, int)",
                "System.Numerics.BitOperations.RotateRight(uint, int)",
                "System.Numerics.BitOperations.RotateRight(ulong, int)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(uint)",
                "System.Numerics.BitOperations.RoundUpToPowerOf2(ulong)",
                "System.Numerics.BitOperations.TrailingZeroCount(int)",
                "System.Numerics.BitOperations.TrailingZeroCount(uint)",
                "System.Numerics.BitOperations.TrailingZeroCount(long)",
                "System.Numerics.BitOperations.TrailingZeroCount(ulong)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void NumericsRuntimeCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Numerics.BigInteger.Add(System.Numerics.BigInteger, System.Numerics.BigInteger)",
                "System.Numerics.Complex.Complex(double, double)",
                "System.Numerics.Complex.Abs(System.Numerics.Complex)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void VectorMathCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Numerics.Quaternion.Quaternion(float, float, float, float)",
                "System.Numerics.Vector3.Normalize(System.Numerics.Vector3)",
                "System.Runtime.Intrinsics.X86.Sse.Add(System.Runtime.Intrinsics.Vector128<float>, System.Runtime.Intrinsics.Vector128<float>)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void DrawingPrimitivesCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Drawing.Color.FromArgb(int, int, int, int)",
                "System.Drawing.Point.Point(int, int)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void BitConverterReadHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToInt32(byte[], int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToInt32(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToDouble(byte[], int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.BitConverter.ToDouble(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToInt32(byte[], int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToInt32(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToDouble(byte[], int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.BitConverter.ToDouble(System.ReadOnlySpan<byte>)"));
        }

        [Test]
        public void MathCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Math.Abs",
                "System.Math.Ceiling(decimal)",
                "System.Math.Clamp",
                "System.Math.Max",
                "System.Math.Min",
                "System.Math.Round",
                "System.Math.Truncate(double)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void MathAbsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Math.Abs(System.Decimal)",
                "System.Math.Abs(double)",
                "System.Math.Abs(float)",
                "System.Math.Abs(int)",
                "System.Math.Abs(long)",
                "System.Math.Abs(nint)",
                "System.Math.Abs(sbyte)",
                "System.Math.Abs(short)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
                Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void MathConstants_AreSourcedFromFieldSemantics_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Math.E");
            AssertNotInManualCatalogs("System.Math.PI");
            AssertNotInManualCatalogs("System.Math.Tau");
        }

        [Test]
        public void MathDoubleHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Ceiling(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Floor(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sin(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sqrt(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Truncate(double)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Math.Sign(decimal)"));
        }

        [Test]
        public void ArrayEmpty_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Array.Empty()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Array.Empty()"));
        }

        [Test]
        public void MemoryExtensionsCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.MemoryExtensions.AsSpan(string)",
                "System.MemoryExtensions.AsSpan<T>(T[])",
                "System.MemoryExtensions.SequenceEqual<T>(System.ReadOnlySpan<T>, System.ReadOnlySpan<T>)",
                "System.MemoryExtensions.Trim<T>(System.ReadOnlySpan<T>)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void StringToCharArray_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("string.ToCharArray()"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("string.ToCharArray()"));
        }

        [Test]
        public void ConvertCatalog_IsSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(byte[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.ToHexString(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromBase64String(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromBase64CharArray(char[], int, int)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromHexString(string)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain("System.Convert.FromHexString(System.ReadOnlySpan<char>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[])"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToBase64String(byte[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(byte[])"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(byte[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.ToHexString(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromBase64String(string)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromBase64CharArray(char[], int, int)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromHexString(string)"));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain("System.Convert.FromHexString(System.ReadOnlySpan<char>)"));
        }

        [Test]
        public void ConvertCurrentCultureHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var members = new[]
            {
                "System.Convert.ToByte(object?)",
                "System.Convert.ToByte(string?)",
                "System.Convert.ToDateTime(object?)",
                "System.Convert.ToDateTime(string?)",
                "System.Convert.ToDecimal(object?)",
                "System.Convert.ToDecimal(string?)",
                "System.Convert.ToDouble(object?)",
                "System.Convert.ToDouble(string?)",
                "System.Convert.ToInt16(object?)",
                "System.Convert.ToInt16(string?)",
                "System.Convert.ToInt32(object?)",
                "System.Convert.ToInt32(string?)",
                "System.Convert.ToInt64(object?)",
                "System.Convert.ToInt64(string?)",
                "System.Convert.ToSByte(object?)",
                "System.Convert.ToSByte(string?)",
                "System.Convert.ToSingle(object?)",
                "System.Convert.ToSingle(string?)",
                "System.Convert.ToString(object?)",
                "System.Convert.ToUInt16(object?)",
                "System.Convert.ToUInt16(string?)",
                "System.Convert.ToUInt32(object?)",
                "System.Convert.ToUInt32(string?)",
                "System.Convert.ToUInt64(object?)",
                "System.Convert.ToUInt64(string?)",
            };

            foreach (var member in members)
            {
                Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(member));
            }
        }

        [Test]
        public void ConvertChangeTypeTypeOverload_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.Convert.ChangeType(object, System.Type)");
        }

        [Test]
        public void CurrentCultureNumericParseAndFormatHelpers_AreHandledSemantically_NotStaticCatalogs()
        {
            var members = new[]
            {
                "byte.Parse(string)",
                "byte.TryParse(System.ReadOnlySpan<char>, out byte)",
                "byte.ToString(string?)",
                "byte.ToString()",
                "decimal.Parse(string)",
                "decimal.TryParse(System.ReadOnlySpan<char>, out decimal)",
                "decimal.ToString(string?)",
                "decimal.ToString()",
                "double.Parse(string)",
                "double.TryParse(string, out double)",
                "double.TryParse(System.ReadOnlySpan<char>, out double)",
                "double.ToString(string?)",
                "double.ToString()",
                "float.Parse(string)",
                "float.TryParse(string, out float)",
                "float.TryParse(System.ReadOnlySpan<char>, out float)",
                "float.ToString(string?)",
                "float.ToString()",
                "int.Parse(string)",
                "int.TryParse(string, out int)",
                "int.TryParse(System.ReadOnlySpan<char>, out int)",
                "int.ToString(string?)",
                "int.ToString()",
                "long.Parse(string)",
                "long.TryParse(System.ReadOnlySpan<char>, out long)",
                "long.ToString(string?)",
                "long.ToString()",
                "short.Parse(string)",
                "short.TryParse(string, out short)",
                "short.TryParse(System.ReadOnlySpan<char>, out short)",
                "short.ToString(string?)",
                "short.ToString()",
                "sbyte.Parse(string)",
                "sbyte.TryParse(string, out sbyte)",
                "sbyte.TryParse(System.ReadOnlySpan<char>, out sbyte)",
                "sbyte.ToString(string?)",
                "sbyte.ToString()",
                "ushort.Parse(string)",
                "ushort.TryParse(string, out ushort)",
                "ushort.TryParse(System.ReadOnlySpan<char>, out ushort)",
                "ushort.ToString(string?)",
                "ushort.ToString()",
                "uint.Parse(string)",
                "uint.TryParse(string, out uint)",
                "uint.TryParse(System.ReadOnlySpan<char>, out uint)",
                "uint.ToString(string?)",
                "uint.ToString()",
                "ulong.Parse(string)",
                "ulong.TryParse(string, out ulong)",
                "ulong.TryParse(System.ReadOnlySpan<char>, out ulong)",
                "ulong.ToString(string?)",
                "ulong.ToString()",
                "System.Half.Parse(string)",
                "System.Half.TryParse(string, out System.Half)",
                "System.Half.TryParse(System.ReadOnlySpan<char>, out System.Half)",
                "System.Half.ToString(string?)",
                "System.Half.ToString()",
                "System.Numerics.BigInteger.Parse(string)",
                "System.Numerics.BigInteger.TryParse(string, out System.Numerics.BigInteger)",
                "System.Numerics.BigInteger.TryParse(System.ReadOnlySpan<char>, out System.Numerics.BigInteger)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void CurrentCultureDateAndTimeHelpers_AreNotBackedBySpecificMethodCatalogEntries()
        {
            var members = new[]
            {
                "System.DateOnly.Parse(string)",
                "System.DateOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.DateOnly.ParseExact(string, string)",
                "System.DateOnly.ParseExact(string, string[])",
                "System.DateOnly.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateOnly.ParseExact(System.ReadOnlySpan<char>, string[])",
                "System.DateOnly.ToLongDateString()",
                "System.DateOnly.ToShortDateString()",
                "System.DateOnly.TryParse(string, out System.DateOnly)",
                "System.DateOnly.TryParse(System.ReadOnlySpan<char>, out System.DateOnly)",
                "System.DateOnly.TryParseExact(string, string, out System.DateOnly)",
                "System.DateOnly.TryParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, out System.DateOnly)",
                "System.DateOnly.TryParseExact(string, string[], out System.DateOnly)",
                "System.DateOnly.TryParseExact(System.ReadOnlySpan<char>, string?[]?, out System.DateOnly)",
                "System.DateOnly.ToString(string?)",
                "System.DateOnly.ToString()",
                "System.DateTime.Parse(string)",
                "System.DateTime.TryParse(string, out System.DateTime)",
                "System.DateTime.TryParse(System.ReadOnlySpan<char>, out System.DateTime)",
                "System.DateTime.ToLongDateString()",
                "System.DateTime.ToLongTimeString()",
                "System.DateTime.ToShortDateString()",
                "System.DateTime.ToShortTimeString()",
                "System.DateTime.ToString(string)",
                "System.DateTime.ToString()",
                "System.DateTimeOffset.Parse(string)",
                "System.DateTimeOffset.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.DateTimeOffset.TryParse(string, out System.DateTimeOffset)",
                "System.DateTimeOffset.TryParse(System.ReadOnlySpan<char>, out System.DateTimeOffset)",
                "System.DateTimeOffset.ToString(string?)",
                "System.DateTimeOffset.ToString()",
                "System.TimeOnly.Parse(string)",
                "System.TimeOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.TimeOnly.ParseExact(string, string)",
                "System.TimeOnly.ParseExact(string, string[])",
                "System.TimeOnly.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.TimeOnly.ParseExact(System.ReadOnlySpan<char>, string[])",
                "System.TimeOnly.ToLongTimeString()",
                "System.TimeOnly.ToShortTimeString()",
                "System.TimeOnly.ToString(string?)",
                "System.TimeOnly.ToString()",
                "System.TimeOnly.TryParse(string, out System.TimeOnly)",
                "System.TimeOnly.TryParse(System.ReadOnlySpan<char>, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(string, string, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(string, string[], out System.TimeOnly)",
                "System.TimeOnly.TryParseExact(System.ReadOnlySpan<char>, string?[]?, out System.TimeOnly)",
                "System.TimeSpan.Parse(string)",
                "System.TimeSpan.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?)",
                "System.TimeSpan.ToString()",
                "System.TimeSpan.TryParse(string, out System.TimeSpan)",
                "System.TimeSpan.TryParse(System.ReadOnlySpan<char>, out System.TimeSpan)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ProviderBasedDateAndTimeParseHelpers_AreHandledSemantically_NotStaticCatalogEntries()
        {
            var members = new[]
            {
                "System.DateOnly.Parse(string, System.IFormatProvider?)",
                "System.DateOnly.Parse(string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateOnly.ParseExact(string, string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTime.TryParseExact(string, string, System.IFormatProvider, System.Globalization.DateTimeStyles, out System.DateTime)",
                "System.DateTime.TryParseExact(string, string[], System.IFormatProvider, System.Globalization.DateTimeStyles, out System.DateTime)",
                "System.DateTimeOffset.Parse(string, System.IFormatProvider?)",
                "System.DateTimeOffset.Parse(string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.ParseExact(string, string, System.IFormatProvider?)",
                "System.DateTimeOffset.ParseExact(string, string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.ParseExact(string, string[], System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.ParseExact(System.ReadOnlySpan<char>, string[], System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.DateTimeOffset.TryParseExact(string, string, System.IFormatProvider, System.Globalization.DateTimeStyles, out System.DateTimeOffset)",
                "System.DateTimeOffset.TryParseExact(string?, string?[]?, System.IFormatProvider?, System.Globalization.DateTimeStyles, out System.DateTimeOffset)",
                "System.DateTimeOffset.TryParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles, out System.DateTimeOffset)",
                "System.DateTimeOffset.TryParseExact(System.ReadOnlySpan<char>, string?[]?, System.IFormatProvider?, System.Globalization.DateTimeStyles, out System.DateTimeOffset)",
                "System.TimeOnly.Parse(string, System.IFormatProvider?)",
                "System.TimeOnly.Parse(string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.TimeOnly.Parse(System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.TimeOnly.ParseExact(string, string, System.IFormatProvider?, System.Globalization.DateTimeStyles)",
                "System.TimeSpan.Parse(string, System.IFormatProvider?)",
                "System.TimeSpan.ParseExact(string, string, System.IFormatProvider?)",
                "System.TimeSpan.ParseExact(string, string, System.IFormatProvider?, System.Globalization.TimeSpanStyles)",
                "System.TimeSpan.ParseExact(System.ReadOnlySpan<char>, System.ReadOnlySpan<char>, System.IFormatProvider?, System.Globalization.TimeSpanStyles)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void TimeSpanComparisonAndFactoryHelpers_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            AssertNotInManualCatalogs("System.TimeSpan.CompareTo(System.TimeSpan)");
            AssertNotInManualCatalogs("System.TimeSpan.FromDays(double)");
        }

        [Test]
        public void FieldBackedStaticBclValues_AreHandledSemantically_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;

public static class StaticFieldSamples
{
    public static object Sample()
    {
        _ = Guid.Empty;
        _ = TimeSpan.Zero;
        _ = EventArgs.Empty;
        _ = DBNull.Value;
        _ = IPAddress.Any;
        _ = IPAddress.Loopback;
        _ = HttpVersion.Version11;
        return Missing.Value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StaticFieldCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Guid.Empty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "TimeSpan.Zero"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "EventArgs.Empty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "DBNull.Value"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "IPAddress.Any"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "HttpVersion.Version11"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Missing.Value"));
        }

        [Test]
        public void RepresentativeCatalogSignaturesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public static class CatalogSignatureSamples
{
    public static int Sample()
    {
        var list = new List<int>();
        var names = new NameCollection();
        var values = new[] { 1, 2, 3 };
        object leftObject = new object();
        object rightObject = new object();
        list.Add(1);
        _ = IPAddress.Loopback;
        _ = names.Contains(""alpha"");
        _ = new FileNotFoundException(""missing.txt"");
        ReadOnlySpan<char> chars = ""alpha"".AsSpan();
        byte[] bytes = new byte[1];
        ReadOnlySpan<byte> spanBytes = bytes;
        ReadOnlySpan<byte> left = stackalloc byte[1];
        ReadOnlySpan<byte> right = stackalloc byte[1];
        _ = new string(chars);
        _ = SHA1.HashData(bytes);
        _ = SHA256.HashData(bytes);
        _ = SHA384.HashData(bytes);
        _ = SHA512.HashData(bytes);
        _ = MD5.HashData(bytes);
        _ = SHA1.HashData(spanBytes);
        _ = SHA256.HashData(spanBytes);
        _ = SHA384.HashData(spanBytes);
        _ = SHA512.HashData(spanBytes);
        _ = MD5.HashData(spanBytes);
        _ = CryptographicOperations.FixedTimeEquals(left, right);
        _ = new ArgumentOutOfRangeException(""value"");
        _ = new BadImageFormatException(""bad image"");
        _ = new AttributeUsageAttribute(AttributeTargets.Method);
        _ = object.ReferenceEquals(leftObject, rightObject);
        _ = new ArraySegment<int>(values);
        _ = new ArraySegment<int>(values, 0, 1);
        _ = Tuple.Create(1, 2);
        _ = ValueTuple.Create(1, 2);
        _ = new DivideByZeroException();
        _ = new InvalidOperationException(""bad operation"");
        _ = new ObsoleteAttribute(""legacy"");
        _ = new Index(2, false);
        _ = new Range(new Index(0, false), new Index(1, false));
        _ = new UIntPtr(1u);
        _ = new CallerArgumentExpressionAttribute(""value"");
        _ = new MethodImplAttribute(MethodImplOptions.AggressiveInlining);
        IntPtr ptr = IntPtr.Zero;
        _ = System.Runtime.InteropServices.Marshal.PtrToStructure<int>(ptr);
        return Array.Empty<int>().Length + list.Count + values.Length;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)"), Is.EqualTo("System.Collections.Generic.List<T>.Add(T)"));
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"), Is.EqualTo("System.Net.IPAddress.Loopback.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback")));

            Assert.That(GetPropertySignature(compilation, syntaxTree, "list.Count"), Is.EqualTo("System.Collections.Generic.List<T>.Count.get"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetPropertySignature(compilation, syntaxTree, "list.Count")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")"), Is.EqualTo("System.Collections.ObjectModel.KeyedCollection<TKey, TItem>.Contains(TKey)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "names.Contains(\"alpha\")")));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")"), Is.EqualTo("System.IO.FileNotFoundException.FileNotFoundException(string?)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetObjectCreationSignature(compilation, syntaxTree, "new FileNotFoundException(\"missing.txt\")")));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new string(chars)"), Is.EqualTo("string.String(System.ReadOnlySpan<char>)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new string(chars)"));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "System.Runtime.InteropServices.Marshal.PtrToStructure<int>(ptr)"), Is.EqualTo("System.Runtime.InteropServices.Marshal.PtrToStructure<T>(nint)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "System.Runtime.InteropServices.Marshal.PtrToStructure<int>(ptr)"));
            AssertNotInManualCatalogs("System.Security.Claims.ClaimsPrincipal.IsInRole(string)");
            AssertNotInManualCatalogs("System.Reflection.FieldInfo.GetValue(object)");
            AssertNotInManualCatalogs("System.Reflection.PropertyInfo.GetValue(object)");

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)"), Is.EqualTo("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "CryptographicOperations.FixedTimeEquals(left, right)"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new ArgumentOutOfRangeException(\"value\")"), Is.EqualTo("System.ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArgumentOutOfRangeException(\"value\")"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new BadImageFormatException(\"bad image\")"), Is.EqualTo("System.BadImageFormatException.BadImageFormatException(string?)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new BadImageFormatException(\"bad image\")"));

            Assert.That(GetObjectCreationSignature(compilation, syntaxTree, "new AttributeUsageAttribute(AttributeTargets.Method)"), Is.EqualTo("System.AttributeUsageAttribute.AttributeUsageAttribute(System.AttributeTargets)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new AttributeUsageAttribute(AttributeTargets.Method)"));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "object.ReferenceEquals(leftObject, rightObject)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArraySegment<int>(values)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ArraySegment<int>(values, 0, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Tuple.Create(1, 2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "ValueTuple.Create(1, 2)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new DivideByZeroException()"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new InvalidOperationException(\"bad operation\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ObsoleteAttribute(\"legacy\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Index(2, false)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Range(new Index(0, false), new Index(1, false))"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new UIntPtr(1u)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new CallerArgumentExpressionAttribute(\"value\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new MethodImplAttribute(MethodImplOptions.AggressiveInlining)"));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA1.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA256.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.MD5.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA384.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)"), Is.EqualTo("System.Security.Cryptography.SHA512.HashData(byte[])"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(bytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA1.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA1.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA256.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.MD5.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "MD5.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA384.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA384.HashData(spanBytes)")));

            Assert.That(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)"), Is.EqualTo("System.Security.Cryptography.SHA512.HashData(System.ReadOnlySpan<byte>)"));
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)")));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(GetInvocationSignature(compilation, syntaxTree, "SHA512.HashData(spanBytes)")));
        }

        [Test]
        public void SymbolResolvedCatalogSamples_DoNotConflictBetweenPureAndImpureCatalogs()
        {
            var source = @"
using System;
using System.Collections.Generic;
using System.Net;

public static class CatalogConflictSamples
{
    public static void Sample()
    {
        var list = new List<int>();
        list.Add(1);
        _ = list.Count;
        _ = Array.Empty<int>();
        _ = IPAddress.Loopback;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CatalogConflictResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertCatalogMembership(GetInvocationSignature(compilation, syntaxTree, "list.Add(1)"), expectedPure: false, expectedImpure: false);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "IPAddress.Loopback"), expectedPure: false, expectedImpure: false);
            AssertCatalogMembership(GetPropertySignature(compilation, syntaxTree, "list.Count"), expectedPure: false, expectedImpure: false);
        }

        [Test]
        public void RecentGuidAndDateTimeOffsetGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Buffers.Binary;
using System.Numerics;

public static class RecentCatalogSignatureSamples
{
    public static long Sample(Guid guid, string text, DateTimeOffset value)
    {
        _ = Guid.ParseExact(text, ""D"");
        _ = Guid.TryParse(text, out var parsed);
        _ = Guid.TryParseExact(text, ""D"", out parsed);
        _ = guid.Equals(Guid.Empty);
        _ = guid.CompareTo(Guid.Empty);
        _ = guid.ToString(""N"");
        var chars = text.ToCharArray();
        ReadOnlySpan<char> charSpan = text.AsSpan();
        _ = new string(charSpan);
        _ = guid.ToByteArray();
        _ = guid.ToByteArray(true);
        _ = BitConverter.GetBytes(true);
        _ = BitConverter.GetBytes('a');
        _ = BitConverter.GetBytes((Half)1);
        _ = BitConverter.GetBytes((short)1);
        _ = BitConverter.GetBytes(1);
        _ = BitConverter.GetBytes(1f);
        _ = BitConverter.GetBytes(1L);
        _ = BitConverter.GetBytes((ushort)1);
        _ = BitConverter.GetBytes(1u);
        _ = BitConverter.GetBytes(1ul);
        _ = chars;
        _ = BitOperations.IsPow2(1);
        _ = BitOperations.IsPow2(1u);
        _ = BitOperations.IsPow2(1L);
        _ = BitOperations.IsPow2(1ul);
        _ = BitOperations.IsPow2((nint)1);
        _ = BitOperations.IsPow2((nuint)1);
        _ = BitOperations.LeadingZeroCount(1u);
        _ = BitOperations.LeadingZeroCount(1ul);
        _ = BitOperations.Log2(1u);
        _ = BitOperations.Log2(1ul);
        _ = BitOperations.PopCount(1u);
        _ = BitOperations.PopCount(1ul);
        _ = BitOperations.PopCount((nuint)1);
        _ = BitOperations.RotateLeft(1u, 1);
        _ = BitOperations.RotateLeft(1ul, 1);
        _ = BitOperations.RotateLeft((nuint)1, 1);
        _ = BitOperations.RotateRight(1u, 1);
        _ = BitOperations.RotateRight(1ul, 1);
        _ = BitOperations.RotateRight((nuint)1, 1);
        _ = BitOperations.RoundUpToPowerOf2(1u);
        _ = BitOperations.RoundUpToPowerOf2(1ul);
        _ = BitOperations.TrailingZeroCount(1);
        _ = BitOperations.TrailingZeroCount(1u);
        _ = BitOperations.TrailingZeroCount(1L);
        _ = BitOperations.TrailingZeroCount(1ul);
        ReadOnlySpan<byte> bytes = stackalloc byte[8];
        _ = BinaryPrimitives.ReadInt16BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt16LittleEndian(bytes);
        _ = BinaryPrimitives.ReadInt32BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        _ = BinaryPrimitives.ReadInt64BigEndian(bytes);
        _ = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        _ = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        _ = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        _ = BinaryPrimitives.ReverseEndianness((sbyte)1);
        _ = BinaryPrimitives.ReverseEndianness((byte)1);
        _ = BinaryPrimitives.ReverseEndianness((short)1);
        _ = BinaryPrimitives.ReverseEndianness((ushort)1);
        _ = BinaryPrimitives.ReverseEndianness('a');
        _ = BinaryPrimitives.ReverseEndianness(1);
        _ = BinaryPrimitives.ReverseEndianness(1u);
        _ = BinaryPrimitives.ReverseEndianness(1L);
        _ = BinaryPrimitives.ReverseEndianness(1ul);
        _ = BinaryPrimitives.ReverseEndianness((nint)1);
        _ = BinaryPrimitives.ReverseEndianness((nuint)1);
        _ = BinaryPrimitives.ReverseEndianness((Int128)1);
        _ = BinaryPrimitives.ReverseEndianness((UInt128)1);
        var fromMilliseconds = DateTimeOffset.FromUnixTimeMilliseconds(0);
        var fromSeconds = DateTimeOffset.FromUnixTimeSeconds(0);
        var add = value.Add(TimeSpan.FromHours(1));
        var addHours = value.AddHours(2);
        var addMilliseconds = value.AddMilliseconds(3);
        var addMinutes = value.AddMinutes(4);
        var addMonths = value.AddMonths(5);
        var addSeconds = value.AddSeconds(6);
        var addTicks = value.AddTicks(7);
        var addYears = value.AddYears(8);
        var added = value.AddDays(1);
        _ = DateTimeOffset.Compare(value, added);
        _ = value.CompareTo(added);
        _ = value.Equals(added);
        _ = DateTimeOffset.Equals(value, added);
        _ = added.Subtract(value);
        var seconds = added.ToUnixTimeSeconds();
        return added.ToUnixTimeMilliseconds() + seconds + value.Offset.Ticks;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "RecentCatalogSignatureResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.ParseExact(text, \"D\")"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParse(text, out var parsed)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Guid.TryParseExact(text, \"D\", out parsed)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.Equals(Guid.Empty)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.CompareTo(Guid.Empty)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "guid.ToString(\"N\")"));
            AssertCatalogMembership(GetObjectCreationSignature(compilation, syntaxTree, "new string(charSpan)"), expectedPure: false, expectedImpure: false);
            var bitConverterGetBytesExpressions = new[]
            {
                "BitConverter.GetBytes(true)",
                "BitConverter.GetBytes('a')",
                "BitConverter.GetBytes((Half)1)",
                "BitConverter.GetBytes((short)1)",
                "BitConverter.GetBytes(1)",
                "BitConverter.GetBytes(1f)",
                "BitConverter.GetBytes(1L)",
                "BitConverter.GetBytes((ushort)1)",
                "BitConverter.GetBytes(1u)",
                "BitConverter.GetBytes(1ul)",
            };

            foreach (var expression in bitConverterGetBytesExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1L)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2((nint)1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.IsPow2((nuint)1)"));
            var binaryPrimitivesReadExpressions = new[]
            {
                "BinaryPrimitives.ReadInt16BigEndian(bytes)",
                "BinaryPrimitives.ReadInt16LittleEndian(bytes)",
                "BinaryPrimitives.ReadInt32BigEndian(bytes)",
                "BinaryPrimitives.ReadInt32LittleEndian(bytes)",
                "BinaryPrimitives.ReadInt64BigEndian(bytes)",
                "BinaryPrimitives.ReadInt64LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt16BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt16LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt32BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt32LittleEndian(bytes)",
                "BinaryPrimitives.ReadUInt64BigEndian(bytes)",
                "BinaryPrimitives.ReadUInt64LittleEndian(bytes)",
            };

            foreach (var expression in binaryPrimitivesReadExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.LeadingZeroCount(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.Log2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.PopCount((nuint)1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1u, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft(1ul, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateLeft((nuint)1, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1u, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight(1ul, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RotateRight((nuint)1, 1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.RoundUpToPowerOf2(1ul)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1u)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1L)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "BitOperations.TrailingZeroCount(1ul)"));
            var binaryPrimitivesReverseEndiannessExpressions = new[]
            {
                "BinaryPrimitives.ReverseEndianness((sbyte)1)",
                "BinaryPrimitives.ReverseEndianness((byte)1)",
                "BinaryPrimitives.ReverseEndianness((short)1)",
                "BinaryPrimitives.ReverseEndianness((ushort)1)",
                "BinaryPrimitives.ReverseEndianness('a')",
                "BinaryPrimitives.ReverseEndianness(1)",
                "BinaryPrimitives.ReverseEndianness(1u)",
                "BinaryPrimitives.ReverseEndianness(1L)",
                "BinaryPrimitives.ReverseEndianness(1ul)",
                "BinaryPrimitives.ReverseEndianness((nint)1)",
                "BinaryPrimitives.ReverseEndianness((nuint)1)",
                "BinaryPrimitives.ReverseEndianness((Int128)1)",
                "BinaryPrimitives.ReverseEndianness((UInt128)1)",
            };

            foreach (var expression in binaryPrimitivesReverseEndiannessExpressions)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, expression));
            }

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.FromUnixTimeMilliseconds(0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.FromUnixTimeSeconds(0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Add(TimeSpan.FromHours(1))"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddHours(2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMilliseconds(3)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMinutes(4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMonths(5)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddSeconds(6)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddTicks(7)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddYears(8)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddDays(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.Compare(value, added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.CompareTo(added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Equals(added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTimeOffset.Equals(value, added)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.Subtract(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.ToUnixTimeMilliseconds()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "added.ToUnixTimeSeconds()"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Offset"));
        }

        [Test]
        public void BinaryPrimitivesWriteFamily_AreSourcedFromGeneratedPurityEvidence_NotStaticCatalogs()
        {
            var source = @"
using System;
using System.Buffers.Binary;

public static class BinaryPrimitivesWriteCatalogSignatureSamples
{
    public static void Sample(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt16BigEndian(destination, 1);
        BinaryPrimitives.WriteInt16LittleEndian(destination, 1);
        BinaryPrimitives.WriteInt32BigEndian(destination, 1);
        BinaryPrimitives.WriteInt32LittleEndian(destination, 1);
        BinaryPrimitives.WriteInt64BigEndian(destination, 1L);
        BinaryPrimitives.WriteInt64LittleEndian(destination, 1L);
        BinaryPrimitives.WriteInt128BigEndian(destination, (Int128)1);
        BinaryPrimitives.WriteInt128LittleEndian(destination, (Int128)1);
        BinaryPrimitives.WriteIntPtrBigEndian(destination, (nint)1);
        BinaryPrimitives.WriteIntPtrLittleEndian(destination, (nint)1);
        BinaryPrimitives.WriteUInt16BigEndian(destination, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, 1);
        BinaryPrimitives.WriteUInt32BigEndian(destination, 1U);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, 1U);
        BinaryPrimitives.WriteSingleBigEndian(destination, 1.0f);
        BinaryPrimitives.WriteSingleLittleEndian(destination, 1.0f);
        BinaryPrimitives.WriteDoubleBigEndian(destination, 1.0);
        BinaryPrimitives.WriteDoubleLittleEndian(destination, 1.0);
        BinaryPrimitives.WriteHalfBigEndian(destination, (Half)1);
        BinaryPrimitives.WriteHalfLittleEndian(destination, (Half)1);
        BinaryPrimitives.WriteUInt128BigEndian(destination, (UInt128)1);
        BinaryPrimitives.WriteUInt128LittleEndian(destination, (UInt128)1);
        BinaryPrimitives.WriteUIntPtrBigEndian(destination, (nuint)1);
        BinaryPrimitives.WriteUIntPtrLittleEndian(destination, (nuint)1);
        _ = BinaryPrimitives.TryWriteInt16BigEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteInt16LittleEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteInt32BigEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteInt32LittleEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteInt64BigEndian(destination, 1L);
        _ = BinaryPrimitives.TryWriteInt64LittleEndian(destination, 1L);
        _ = BinaryPrimitives.TryWriteInt128BigEndian(destination, (Int128)1);
        _ = BinaryPrimitives.TryWriteInt128LittleEndian(destination, (Int128)1);
        _ = BinaryPrimitives.TryWriteIntPtrBigEndian(destination, (nint)1);
        _ = BinaryPrimitives.TryWriteIntPtrLittleEndian(destination, (nint)1);
        _ = BinaryPrimitives.TryWriteUInt16BigEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteUInt16LittleEndian(destination, 1);
        _ = BinaryPrimitives.TryWriteUInt32BigEndian(destination, 1U);
        _ = BinaryPrimitives.TryWriteUInt32LittleEndian(destination, 1U);
        _ = BinaryPrimitives.TryWriteUInt64BigEndian(destination, 1UL);
        _ = BinaryPrimitives.TryWriteUInt64LittleEndian(destination, 1UL);
        _ = BinaryPrimitives.TryWriteUInt128BigEndian(destination, (UInt128)1);
        _ = BinaryPrimitives.TryWriteUInt128LittleEndian(destination, (UInt128)1);
        _ = BinaryPrimitives.TryWriteUIntPtrBigEndian(destination, (nuint)1);
        _ = BinaryPrimitives.TryWriteUIntPtrLittleEndian(destination, (nuint)1);
        _ = BinaryPrimitives.TryWriteSingleBigEndian(destination, 1.0f);
        _ = BinaryPrimitives.TryWriteSingleLittleEndian(destination, 1.0f);
        _ = BinaryPrimitives.TryWriteDoubleBigEndian(destination, 1.0);
        _ = BinaryPrimitives.TryWriteDoubleLittleEndian(destination, 1.0);
        _ = BinaryPrimitives.TryWriteHalfBigEndian(destination, (Half)1);
        _ = BinaryPrimitives.TryWriteHalfLittleEndian(destination, (Half)1);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "BinaryPrimitivesWriteCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var members = new[]
            {
                "BinaryPrimitives.WriteInt16BigEndian(destination, 1)",
                "BinaryPrimitives.WriteInt16LittleEndian(destination, 1)",
                "BinaryPrimitives.WriteInt32BigEndian(destination, 1)",
                "BinaryPrimitives.WriteInt32LittleEndian(destination, 1)",
                "BinaryPrimitives.WriteInt64BigEndian(destination, 1L)",
                "BinaryPrimitives.WriteInt64LittleEndian(destination, 1L)",
                "BinaryPrimitives.WriteInt128BigEndian(destination, (Int128)1)",
                "BinaryPrimitives.WriteInt128LittleEndian(destination, (Int128)1)",
                "BinaryPrimitives.WriteIntPtrBigEndian(destination, (nint)1)",
                "BinaryPrimitives.WriteIntPtrLittleEndian(destination, (nint)1)",
                "BinaryPrimitives.WriteUInt16BigEndian(destination, 1)",
                "BinaryPrimitives.WriteUInt16LittleEndian(destination, 1)",
                "BinaryPrimitives.WriteUInt32BigEndian(destination, 1U)",
                "BinaryPrimitives.WriteUInt32LittleEndian(destination, 1U)",
                "BinaryPrimitives.WriteSingleBigEndian(destination, 1.0f)",
                "BinaryPrimitives.WriteSingleLittleEndian(destination, 1.0f)",
                "BinaryPrimitives.WriteDoubleBigEndian(destination, 1.0)",
                "BinaryPrimitives.WriteDoubleLittleEndian(destination, 1.0)",
                "BinaryPrimitives.WriteHalfBigEndian(destination, (Half)1)",
                "BinaryPrimitives.WriteHalfLittleEndian(destination, (Half)1)",
                "BinaryPrimitives.WriteUInt128BigEndian(destination, (UInt128)1)",
                "BinaryPrimitives.WriteUInt128LittleEndian(destination, (UInt128)1)",
                "BinaryPrimitives.WriteUIntPtrBigEndian(destination, (nuint)1)",
                "BinaryPrimitives.WriteUIntPtrLittleEndian(destination, (nuint)1)",
                "BinaryPrimitives.TryWriteInt16BigEndian(destination, 1)",
                "BinaryPrimitives.TryWriteInt16LittleEndian(destination, 1)",
                "BinaryPrimitives.TryWriteInt32BigEndian(destination, 1)",
                "BinaryPrimitives.TryWriteInt32LittleEndian(destination, 1)",
                "BinaryPrimitives.TryWriteInt64BigEndian(destination, 1L)",
                "BinaryPrimitives.TryWriteInt64LittleEndian(destination, 1L)",
                "BinaryPrimitives.TryWriteInt128BigEndian(destination, (Int128)1)",
                "BinaryPrimitives.TryWriteInt128LittleEndian(destination, (Int128)1)",
                "BinaryPrimitives.TryWriteIntPtrBigEndian(destination, (nint)1)",
                "BinaryPrimitives.TryWriteIntPtrLittleEndian(destination, (nint)1)",
                "BinaryPrimitives.TryWriteUInt16BigEndian(destination, 1)",
                "BinaryPrimitives.TryWriteUInt16LittleEndian(destination, 1)",
                "BinaryPrimitives.TryWriteUInt32BigEndian(destination, 1U)",
                "BinaryPrimitives.TryWriteUInt32LittleEndian(destination, 1U)",
                "BinaryPrimitives.TryWriteUInt64BigEndian(destination, 1UL)",
                "BinaryPrimitives.TryWriteUInt64LittleEndian(destination, 1UL)",
                "BinaryPrimitives.TryWriteUInt128BigEndian(destination, (UInt128)1)",
                "BinaryPrimitives.TryWriteUInt128LittleEndian(destination, (UInt128)1)",
                "BinaryPrimitives.TryWriteUIntPtrBigEndian(destination, (nuint)1)",
                "BinaryPrimitives.TryWriteUIntPtrLittleEndian(destination, (nuint)1)",
                "BinaryPrimitives.TryWriteSingleBigEndian(destination, 1.0f)",
                "BinaryPrimitives.TryWriteSingleLittleEndian(destination, 1.0f)",
                "BinaryPrimitives.TryWriteDoubleBigEndian(destination, 1.0)",
                "BinaryPrimitives.TryWriteDoubleLittleEndian(destination, 1.0)",
                "BinaryPrimitives.TryWriteHalfBigEndian(destination, (Half)1)",
                "BinaryPrimitives.TryWriteHalfLittleEndian(destination, (Half)1)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, member));
            }
        }

        [Test]
        public void IPAddressIsLoopbackGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System.Net;

public static class IPAddressLoopbackGeneratedCatalogSignatureSamples
{
    public static bool Sample(IPAddress address)
    {
        return IPAddress.IsLoopback(address);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "IPAddressLoopbackGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "IPAddress.IsLoopback(address)"));
        }

        [Test]
        public void DateTimeGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class DateTimeCatalogSignatureSamples
{
    public static DateTime Sample(DateTime value, TimeSpan offset)
    {
        _ = value.Add(offset);
        _ = value.AddDays(1);
        _ = value.AddHours(2);
        _ = value.AddMilliseconds(3);
        _ = value.AddMinutes(4);
        _ = value.AddMonths(5);
        _ = value.AddSeconds(6);
        _ = value.AddTicks(7);
        _ = value.AddYears(8);
        _ = DateTime.FromBinary(value.ToBinary());
        _ = DateTime.FromOADate(value.ToOADate());
        _ = DateTime.Compare(value, value);
        _ = value.CompareTo(value);
        _ = value.Equals(value);
        _ = value.Equals((object)value);
        _ = DateTime.Equals(value, value);
        _ = DateTime.DaysInMonth(2000, 2);
        _ = value.Day;
        _ = value.DayOfWeek;
        _ = value.DayOfYear;
        _ = value.Hour;
        _ = value.Kind;
        _ = value.Millisecond;
        _ = value.Minute;
        _ = value.Month;
        _ = value.Second;
        _ = value.Ticks;
        _ = value.TimeOfDay;
        _ = value.Subtract(value);
        _ = value.Subtract(offset);
        _ = value.ToBinary();
        return value;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DateTimeGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Add(offset)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddDays(1)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddHours(2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMilliseconds(3)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMinutes(4)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddMonths(5)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddSeconds(6)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddTicks(7)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.AddYears(8)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.FromBinary(value.ToBinary())"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.FromOADate(value.ToOADate())"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.Compare(value, value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.CompareTo(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Equals(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Equals((object)value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.Equals(value, value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "DateTime.DaysInMonth(2000, 2)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Day"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.DayOfWeek"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.DayOfYear"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Hour"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Kind"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Millisecond"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Minute"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Month"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Second"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Ticks"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.TimeOfDay"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Subtract(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Subtract(offset)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.ToBinary()"));
        }

        [Test]
        public void BooleanAndCharGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class BooleanCharCatalogSignatureSamples
{
    public static int Sample(bool left, bool right, char value, char other, int codePoint)
    {
        _ = left.CompareTo(right);
        _ = char.ConvertFromUtf32(codePoint);
        _ = char.ConvertToUtf32(value, other);
        _ = char.GetNumericValue(value);
        _ = char.IsControl(value);
        _ = char.IsDigit(value);
        _ = char.IsLetter(value);
        _ = char.IsLower(value);
        _ = char.IsNumber(value);
        _ = char.IsPunctuation(value);
        _ = char.IsSeparator(value);
        _ = char.IsSymbol(value);
        _ = char.IsUpper(value);
        _ = char.IsWhiteSpace(value);
        _ = char.ToLowerInvariant(value);
        _ = char.ToUpperInvariant(value);
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "BooleanCharGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "left.CompareTo(right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.ConvertFromUtf32(codePoint)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.ConvertToUtf32(value, other)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.GetNumericValue(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsControl(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsDigit(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsLetter(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsLower(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsNumber(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsPunctuation(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsSeparator(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsSymbol(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsUpper(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.IsWhiteSpace(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.ToLowerInvariant(value)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "char.ToUpperInvariant(value)"));
        }

        [Test]
        public void IndexAndHashCodeGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public static class IndexHashCodeCatalogSignatureSamples
{
    public static int Sample()
    {
        HashCode hash = default;
        var copy = new HashCode();
        var combined = HashCode.Combine(1, 2);
        var end = Index.End;
        var start = Index.Start;
        _ = copy.ToHashCode();
        _ = hash.ToHashCode();
        return combined;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "IndexHashCodeGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new HashCode()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "HashCode.Combine(1, 2)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "hash.ToHashCode()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "copy.ToHashCode()"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Index.End"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "Index.Start"));
        }

        [Test]
        public void StringBuilderLengthAndHttpResponseSuccessStatusCodeGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System.Net.Http;
using System.Text;

public static class StringBuilderHttpResponseCatalogSignatureSamples
{
    public static int Sample(StringBuilder builder, HttpResponseMessage response)
    {
        return builder.Length + (response.IsSuccessStatusCode ? 1 : 0);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "StringBuilderHttpResponseGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "builder.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "response.IsSuccessStatusCode"));
        }

        [Test]
        public void DateTimeToFileTimeAndMemberwiseCloneGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;

public class CloneableSample
{
    public object CloneSelf()
    {
        return MemberwiseClone();
    }
}

public static class DateTimeMemberwiseCloneCatalogSignatureSamples
{
    public static long Sample(DateTime value, CloneableSample sample)
    {
        _ = sample.CloneSelf();
        return value.ToFileTime();
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DateTimeMemberwiseCloneGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "MemberwiseClone()"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.ToFileTime()"));
        }

        [Test]
        public void SpanAndMemoryMarshalGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.Runtime.InteropServices;

public static class SpanMemoryMarshalCatalogSignatureSamples
{
    public static int Sample(ReadOnlySpan<int> readOnly, Span<int> writable)
    {
        ReadOnlySpan<byte> bytes = stackalloc byte[sizeof(int)];
        var head = readOnly.Slice(0, 0);
        var index = readOnly.BinarySearch(0);
        var readOnlyBytes = MemoryMarshal.AsBytes(readOnly);
        _ = MemoryMarshal.Read<int>(bytes);
        var writableBytes = MemoryMarshal.AsBytes(writable);
        return readOnly.Length + writable.Length + head.Length + index + readOnlyBytes.Length + writableBytes.Length + (readOnly.IsEmpty ? 0 : 1) + (writable.IsEmpty ? 0 : 1);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "SpanMemoryMarshalGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "readOnly.Slice(0, 0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "readOnly.BinarySearch(0)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "MemoryMarshal.Read<int>(bytes)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "MemoryMarshal.AsBytes(writable)"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "readOnly.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "writable.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "readOnly.IsEmpty"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "writable.IsEmpty"));
        }

        [Test]
        public void ReadOnlySequenceGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System.Buffers;

public static class ReadOnlySequenceCatalogSignatureSamples
{
    public static int Sample(ReadOnlySequence<int> value)
    {
        var start = value.Start;
        var end = value.End;
        var slice = value.Slice(1L);
        return value.IsEmpty ? 0 : value.Length > slice.Length ? 1 : 2;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ReadOnlySequenceGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Start"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.End"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.Length"));
            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "value.IsEmpty"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "value.Slice(1L)"));
        }

        [Test]
        public void ListCapacityGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
using System.Collections.Generic;

public static class ListCapacityCatalogSignatureSamples
{
    public static int Sample(List<int> values)
    {
        return values.Capacity;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ListCapacityGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetPropertySignature(compilation, syntaxTree, "values.Capacity"));
        }

        [Test]
        public void ReviewedRuntimePureHelpers_AreNotBackedByStaticCatalogs()
        {
            var members = new[]
            {
                "System.Collections.Generic.List<T>.AsReadOnly()",
                "System.Runtime.Serialization.DataContractAttribute.DataContractAttribute()",
                "System.Linq.ParallelEnumerable.AsParallel<TSource>(System.Collections.Generic.IEnumerable<TSource>)",
                "System.Reflection.Emit.Label.Equals(object)",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void ReviewedRuntimeHelperSlices_AreNotBackedByStaticCatalogs()
        {
            const string source = @"
using System;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

public static class ReviewedRuntimeHelperCatalogSignatureSamples
{
    public static int Sample(ReadOnlySpan<int> values, Delegate left, Delegate right)
    {
        _ = StringInfo.ParseCombiningCharacters(""text"");
        _ = Delegate.Combine(left, right);
        _ = Delegate.Remove(left, right);
        _ = Marshal.SizeOf<int>();
        _ = new Pipe(PipeOptions.Default);
        return values.Length;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var coreLibDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var sharedDirectory = Directory.GetParent(Directory.GetParent(coreLibDirectory)!.FullName)!.FullName;
            var pipelinesAssemblyPath = Directory.GetFiles(sharedDirectory, "System.IO.Pipelines.dll", SearchOption.AllDirectories)
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .First();
            var compilation = CSharpCompilation.Create(
                "ReviewedRuntimeHelperCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences()
                    .Add(MetadataReference.CreateFromFile(pipelinesAssemblyPath)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "StringInfo.ParseCombiningCharacters(\"text\")"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Delegate.Combine(left, right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Delegate.Remove(left, right)"));
            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "Marshal.SizeOf<int>()"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new Pipe(PipeOptions.Default)"));
        }

        [Test]
        public void MetadataExpressionAndDeadReviewedRuntimeRows_AreNotBackedByManualCatalogs()
        {
            var members = new[]
            {
                "System.Reflection.Metadata.MetadataReader.GetString(System.Reflection.Metadata.StringHandle)",
                "System.Diagnostics.CounterSample.Calculate(System.Diagnostics.CounterSample, System.Diagnostics.CounterSample)",
                "System.Linq.Expressions.Expression.Constant(object)",
                "System.Linq.Expressions.Expression.Call(System.Reflection.MethodInfo, System.Linq.Expressions.Expression[])",
                "System.Runtime.Intrinsics.X86.Avx2.Multiply(System.Runtime.Intrinsics.Vector256<double>, System.Runtime.Intrinsics.Vector256<double>)",
                "System.Reflection.Emit.OpCodes.Ldarg_0.get",
                "System.Runtime.CompilerServices.IsExternalInit",
            };

            foreach (var member in members)
            {
                AssertNotInManualCatalogs(member);
            }
        }

        [Test]
        public void EmailAddressConstructorGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
using System.ComponentModel.DataAnnotations;

public static class EmailAddressCatalogSignatureSamples
{
    public static int Sample()
    {
        var attribute = new EmailAddressAttribute();
        return attribute is null ? 0 : 1;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "EmailAddressGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new EmailAddressAttribute()"));
        }

        [Test]
        public void ConfigurationManagerAppSettings_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Configuration;

public static class ConfigurationManagerCatalogSignatureSamples
{
    public static string? Sample()
    {
        return ConfigurationManager.AppSettings[""MyKey""];
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ConfigurationManagerGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences()
                    .Add(MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetPropertySignature(compilation, syntaxTree, "ConfigurationManager.AppSettings");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "ConfigurationManager.AppSettings");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

            Assert.That(signature, Is.EqualTo("System.Configuration.ConfigurationManager.AppSettings.get"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve System.Configuration.ConfigurationManager.AppSettings.get from the package implementation assembly.");
            Assert.That(classification, Is.EqualTo("impure"));
        }

        [Test]
        public void ConfigurationManagerConnectionStrings_IsSourcedFromGeneratedImpureEvidence_NotStaticCatalogs()
        {
            const string source = @"
using System.Configuration;

public static class ConfigurationManagerConnectionStringsCatalogSignatureSamples
{
    public static ConnectionStringSettingsCollection Sample()
    {
        return ConfigurationManager.ConnectionStrings;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ConfigurationManagerConnectionStringsGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences()
                    .Add(MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var signature = GetPropertySignature(compilation, syntaxTree, "ConfigurationManager.ConnectionStrings");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "ConfigurationManager.ConnectionStrings");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var (matched, classification) = GetGeneratedPurityClassification(propertySymbol.GetMethod!, compilation);

            Assert.That(signature, Is.EqualTo("System.Configuration.ConfigurationManager.ConnectionStrings.get"));
            AssertNotInManualCatalogs(signature);
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve System.Configuration.ConfigurationManager.ConnectionStrings.get from the package implementation assembly.");
            Assert.That(classification, Is.EqualTo("impure"));
        }

        [Test]
        public void CoreComponentModelAttributeConstructorsGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System;
using System.ComponentModel;
using System.Diagnostics;

public static class CoreComponentModelAttributeConstructorCatalogSignatureSamples
{
    public static int Sample()
    {
        var browsable = new BrowsableAttribute(true);
        var description = new DescriptionAttribute(""sample"");
        var conditional = new ConditionalAttribute(""DEBUG"");
        return (browsable is null ? 0 : 1) + (description is null ? 0 : 1) + (conditional is null ? 0 : 1);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CoreComponentModelAttributeConstructorsGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new BrowsableAttribute(true)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new DescriptionAttribute(\"sample\")"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new ConditionalAttribute(\"DEBUG\")"));
        }

        [Test]
        public void RegularExpressionAttributeConstructorGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
using System.ComponentModel.DataAnnotations;

public static class RegularExpressionAttributeCatalogSignatureSamples
{
    public static int Sample()
    {
        var attribute = new RegularExpressionAttribute(""^[a-z]+$"");
        return attribute is null ? 0 : 1;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "RegularExpressionAttributeGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new RegularExpressionAttribute(\"^[a-z]+$\")"));
        }

        [Test]
        public void CoreDataAnnotationsConstructorsGeneratedPurityEntriesResolveAgainstNet80References()
        {
            var source = @"
using System.ComponentModel.DataAnnotations;

public static class CoreDataAnnotationsConstructorCatalogSignatureSamples
{
    public static int Sample()
    {
        var required = new RequiredAttribute();
        var stringLength = new StringLengthAttribute(10);
        var range = new RangeAttribute(0d, 1d);
        return (required is null ? 0 : 1) + (stringLength is null ? 0 : 1) + (range is null ? 0 : 1);
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "CoreDataAnnotationsConstructorsGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new RequiredAttribute()"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new StringLengthAttribute(10)"));
            AssertNotInManualCatalogs(GetObjectCreationSignature(compilation, syntaxTree, "new RangeAttribute(0d, 1d)"));
        }

        [Test]
        public void DecimalNegateGeneratedPurityEntryResolvesAgainstNet80References()
        {
            var source = @"
public static class DecimalNegateCatalogSignatureSamples
{
    public static int Sample(decimal value)
    {
        var negated = decimal.Negate(value);
        return 0;
    }
}";
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DecimalNegateGeneratedCatalogResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            AssertNotInManualCatalogs(GetInvocationSignature(compilation, syntaxTree, "decimal.Negate(value)"));
        }

        private static void AssertCatalogMembership(string signature, bool expectedPure, bool expectedImpure)
        {
            Assert.That(Constants.KnownPureBCLMembers.Contains(signature), Is.EqualTo(expectedPure), signature);
            Assert.That(Constants.KnownImpureMethods.Contains(signature), Is.EqualTo(expectedImpure), signature);
            Assert.That(expectedPure && expectedImpure, Is.False, "Test sample should not intentionally expect a catalog conflict: " + signature);
        }

        private static void AssertNotInManualCatalogs(string signature)
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(signature), signature);
        }

        private static (bool matched, string classification) GetGeneratedPurityClassification(IMethodSymbol methodSymbol, Compilation compilation)
        {
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), default(CancellationToken) })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2];
            var classification = matched
                ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;
            return (matched, classification);
        }

        private static AnalyzerOptions CreateGeneratedPurityAnalyzerOptions()
        {
            Assert.Ignore("Checked-in effect summary JSON artifacts were removed from the repository.");
            return new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty);
        }

        private static ImmutableArray<AdditionalText> CreateCheckedInEffectSummaryAdditionalFiles()
        {
            var analyzerDirectory = Path.Combine(FindRepositoryRoot(), "PurelySharp.Analyzer");
            var summaryPaths = Directory
                .EnumerateFiles(analyzerDirectory, "*.PurelySharp.EffectSummary.json", SearchOption.TopDirectoryOnly)
                .Concat(new[] { Path.Combine(analyzerDirectory, "PurelySharp.EffectSummary.json") })
                .Where(File.Exists)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();

            return summaryPaths
                .Select(path => (AdditionalText)new DiskAdditionalText(path))
                .ToImmutableArray();
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "PurelySharp.Analyzer")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
        }

        private sealed class DiskAdditionalText : AdditionalText
        {
            public DiskAdditionalText(string path)
            {
                Path = path;
            }

            public override string Path { get; }

            public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default)
            {
                return Microsoft.CodeAnalysis.Text.SourceText.From(File.ReadAllText(Path));
            }
        }

        private static string GetInvocationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node => node.ToString() == expressionText)
                .ToArray();
            Assert.That(invocations, Is.Not.Empty, "Invocation should exist: " + expressionText);
            var invocation = invocations[^1];
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Invocation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetObjectCreationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Object creation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetPropertySignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText, bool preferSetter = false)
        {
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol;
            Assert.That(symbol, Is.Not.Null, "Property should resolve: " + expressionText);

            if (preferSetter && symbol is IPropertySymbol propertySymbol && propertySymbol.SetMethod != null)
            {
                var setterSignature = propertySymbol.SetMethod.OriginalDefinition.ToDisplayString();
                return setterSignature.EndsWith(".set", StringComparison.Ordinal)
                    ? setterSignature
                    : setterSignature + ".set";
            }

            var signature = symbol!.OriginalDefinition.ToDisplayString();
            return signature.EndsWith(".get", StringComparison.Ordinal) || signature.EndsWith(".set", StringComparison.Ordinal)
                ? signature
                : signature + ".get";
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }
    }
}
