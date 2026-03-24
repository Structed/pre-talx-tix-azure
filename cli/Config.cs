using System.Text.Json;
using Spectre.Console;

namespace PreTalxTix.Cli;

public sealed class AppConfig
{
    public string Host { get; set; } = "";
    public string KeyFile { get; set; } = "";
    public string ProjectDir { get; set; } = "~/pre-talx-tix-azure";

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pretalxtix");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    public (string User, string Hostname) ParseHost()
    {
        if (Host.Contains('@'))
        {
            var parts = Host.Split('@', 2);
            return (parts[0], parts[1]);
        }
        return ("root", Host);
    }

    public void RunConnect(string? hostArg)
    {
        var host = hostArg;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = AnsiConsole.Ask<string>("SSH connection ([green]user@host[/]):");
        }

        Host = host;

        var keyFile = AnsiConsole.Ask("SSH key file ([grey]leave empty for ssh-agent[/]):", "");
        if (!string.IsNullOrWhiteSpace(keyFile))
            KeyFile = keyFile;

        var projectDir = AnsiConsole.Ask("Remote project directory:", ProjectDir);
        ProjectDir = projectDir;

        Save();

        AnsiConsole.MarkupLine($"[green]✓[/] Saved to [blue]{ConfigPath}[/]");
        AnsiConsole.MarkupLine($"  Host: [yellow]{Host}[/]");
        if (!string.IsNullOrWhiteSpace(KeyFile))
            AnsiConsole.MarkupLine($"  Key:  [yellow]{KeyFile}[/]");
        AnsiConsole.MarkupLine($"  Dir:  [yellow]{ProjectDir}[/]");
    }
}
