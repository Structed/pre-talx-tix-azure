using System.Diagnostics;
using Spectre.Console;

namespace TixTalk.Cli;

/// <summary>
/// Tears down a Pulumi stack by running <c>pulumi destroy</c>.
/// </summary>
public static class Teardown
{
    public static int Run()
    {
        AnsiConsole.Write(new Rule("[red]Teardown Environment[/]").RuleStyle("red"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]This will destroy all Azure resources for the selected stack.[/]");
        AnsiConsole.WriteLine();

        // Select stack
        var stack = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which [green]stack[/] to tear down?")
                .AddChoices(new[] { "dev", "prod" }));

        if (stack == "prod")
        {
            AnsiConsole.MarkupLine("[red bold]WARNING: You are about to destroy the PRODUCTION environment![/]");
            if (!AnsiConsole.Confirm("[red]Are you absolutely sure?[/]", false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                return 0;
            }
        }

        if (!AnsiConsole.Confirm($"Destroy all resources in the [yellow]{stack}[/] stack?", false))
        {
            AnsiConsole.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        // Find infra directory
        var infraDir = FindInfraDir();
        if (infraDir == null)
        {
            AnsiConsole.MarkupLine("[red]Could not find infra/ directory.[/]");
            AnsiConsole.MarkupLine("[grey]Ensure you are running from the tixtalk repository root.[/]");
            return 1;
        }

        // Select the stack
        AnsiConsole.MarkupLine($"[grey]Selecting stack: {stack}[/]");
        if (RunPulumi($"stack select {stack}", infraDir) != 0)
        {
            AnsiConsole.MarkupLine($"[red]Stack '{stack}' not found. Has it been provisioned?[/]");
            return 1;
        }

        // Run pulumi destroy
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Running pulumi destroy...[/]");
        AnsiConsole.WriteLine();

        var result = RunPulumi("destroy --yes", infraDir);
        if (result != 0)
        {
            AnsiConsole.MarkupLine("[red]Teardown failed.[/] Check the output above.");
            return 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓[/] Stack [yellow]{stack}[/] destroyed successfully.");
        AnsiConsole.MarkupLine("[grey]All Azure resources have been removed.[/]");
        return 0;
    }

    private static string? FindInfraDir()
    {
        // Try relative to current directory first
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "infra"),
            Path.Combine(AppContext.BaseDirectory, "..", "infra"),
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "Pulumi.yaml")))
                return Path.GetFullPath(dir);
        }

        return null;
    }

    private static int RunPulumi(string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pulumi",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
        };

        try
        {
            var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode ?? 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to run pulumi:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
