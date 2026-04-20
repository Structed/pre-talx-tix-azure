using System.Diagnostics;
using Spectre.Console;

namespace TixTalk.Cli;

/// <summary>
/// Shared helpers for running Azure CLI (az) commands.
/// </summary>
public static class AzureCli
{
    /// <summary>
    /// Checks that Azure CLI is installed and working.
    /// </summary>
    public static bool Validate()
    {
        var (exitCode, _) = RunCommand(subscription: null, "version", "--output", "none");
        if (exitCode == 0)
            return true;

        AnsiConsole.MarkupLine("[red]Error:[/] Azure CLI (az) not found or not working.");
        AnsiConsole.MarkupLine("Install it from: [blue]https://aka.ms/installazurecli[/]");
        AnsiConsole.MarkupLine("Then run: [yellow]az login[/]");
        return false;
    }

    /// <summary>
    /// Runs an Azure CLI command, optionally scoped to a subscription.
    /// Pass null for <paramref name="subscription"/> to use the default.
    /// </summary>
    public static (int ExitCode, string Output) RunCommand(string? subscription, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Collect all arguments
        var allArgs = new List<string>(args);
        if (!string.IsNullOrWhiteSpace(subscription))
        {
            allArgs.Add("--subscription");
            allArgs.Add(subscription);
        }

        if (OperatingSystem.IsWindows())
        {
            var azPath = FindCli();
            if (azPath == null)
                return (1, "Azure CLI not found");

            if (azPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                // .cmd files must be launched via cmd.exe /c
                // Quote each argument and wrap the whole command for cmd.exe
                psi.FileName = "cmd.exe";
                var quotedArgs = string.Join(" ", allArgs.Select(a => $"\"{a}\""));
                psi.Arguments = $"/c \"\"{azPath}\" {quotedArgs}\"";
            }
            else
            {
                psi.FileName = azPath;
                foreach (var arg in allArgs)
                    psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            psi.FileName = "az";
            foreach (var arg in allArgs)
                psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return (1, "Failed to start az command");

            // Read both streams concurrently to avoid deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var stderr = stderrTask.GetAwaiter().GetResult();

            var output = string.IsNullOrWhiteSpace(stdout) ? stderr.TrimEnd() : stdout;
            return (process.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (1, ex.Message);
        }
    }

    private static string? FindCli()
    {
        // Look for az.exe or az.cmd in known install locations
        var searchDirs = new[]
        {
            @"C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin",
            @"C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Azure CLI\wbin"),
        };

        // Prefer az.exe (direct launch), fall back to az.cmd (needs cmd.exe /c)
        foreach (var dir in searchDirs)
        {
            var exePath = Path.Combine(dir, "az.exe");
            if (File.Exists(exePath))
                return exePath;
        }

        foreach (var dir in searchDirs)
        {
            var cmdPath = Path.Combine(dir, "az.cmd");
            if (File.Exists(cmdPath))
                return cmdPath;
        }

        // Try to find via PATH using where.exe
        foreach (var name in new[] { "az.exe", "az.cmd" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = name,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadLine();
                    process.WaitForExit();
                    if (!string.IsNullOrWhiteSpace(output) && File.Exists(output))
                        return output;
                }
            }
            catch
            {
                // Ignore
            }
        }

        return null;
    }
}
