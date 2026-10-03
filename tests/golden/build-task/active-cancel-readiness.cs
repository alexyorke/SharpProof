// golden-scenario: active-cancel-readiness
using System;
using System.Globalization;
using System.IO;
using System.Threading;

const string temporary = "ready.pid.tmp";
if (File.Exists("hold-partial"))
{
    File.WriteAllText(temporary, string.Empty);
    File.WriteAllText("partial.ready", "started");
    while (!File.Exists("publish.release"))
    {
        Thread.Sleep(10);
    }
}
File.WriteAllText(temporary, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
File.Move(temporary, "ready.pid");
Thread.Sleep(Timeout.Infinite);
