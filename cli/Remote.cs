using System.Diagnostics;
using Renci.SshNet;
using Spectre.Console;

namespace PreTalxTix.Cli;

public sealed class Remote
{
    private readonly AppConfig _config;

    public Remote(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Run a manage.sh subcommand via SSH.NET (non-interactive, streams output).
    /// </summary>
    public int RunCommand(string manageArgs)
    {
        EnsureConfigured();
        var cmd = $"cd {_config.ProjectDir} && ./manage.sh {manageArgs}";

        var (user, hostname) = _config.ParseHost();

        using var client = CreateSshClient(user, hostname);
        try
        {
            client.Connect();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]SSH connection failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        using var command = client.CreateCommand(cmd);
        command.CommandTimeout = TimeSpan.FromMinutes(30);

        var asyncResult = command.BeginExecute();

        // Stream stdout
        using var stdout = command.OutputStream;
        using var stderr = command.ExtendedOutputStream;

        var buffer = new byte[4096];
        while (!asyncResult.IsCompleted || stdout.CanRead)
        {
            var read = stdout.Read(buffer, 0, buffer.Length);
            if (read > 0)
            {
                Console.Write(System.Text.Encoding.UTF8.GetString(buffer, 0, read));
            }
            else if (!asyncResult.IsCompleted)
            {
                Thread.Sleep(100);
            }
        }

        command.EndExecute(asyncResult);

        // Flush any remaining stderr
        if (stderr.CanRead)
        {
            var errBuffer = new byte[4096];
            int errRead;
            while ((errRead = stderr.Read(errBuffer, 0, errBuffer.Length)) > 0)
            {
                Console.Error.Write(System.Text.Encoding.UTF8.GetString(errBuffer, 0, errRead));
            }
        }

        return command.ExitStatus ?? 1;
    }

    /// <summary>
    /// Run a manage.sh subcommand via native ssh (interactive, with TTY).
    /// Used for: logs, shell, restore.
    /// </summary>
    public int RunInteractive(string manageArgs)
    {
        EnsureConfigured();
        var remoteCmd = $"cd {_config.ProjectDir} && ./manage.sh {manageArgs}";

        var sshArgs = new List<string> { "-t" };

        if (!string.IsNullOrWhiteSpace(_config.KeyFile))
        {
            sshArgs.Add("-i");
            sshArgs.Add(ExpandPath(_config.KeyFile));
        }

        sshArgs.Add(_config.Host);
        sshArgs.Add(remoteCmd);

        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            UseShellExecute = false,
        };

        foreach (var arg in sshArgs)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to start ssh process.[/]");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to launch ssh:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[grey]Make sure 'ssh' is on your PATH (built-in on Windows 10+, macOS, Linux).[/]");
            return 1;
        }
    }

    /// <summary>
    /// Fetch the remote .env file to read DOMAIN for display purposes.
    /// </summary>
    public string? GetRemoteDomain()
    {
        EnsureConfigured();
        var (user, hostname) = _config.ParseHost();

        try
        {
            using var client = CreateSshClient(user, hostname);
            client.Connect();
            using var cmd = client.RunCommand(
                $"grep '^DOMAIN=' {_config.ProjectDir}/.env 2>/dev/null | cut -d= -f2");
            var domain = cmd.Result.Trim();
            return string.IsNullOrWhiteSpace(domain) || domain == "yourdomain.com" ? null : domain;
        }
        catch
        {
            return null;
        }
    }

    private SshClient CreateSshClient(string user, string hostname)
    {
        var connectionInfo = CreateConnectionInfo(user, hostname);
        return new SshClient(connectionInfo);
    }

    private ConnectionInfo CreateConnectionInfo(string user, string hostname)
    {
        var authMethods = new List<AuthenticationMethod>();

        // Try explicit key file first
        if (!string.IsNullOrWhiteSpace(_config.KeyFile))
        {
            var keyPath = ExpandPath(_config.KeyFile);
            if (File.Exists(keyPath))
            {
                var pkFile = TryLoadPrivateKey(keyPath);
                if (pkFile != null)
                    authMethods.Add(new PrivateKeyAuthenticationMethod(user, pkFile));
            }
        }

        // Try default key locations
        var sshDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        foreach (var keyName in new[] { "id_ed25519", "id_rsa", "id_ecdsa" })
        {
            var keyPath = Path.Combine(sshDir, keyName);
            if (File.Exists(keyPath))
            {
                try
                {
                    authMethods.Add(new PrivateKeyAuthenticationMethod(user,
                        new PrivateKeyFile(keyPath)));
                }
                catch
                {
                    // Key might be passphrase-protected — skip, native ssh will handle it
                }
            }
        }

        if (authMethods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No usable SSH keys found. Falling back to password auth.[/]");
            var password = AnsiConsole.Prompt(
                new TextPrompt<string>("SSH password:").Secret());
            authMethods.Add(new PasswordAuthenticationMethod(user, password));
        }

        return new ConnectionInfo(hostname, 22, user, authMethods.ToArray());
    }

    private static PrivateKeyFile? TryLoadPrivateKey(string keyPath)
    {
        try
        {
            return new PrivateKeyFile(keyPath);
        }
        catch (Renci.SshNet.Common.SshPassPhraseNullOrEmptyException)
        {
            var passphrase = AnsiConsole.Prompt(
                new TextPrompt<string>($"Passphrase for [green]{Path.GetFileName(keyPath)}[/]:")
                    .Secret());
            try
            {
                return new PrivateKeyFile(keyPath, passphrase);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Could not load key {Path.GetFileName(keyPath)}:[/] {Markup.Escape(ex.Message)}");
                return null;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not load key {Path.GetFileName(keyPath)}:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private void EnsureConfigured()
    {
        if (!_config.IsConfigured)
        {
            AnsiConsole.MarkupLine("[red]Not connected.[/] Run [yellow]ptx connect <user@host>[/] first.");
            Environment.Exit(1);
        }
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        return path;
    }
}
