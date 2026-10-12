using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

internal static class SharpProofProductionMsBuildHost
{
    public static int Main(string[] args)
    {
        try
        {
            using var request = JsonDocument.Parse(File.ReadAllText(args[0]));
            string sdk = request.RootElement.GetProperty("sdkPath").GetString();
            string dotnet = request.RootElement.GetProperty("dotnetPath").GetString();
            if (!string.Equals(Environment.ProcessPath, dotnet, StringComparison.Ordinal))
                throw new InvalidOperationException("Production inventory host differs from the resolved dotnet host.");
            // dotnet msbuild runs the CLI from this SDK directory. Keep the SDK's
            // own forwarding defaults and environment handling for every query.
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", sdk + Path.DirectorySeparatorChar);
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                string path = Path.Combine(sdk, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(sdk, "dotnet.dll"));
            var commandType = assembly.GetType("Microsoft.DotNet.Tools.MSBuild.MSBuildCommand", true);
            var fromArgs = commandType.GetMethod("FromArgs", new[] { typeof(string[]), typeof(string) });
            var execute = commandType.GetMethod("Execute", Type.EmptyTypes);
            if (fromArgs == null || execute == null)
                throw new MissingMethodException("The active SDK has no MSBuild forwarding entrypoint.");
            var records = new List<object>();
            var forwarding = new List<object>();
            foreach (var query in request.RootElement.GetProperty("queries").EnumerateArray())
            {
                string[] arguments = query.GetProperty("arguments").EnumerateArray()
                    .Select(value => value.GetString()).ToArray();
                using var capture = new StringWriter(CultureInfo.InvariantCulture);
                TextWriter oldOut = Console.Out, oldError = Console.Error;
                int exitCode;
                try
                {
                    Console.SetOut(capture);
                    Console.SetError(capture);
                    var command = fromArgs.Invoke(null, new object[] { arguments, null });
                    var forwarded = commandType.GetProperty("MSBuildArguments").GetValue(command);
                    forwarding.Add(new
                    {
                        projectRelativePath = query.GetProperty("projectRelativePath").GetString(),
                        arguments = forwarded,
                        workingDirectory = Environment.CurrentDirectory
                    });
                    exitCode = (int)execute.Invoke(command, null);
                }
                finally
                {
                    Console.SetOut(oldOut);
                    Console.SetError(oldError);
                }
                records.Add(new
                {
                    projectRelativePath = query.GetProperty("projectRelativePath").GetString(),
                    projectPath = query.GetProperty("projectPath").GetString(),
                    exitCode,
                    output = capture.ToString().Trim()
                });
            }
            // The SDK runtimeconfig selects the framework with its normal roll-forward
            // semantics. Report the effective framework; do not pin its minimum patch.
            var runtimeAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(value => new[] { "System.Private.CoreLib", "System.Runtime", "System.Text.Json",
                    "Microsoft.Build", "Microsoft.Build.Framework", "MSBuild", "dotnet",
                    "Microsoft.DotNet.Cli.Utils" }.Contains(value.GetName().Name))
                .Select(value => new { name = value.GetName().Name, path = value.Location }).ToArray();
            File.WriteAllText(args[1], JsonSerializer.Serialize(new
            {
                queries = records,
                runtime = RuntimeInformation.FrameworkDescription,
                actualHost = Environment.ProcessPath,
                sdkAssembly = assembly.Location,
                runtimeAssemblies,
                sdkBaseDirectory = AppContext.BaseDirectory,
                entrypoint = "Microsoft.DotNet.Tools.MSBuild.MSBuildCommand.FromArgs.Execute",
                forwarding
            }) + "\n");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }
}
