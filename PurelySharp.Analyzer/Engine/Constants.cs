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
        "System.Collections.Generic.IDictionary<TKey, TValue>.Keys.get",
        "System.Collections.Generic.IDictionary<TKey, TValue>.Values.get",
        "System.Collections.Queue.Synchronized(System.Collections.Queue)",
        "System.Text.RegularExpressions.Regex.Split(string, string)",
        "System.Text.RegularExpressions.Regex.Split(string)",
        "System.ComponentModel.TypeDescriptor.GetConverter(System.Type)",
        "System.ComponentModel.TypeDescriptor.GetProperties(object)",
        "object.GetHashCode()",
        "System.Diagnostics.Debugger.Break()",
        "System.Type.GetConstructor(System.Type[])",
        "System.Type.GetConstructors()",
        "System.Type.GetConstructors(System.Reflection.BindingFlags)",
        "System.Type.GetEvent(string)",
        "System.Type.GetEvent(string, System.Reflection.BindingFlags)",
        "System.Type.GetEvents()",
        "System.Type.GetEvents(System.Reflection.BindingFlags)",
        "System.Type.GetField(string)",
        "System.Type.GetField(string, System.Reflection.BindingFlags)",
        "System.Type.GetFields()",
        "System.Type.GetFields(System.Reflection.BindingFlags)",
        "System.Type.GetInterface(string)",
        "System.Type.GetInterface(string, bool)",
        "System.Type.GetInterfaces()",
        "System.Type.GetMember(string)",
        "System.Type.GetMember(string, System.Reflection.BindingFlags)",
        "System.Type.GetMember(string, System.Reflection.MemberTypes, System.Reflection.BindingFlags)",
        "System.Type.GetMembers()",
        "System.Type.GetMembers(System.Reflection.BindingFlags)",
        "System.Type.GetMethod(string)",
        "System.Type.GetMethod(string, System.Type[])",
        "System.Type.GetMethod(string, System.Reflection.BindingFlags)",
        "System.Type.GetMethods()",
        "System.Type.GetMethods(System.Reflection.BindingFlags)",
        "System.Type.GetNestedType(string)",
        "System.Type.GetNestedType(string, System.Reflection.BindingFlags)",
        "System.Type.GetNestedTypes()",
        "System.Type.GetNestedTypes(System.Reflection.BindingFlags)",
        "System.Type.GetProperty(string)",
        "System.Type.GetProperty(string, System.Reflection.BindingFlags)",
        "System.Type.GetProperties(System.Reflection.BindingFlags)",
        "System.Type.GetProperties()",
        "System.Reflection.Assembly.CodeBase.get",
        "System.Reflection.Assembly.CustomAttributes.get",
        "System.Reflection.Assembly.DefinedTypes.get",
        "System.Reflection.Assembly.EntryPoint.get",
        "System.Reflection.Assembly.EscapedCodeBase.get",
        "System.Reflection.Assembly.ExportedTypes.get",
        "System.Reflection.Assembly.GlobalAssemblyCache.get",
        "System.Reflection.Assembly.HostContext.get",
        "System.Reflection.Assembly.IsDynamic.get",
        "System.Reflection.Assembly.IsFullyTrusted.get",
        "System.Reflection.Assembly.ManifestModule.get",
        "System.Reflection.Assembly.Modules.get",
        "System.Reflection.Assembly.ReflectionOnly.get",
        "System.Reflection.Assembly.SecurityRuleSet.get",
        "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
        "System.Reflection.MethodBase.IsStatic.get",
        "System.Reflection.Module.ModuleVersionId.get",
        "System.Diagnostics.Trace.WriteLine(string)",
        "System.Enum.GetName(System.Type, object)",
        "System.Enum.IsDefined(System.Type, object)",
        "System.Enum.GetValues(System.Type)",
        "System.GC.Collect()",
        "System.GC.GetTotalMemory(bool)",
        "System.IO.DriveInfo.TotalSize.get",
        "System.IO.DriveInfo.GetDrives()",
        "System.IO.File.AppendAllText(string, string)",
        "System.IO.File.Delete(string)",
        "System.IO.File.ReadAllBytes(string)",
        "System.IO.File.ReadAllText(string)",
        "System.IO.File.WriteAllText(string, string)",
        "System.IO.File.WriteAllBytes(string, byte[])",
        "System.Lazy<T>.Lazy(System.Func<T>)",
        "System.Lazy<T>.Value.get",
        "System.Linq.Enumerable.ToArray<TSource>(System.Collections.Generic.IEnumerable<TSource>)",
        "System.Net.Dns.GetHostEntry(string)",
        "System.Net.Http.HttpClient.GetAsync(string)",
        "System.Net.Http.HttpClient.GetStringAsync(string)",
        "System.Net.Http.HttpClient.PostAsync(string, System.Net.Http.HttpContent)",
        "System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()",
        "System.Net.Sockets.Socket.Connect(System.Net.EndPoint)",
        "System.Net.Sockets.Socket.ConnectAsync(System.Net.EndPoint)",
        "System.Net.Sockets.Socket.Receive(byte[])",
        "System.Net.Sockets.Socket.Send(byte[])",
        "System.Net.WebClient.DownloadString(string)",
        "System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(object)",
        "System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(System.RuntimeTypeHandle)",
        "System.Runtime.GCSettings.IsServerGC.get",
        "System.Runtime.InteropServices.Marshal.AllocHGlobal(System.IntPtr)",
        "System.Runtime.InteropServices.Marshal.FreeHGlobal(System.IntPtr)",
        "System.Runtime.InteropServices.Marshal.StructureToPtr(object, System.IntPtr, bool)",

        "System.Security.Cryptography.RandomNumberGenerator.GetBytes(byte[])",
        "System.CodeDom.Compiler.CodeDomProvider.CreateProvider(string)",
        "System.CodeDom.Compiler.CompilerResults.Errors.get",
        "System.Exception.ToString()",

        "System.Text.Json.JsonSerializer.Deserialize",
        "JsonSerializer.Deserialize",
        "System.Text.Json.JsonSerializer.Deserialize<TValue>(string, System.Text.Json.JsonSerializerOptions?)",
        "System.Text.Json.JsonSerializer.Deserialize<TValue>(System.ReadOnlySpan<byte>, System.Text.Json.JsonSerializerOptions?)",

        "System.Text.Json.JsonSerializer.DeserializeAsync",
        "System.Text.Json.JsonSerializer.SerializeAsync",
        "System.Threading.Mutex.WaitOne()",
        "System.Threading.Tasks.Task.Yield()",
        "System.Xml.XmlReader.Create(System.IO.Stream)",
        "System.Xml.XmlReader.Read()",
        "System.Xml.XmlWriter.Create(System.IO.Stream)",
        "System.Xml.XmlWriter.WriteStartElement(string)",
        "System.Xml.XmlWriter.WriteString(string)",
        "System.Collections.ObjectModel.ObservableCollection<T>.Add(T)",
        "System.ComponentModel.BackgroundWorker.RunWorkerAsync()",
        "System.Diagnostics.EventLog.WriteEntry(string)",
        "System.Diagnostics.PerformanceCounter.NextValue()",
        "System.Diagnostics.TraceSource.TraceEvent(System.Diagnostics.TraceEventType, int)",
        "System.Diagnostics.FileVersionInfo.GetVersionInfo(string)",
        "System.IO.Compression.ZipFile.CreateFromDirectory(string, string)",
        "System.IO.Compression.ZipFile.ExtractToDirectory(string, string)",
        "System.IO.Pipes.NamedPipeServerStream.WaitForConnection()",
        "System.Net.Mail.SmtpClient.Send(System.Net.Mail.MailMessage)",
        "System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()",
        "System.Net.NetworkInformation.Ping.Send(string)",
        "System.Reflection.Emit.DynamicMethod.DynamicMethod(string, System.Type, System.Type[])",
        "System.Reflection.Emit.ILGenerator.Emit(System.Reflection.Emit.OpCode)",
        "System.Runtime.Caching.MemoryCache.Default.get",
        "System.Runtime.Caching.MemoryCache.Add(string, object, System.DateTimeOffset)",
        "System.Runtime.Caching.MemoryCache.Get(string)",
        "System.Runtime.Serialization.Json.DataContractJsonSerializer.ReadObject(System.IO.Stream)",
        "System.Runtime.Serialization.Json.DataContractJsonSerializer.WriteObject(System.IO.Stream, object)",
        "System.Security.Principal.WindowsIdentity.GetCurrent()",
        "System.Security.SecureString.AppendChar(char)",
        "System.Security.SecureString.Dispose()",
        "System.Timers.Timer.Start()",
        "System.Timers.Timer.Stop()",
        "System.Xml.Xsl.XslCompiledTransform.Load(string)",
        "System.Xml.Xsl.XslCompiledTransform.Transform(string, string)",
        "System.Collections.Specialized.NameValueCollection.Add(string, string)",
        "System.IO.DirectoryInfo.Exists.get",
        "System.IO.DirectoryInfo.EnumerateFiles()",
        "System.IO.FileInfo.Length.get",
        "System.IO.FileSystemWatcher.EnableRaisingEvents.set",
        "System.Net.HttpListener.Start()",
        "System.Net.HttpListener.GetContext()",
        "System.Net.Sockets.UdpClient.Receive(ref System.Net.IPEndPoint)",
        "System.Reflection.AssemblyName.GetAssemblyName(string)",
        "System.Security.Cryptography.X509Certificates.X509Store.Open(System.Security.Cryptography.X509Certificates.OpenFlags)",
        "System.Threading.Tasks.Dataflow.ActionBlock<TInput>.Post(TInput)",
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
        "System.ComponentModel.CancelEventArgs.Cancel.set",
        "System.ComponentModel.INotifyPropertyChanged.PropertyChanged",
        "System.Data.DataTable.NewRow()",
        "System.Data.DataRow.AcceptChanges()",
        "System.Drawing.Bitmap.Bitmap(int, int)",
        "System.IO.Compression.BrotliStream.BrotliStream(System.IO.Stream, System.IO.Compression.CompressionMode)",
        "System.IO.Compression.DeflateStream.Read(byte[], int, int)",
        "System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(string)",
        "System.IO.MemoryMappedFiles.MemoryMappedViewAccessor.ReadByte(long)",
        "System.Linq.Queryable.Count<TSource>(System.Linq.IQueryable<TSource>)",
        "System.Linq.Queryable.ToList<TSource>(System.Linq.IQueryable<TSource>)",
        "System.Net.Http.Headers.HttpRequestHeaders.Add(string, string)",
        "System.Net.Security.SslStream.AuthenticateAsClientAsync(string)",
        "System.Net.Sockets.Socket.Accept()",
        "System.Net.Sockets.SocketAsyncEventArgs.AcceptSocket.get",
        "System.Net.Sockets.SocketAsyncEventArgs.AcceptSocket.set",
        "System.Reflection.AssemblyName.AssemblyName(string)",
        "System.Reflection.Emit.AssemblyBuilder.DefineDynamicModule(string)",
        "System.Resources.ResourceManager.GetString(string)",
        "System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start<TStateMachine>(ref TStateMachine)",
        "System.Runtime.CompilerServices.ConditionalWeakTable<TKey, TValue>.Add(TKey, TValue)",
        "System.Runtime.InteropServices.ComWrappers.GetOrCreateObjectForComInstance(System.IntPtr, System.Runtime.InteropServices.CreateObjectFlags)",
        "System.Runtime.InteropServices.GCHandle.Alloc(object)",
        "System.Runtime.InteropServices.GCHandle.Free()",
        "System.Security.AccessControl.DirectorySecurity.AddAccessRule(System.Security.AccessControl.FileSystemAccessRule)",
        "System.Security.Cryptography.Pkcs.SignedCms.Sign()",
        "System.Security.Cryptography.Xml.SignedXml.ComputeSignature()",
        "System.Security.Cryptography.X509Certificates.X509Certificate2.X509Certificate2(string)",
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
        "System.Runtime.InteropServices.MemoryMarshal.Write<T>(System.Span<byte>, ref T)",
        "System.ComponentModel.Component.Dispose()",
        "System.ComponentModel.LicenseManager.Validate(System.Type, object)",
        "System.Data.DataSet.Clear()",
        "System.Diagnostics.Debugger.IsAttached.get",
        "System.Diagnostics.Debugger.Launch()",
        "System.Diagnostics.StackTrace.StackTrace()",
        "System.Diagnostics.Switch.Level.get",
        "System.DirectoryServices.DirectoryEntry.DirectoryEntry(string)",
        "System.GC.GetGeneration(object)",
        "System.GC.KeepAlive(object)",
        "System.GC.KeepAlive(object?)",
        "System.IO.BinaryReader.ReadBoolean()",
        "System.IO.BinaryWriter.Write(string)",
        "System.IO.Directory.EnumerateDirectories(string)",
        "System.IO.FileStream.FileStream(string, System.IO.FileMode)",
        "System.IO.Pipelines.PipeReader.ReadAsync(System.Threading.CancellationToken)",
        "System.IO.Pipelines.PipeWriter.WriteAsync(System.ReadOnlyMemory<byte>, System.Threading.CancellationToken)",
        "System.Linq.ParallelEnumerable.ForAll<TSource>(System.Linq.ParallelQuery<TSource>, System.Action<TSource>)",
        "System.Linq.ParallelQuery<TSource>.ToList()",
        "System.Management.ManagementObjectSearcher.ManagementObjectSearcher(string)",
        "System.Net.CredentialCache.DefaultCredentials.get",
        "System.Net.Http.HttpMessageInvoker.SendAsync(System.Net.Http.HttpRequestMessage, System.Threading.CancellationToken)",
        "System.Net.ServicePointManager.SecurityProtocol.get",
        "System.Net.ServicePointManager.SecurityProtocol.set",
        "System.Runtime.InteropServices.Marshal.GetLastWin32Error()",
        "System.Runtime.Serialization.FormatterServices.GetUninitializedObject(System.Type)",


        "System.Collections.Generic.IEnumerator<T>.MoveNext()",
        "System.Collections.ObjectModel.Collection<T>.InsertItem(int, T)",
        "System.Collections.ObjectModel.Collection<T>.SetItem(int, T)",
        "System.ComponentModel.INotifyCollectionChanged.CollectionChanged",
        "System.Delegate.DynamicInvoke(object[])",

        "System.GC.SuppressFinalize(object)",

        "System.IServiceProvider.GetService(System.Type)",
        "System.IO.File.Copy(string, string)",
        "System.IO.File.Move(string, string)",
        "System.IO.File.OpenRead(string)",
        "System.IO.File.OpenWrite(string)",
        "System.IO.File.ReadAllLines(string)",
        "System.Net.Http.HttpContent.ReadAsStringAsync()",
        "System.Net.Http.HttpContent.ReadAsByteArrayAsync()",
        "System.Text.Encoding.Default.get",
        "System.Threading.Tasks.Task.ContinueWith(System.Action<System.Threading.Tasks.Task>)",
        "System.Threading.Tasks.Task.Wait()",
        "System.Threading.Tasks.Task<TResult>.Result.get",


        "System.Span<T>.CopyTo(System.Span<T>)",
        "System.Span<T>.TryCopyTo(System.Span<T>)",
        "System.MemoryExtensions.Reverse<T>(System.Span<T>)",
        "System.Exception.Source.set",


        "System.Activator.CreateInstanceFrom(string, string)",
        "System.Collections.Generic.Dictionary<TKey, TValue>.Values.CopyTo(TValue[], int)",
        "System.Collections.Generic.ICollection<T>.Add(T)",
        "System.Collections.Generic.ICollection<T>.Clear()",
        "System.Collections.Generic.ICollection<T>.Remove(T)",
        "System.Collections.Generic.IList<T>.Insert(int, T)",
        "System.Collections.Generic.IList<T>.RemoveAt(int)",
        "System.ComponentModel.EventHandlerList.AddHandler(object, System.Delegate)",
        "System.HashCode.Add<T>(T)",
        "System.IO.DirectoryInfo.Create()",
        "System.IO.DirectoryInfo.Delete()",
        "System.IO.FileInfo.CopyTo(string)",
        "System.IO.FileInfo.Delete()",
        "System.Text.Json.JsonSerializer.Serialize",

        "System.Security.Cryptography.RandomNumberGenerator.Fill(byte[])",



























    };

    public static readonly HashSet<string> KnownFreshOwnedArrayReturningMembers = new HashSet<string>(StringComparer.Ordinal)
    {
    };

    public static readonly HashSet<string> KnownPureBCLMembers = new HashSet<string>(StringComparer.Ordinal);
}
