using Spectre.Console;

namespace PreTalxTix.Cli;

public static class Menu
{
    public static int Show(AppConfig config, Remote remote)
    {
        if (!config.IsConfigured)
        {
            AnsiConsole.MarkupLine("[yellow]No server configured yet.[/]");
            config.RunConnect(null);
            AnsiConsole.WriteLine();
        }

        var domain = remote.GetRemoteDomain();
        var domainLabel = domain != null ? $" [grey]({domain})[/]" : "";

        AnsiConsole.Write(new Rule($"[blue]Pretix + Pretalx Manager[/]{domainLabel}")
            .RuleStyle("blue"));
        AnsiConsole.WriteLine();

        if (domain != null)
        {
            AnsiConsole.MarkupLine($"  [green]Pretix:[/]  https://tickets.{domain}");
            AnsiConsole.MarkupLine($"  [green]Pretalx:[/] https://talks.{domain}");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"  [grey]Server:[/] [yellow]{config.Host}[/]");
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]What would you like to do?[/]")
                .HighlightStyle("green")
                .AddChoices(
                    "Status",
                    "Update containers",
                    "View logs",
                    "Restart services",
                    "Backup databases",
                    "Restore database",
                    "Open shell",
                    "Update DNS records",
                    "Stop services",
                    "Start services",
                    "─── First-time setup ───",
                    "Server setup (install Docker)",
                    "Deploy (first time)",
                    "─── Configuration ───",
                    "Change connection",
                    "Quit"));

        AnsiConsole.WriteLine();

        return choice switch
        {
            "Status" => remote.RunCommand("status"),
            "Update containers" => PromptUpdate(remote),
            "View logs" => PromptLogs(remote),
            "Restart services" => remote.RunCommand("restart"),
            "Backup databases" => PromptBackup(remote),
            "Restore database" => remote.RunInteractive("restore"),
            "Open shell" => PromptShell(remote),
            "Update DNS records" => remote.RunCommand("dns"),
            "Stop services" => remote.RunCommand("stop"),
            "Start services" => remote.RunCommand("start"),
            "Server setup (install Docker)" => remote.RunCommand("setup"),
            "Deploy (first time)" => remote.RunCommand("deploy"),
            "Change connection" => ChangeConnection(config),
            "Quit" => 0,
            _ => 0,
        };
    }

    private static int PromptUpdate(Remote remote)
    {
        var pretixTag = AnsiConsole.Ask("Pretix tag [grey](leave empty to keep current)[/]:", "");
        var pretalxTag = AnsiConsole.Ask("Pretalx tag [grey](leave empty to keep current)[/]:", "");

        var args = "update";
        if (!string.IsNullOrWhiteSpace(pretixTag))
            args += $" --pretix {pretixTag}";
        if (!string.IsNullOrWhiteSpace(pretalxTag))
            args += $" --pretalx {pretalxTag}";

        return remote.RunCommand(args);
    }

    private static int PromptLogs(Remote remote)
    {
        var service = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which service?")
                .AddChoices("All services", "pretix", "pretalx", "caddy", "postgres", "redis"));

        var arg = service == "All services" ? "" : service;
        return remote.RunInteractive($"logs {arg}");
    }

    private static int PromptShell(Remote remote)
    {
        var service = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Open shell in which container?")
                .AddChoices("pretix", "pretalx", "postgres", "redis", "caddy"));

        return remote.RunInteractive($"shell {service}");
    }

    private static int PromptBackup(Remote remote)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Backup action:")
                .AddChoices("Run backup now", "Install daily cron job"));

        return action == "Install daily cron job"
            ? remote.RunCommand("backup --install-cron")
            : remote.RunCommand("backup");
    }

    private static int ChangeConnection(AppConfig config)
    {
        config.RunConnect(null);
        return 0;
    }
}
