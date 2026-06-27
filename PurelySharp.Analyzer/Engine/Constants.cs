using System.Collections.Immutable;

namespace PurelySharp.Analyzer.Engine;

public static class Constants
{

    public static readonly ImmutableHashSet<string> KnownImpureNamespaces = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.IO",
        "System.Net",
        "System.Data",
        "System.Threading",
        "System.Diagnostics",
        "System.Security.Cryptography",
        "System.Runtime.InteropServices",
        "System.Reflection"
    );

    public static readonly ImmutableHashSet<string> KnownImpureTypeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Timers.Timer"


    );

    public static readonly HashSet<string> KnownImpureMethods = new HashSet<string>(StringComparer.Ordinal)
    {

        "System.Activator.CreateInstance<T>()",
        "System.Activator.CreateInstance(System.Type)",
        "System.Activator.CreateInstance(System.Type, params object[])",
        "System.Array.AsReadOnly<T>(T[])",
        "System.Collections.ArrayList.Adapter(System.Collections.IList)",
        "System.Collections.Queue.Synchronized(System.Collections.Queue)",
        "System.Text.RegularExpressions.Regex.Split(string, string)",
        "System.Text.RegularExpressions.Regex.Split(string)",
        "System.ComponentModel.TypeDescriptor.GetConverter(System.Type)",
        "System.ComponentModel.TypeDescriptor.GetProperties(object)",
        "object.GetHashCode()",
        "System.Enum.GetName(System.Type, object)",
        "System.Enum.IsDefined(System.Type, object)",
        "System.Enum.GetValues(System.Type)",
        "System.GC.Collect()",
        "System.GC.GetTotalMemory(bool)",
        "System.IO.DriveInfo.TotalSize.get",
        "System.Lazy<T>.Lazy(System.Func<T>)",
        "System.Lazy<T>.Value.get",
        "System.Linq.Enumerable.ToArray<TSource>(System.Collections.Generic.IEnumerable<TSource>)",
        "System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(object)",
        "System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(System.RuntimeTypeHandle)",
        "System.Runtime.GCSettings.IsServerGC.get",
        "System.CodeDom.Compiler.CodeDomProvider.CreateProvider(string)",
        "System.CodeDom.Compiler.CompilerResults.Errors.get",
        "System.Exception.ToString()",

        "System.Text.Json.JsonSerializer.Deserialize",
        "JsonSerializer.Deserialize",
        "System.Text.Json.JsonSerializer.Deserialize<TValue>(string, System.Text.Json.JsonSerializerOptions?)",
        "System.Text.Json.JsonSerializer.Deserialize<TValue>(System.ReadOnlySpan<byte>, System.Text.Json.JsonSerializerOptions?)",

        "System.Text.Json.JsonSerializer.DeserializeAsync",
        "System.Text.Json.JsonSerializer.SerializeAsync",
        "System.Xml.XmlReader.Create(System.IO.Stream)",
        "System.Xml.XmlReader.Read()",
        "System.Xml.XmlWriter.Create(System.IO.Stream)",
        "System.Xml.XmlWriter.WriteStartElement(string)",
        "System.Xml.XmlWriter.WriteString(string)",
        "System.Collections.ObjectModel.ObservableCollection<T>.Add(T)",
        "System.ComponentModel.BackgroundWorker.RunWorkerAsync()",
        "System.Runtime.Caching.MemoryCache.Default.get",
        "System.Runtime.Caching.MemoryCache.Add(string, object, System.DateTimeOffset)",
        "System.Runtime.Caching.MemoryCache.Get(string)",
        "System.Runtime.Serialization.Json.DataContractJsonSerializer.ReadObject(System.IO.Stream)",
        "System.Runtime.Serialization.Json.DataContractJsonSerializer.WriteObject(System.IO.Stream, object)",
        "System.Security.Principal.WindowsIdentity.GetCurrent()",
        "System.Security.SecureString.AppendChar(char)",
        "System.Security.SecureString.Dispose()",
        "System.Xml.Xsl.XslCompiledTransform.Load(string)",
        "System.Xml.Xsl.XslCompiledTransform.Transform(string, string)",
        "System.Collections.Specialized.NameValueCollection.Add(string, string)",
        "System.IO.DirectoryInfo.Exists.get",
        "System.IO.FileInfo.Length.get",
        "System.IO.FileSystemWatcher.EnableRaisingEvents.set",
        "System.Threading.Tasks.Parallel.ForEach",
        "System.Threading.Tasks.Parallel.Invoke",
        "System.Windows.Input.ICommand.Execute(object)",
        "Microsoft.Extensions.Logging.ILogger.LogInformation(string)",
        "Microsoft.Extensions.DependencyInjection.ServiceProvider.GetService(System.Type)",
        "Microsoft.Extensions.Configuration.IConfiguration.GetConnectionString(string)",
        "Microsoft.Extensions.Configuration.IConfigurationRoot.Reload()",
        "System.Buffers.ArrayPool<T>.Shared.Rent(int)",
        "System.Buffers.ArrayPool<T>.Shared.Return(T[], bool)",
        "System.Buffers.Text.Base64.EncodeToUtf8(System.ReadOnlySpan<byte>, System.Span<byte>, out int, out int)",








        "System.Collections.ObjectModel.KeyedCollection<TKey, TItem>.Remove(TKey)",
        "System.ComponentModel.AddingNewEventArgs.AddingNewEventArgs()",
        "System.ComponentModel.CancelEventArgs.Cancel.get",
        "System.ComponentModel.INotifyPropertyChanged.PropertyChanged",
        "System.Drawing.Bitmap.Bitmap(int, int)",
        "System.Linq.Queryable.Count<TSource>(System.Linq.IQueryable<TSource>)",
        "System.Linq.Queryable.ToList<TSource>(System.Linq.IQueryable<TSource>)",
        "System.Net.Sockets.SocketAsyncEventArgs.AcceptSocket.get",
        "System.Net.Sockets.SocketAsyncEventArgs.AcceptSocket.set",
        "System.Resources.ResourceManager.GetString(string)",
        "System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start<TStateMachine>(ref TStateMachine)",
        "System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue>.Add(TKey, TValue)",
        "System.Security.AccessControl.DirectorySecurity.AddAccessRule(System.Security.AccessControl.FileSystemAccessRule)",
        "System.ServiceProcess.ServiceBase.Run(System.ServiceProcess.ServiceBase)",
        "System.Speech.Synthesis.SpeechSynthesizer.SpeakAsync(string)",
        "System.Text.Json.Utf8JsonWriter.WriteString(string, string)",
        "System.Transactions.TransactionScope.TransactionScope()",
        "System.Transactions.Transaction.Current.get",
        "Microsoft.Win32.RegistryKey.OpenSubKey(string)",
        "Microsoft.Win32.RegistryKey.GetValue(string)",
        "Microsoft.Win32.RegistryKey.SetValue(string, object)",
        "System.Net.Http.Headers.HttpContentHeaders.ContentLength.get",
        "System.Net.Http.Headers.HttpContentHeaders.ContentLength.set",
        "System.Runtime.InteropServices.SafeHandle.IsInvalid.get",
        "System.Text.Unicode.Utf8.ToUtf16(System.ReadOnlySpan<byte>, System.Span<char>, out int, out int)",
        "System.Xml.XmlDocument.LoadXml(string)",
        "System.Xml.XmlNode.SelectSingleNode(string)",
        "System.Xml.Schema.XmlSchemaSet.Compile()",
        "System.Text.Json.JsonDocument.Parse(string, System.Text.Json.JsonDocumentOptions)",
        "System.Text.Json.JsonElement.GetString()",
        "System.Runtime.Versioning.FrameworkName.FrameworkName(string)",
        "System.ComponentModel.Component.Dispose()",
        "System.ComponentModel.LicenseManager.Validate(System.Type, object)",
        "System.Diagnostics.Debugger.IsAttached.get",
        "System.Diagnostics.Switch.Level.get",
        "System.DirectoryServices.DirectoryEntry.DirectoryEntry(string)",
        "System.GC.GetGeneration(object)",
        "System.Linq.ParallelEnumerable.ForAll<TSource>(System.Linq.ParallelQuery<TSource>, System.Action<TSource>)",
        "System.Linq.ParallelQuery<TSource>.ToList()",
        "System.Management.ManagementObjectSearcher.ManagementObjectSearcher(string)",
        "System.Net.CredentialCache.DefaultCredentials.get",
        "System.Net.ServicePointManager.SecurityProtocol.get",
        "System.Net.ServicePointManager.SecurityProtocol.set",
        "System.Runtime.Serialization.FormatterServices.GetUninitializedObject(System.Type)",


        "System.Collections.Generic.IEnumerator<T>.MoveNext()",
        "System.Collections.ObjectModel.Collection<T>.InsertItem(int, T)",
        "System.Collections.ObjectModel.Collection<T>.SetItem(int, T)",
        "System.ComponentModel.INotifyCollectionChanged.CollectionChanged",
        "System.Delegate.DynamicInvoke(object[])",

        "System.GC.SuppressFinalize(object)",

        "System.IServiceProvider.GetService(System.Type)",
        "System.Text.Encoding.Default.get",
        "System.MemoryExtensions.Reverse<T>(System.Span<T>)",
        "System.Exception.Source.set",


        "System.Activator.CreateInstanceFrom(string, string)",
        "System.Collections.Generic.Dictionary<TKey, TValue>.Values.CopyTo(TValue[], int)",
        "System.ComponentModel.EventHandlerList.AddHandler(object, System.Delegate)",
        "System.HashCode.Add<T>(T)",
        "System.Text.Json.JsonSerializer.Serialize",



























    };

    public static readonly HashSet<string> KnownFreshOwnedArrayReturningMembers = new HashSet<string>(StringComparer.Ordinal)
    {
    };

    public static readonly HashSet<string> KnownPureBCLMembers = new HashSet<string>(StringComparer.Ordinal);
}
