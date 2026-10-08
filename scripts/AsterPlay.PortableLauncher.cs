// Small Windows GUI bootstrapper for the unpacked portable layout.
// Compile with the Windows PowerShell 5.1 CodeDOM C# compiler, not the
// application SDK. The actual .NET 8 / WinUI 3 app and all its native and
// managed dependencies remain together under resources\.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class AsterPlayPortableLauncher
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);

    [STAThread]
    private static int Main(string[] arguments)
    {
        string launcherDirectory = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location);
        string appDirectory = Path.Combine(launcherDirectory, "resources");
        string appExecutable = Path.Combine(appDirectory, "AsterPlay.exe");

        if (!File.Exists(appExecutable))
        {
            MessageBox(IntPtr.Zero,
                "AsterPlay runtime was not found. Keep AsterPlay.exe and the resources folder together.",
                "AsterPlay", 0x10);
            return 1;
        }

        try
        {
            var start = new ProcessStartInfo();
            start.FileName = appExecutable;
            start.WorkingDirectory = appDirectory;
            start.UseShellExecute = false;
            start.Arguments = BuildArguments(arguments);

            using (Process process = Process.Start(start))
            {
                if (process == null)
                    throw new InvalidOperationException("The AsterPlay process was not created.");
            }

            // The real WinUI 3 process owns the taskbar window and startup
            // splash. This small launcher has no visible UI and exits.
            return 0;
        }
        catch (Exception error)
        {
            MessageBox(IntPtr.Zero,
                "AsterPlay could not start:\n" + error.Message,
                "AsterPlay", 0x10);
            return 1;
        }
    }

    // Windows command-line quoting, including embedded quotes/backslashes.
    private static string BuildArguments(string[] arguments)
    {
        StringBuilder result = new StringBuilder();
        foreach (string arg in arguments)
        {
            if (result.Length != 0)
                result.Append(' ');
            result.Append(Quote(arg));
        }
        return result.ToString();
    }

    private static string Quote(string argument)
    {
        if (argument.Length != 0 && argument.IndexOfAny(
            new char[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            return argument;

        StringBuilder result = new StringBuilder();
        result.Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes);
            backslashes = 0;
            result.Append(c);
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
