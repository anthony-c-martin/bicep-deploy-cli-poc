using System.Collections.Immutable;
using System.Text;

namespace CliTest;

public class Renderer
{
    private static string GetLink(string tenantId, string id)
    {
        var url = $"https://portal.azure.com/#@{tenantId}/resource{Uri.EscapeDataString(id)}";
        var caption = $"{Gray}Portal{Reset}{Esc}";

        return $"{Esc}]8;;{url}{Esc}\\{caption}{Esc}]8;;{Esc}\\";
    }

    private static string RewindLines(int count) => $"{Esc}[{count}F";
    private static string EraseLine => $"{Esc}[K";

    private const char Esc = (char)27;
    public static string Orange { get; } = $"{Esc}[38;5;208m";
    public static string Green { get; } = $"{Esc}[38;5;77m";
    public static string Purple { get; } = $"{Esc}[38;5;141m";
    public static string Blue { get; } = $"{Esc}[38;5;39m";
    public static string Gray { get; } = $"{Esc}[38;5;246m";
    public static string Reset { get; } = $"{Esc}[0m";
    public static string Red { get; } = $"{Esc}[38;5;203m";
    public static string Bold { get; } = $"{Esc}[1m";

    public static string HideCursor { get; } = $"{Esc}[?25l";
    public static string ShowCursor { get; } = $"{Esc}[?25h";

    private ImmutableArray<CliDeployment> deployments = [];

    public void Refresh(ImmutableArray<CliDeployment> newDeployments)
    {
        lock (this)
        {
            deployments = newDeployments;
        }
    }

    public async Task RenderLoop(TimeSpan delay, CancellationToken cancellationToken)
    {
        var lineCount = 0;
        try
        {
            while (true)
            {
                Render(deployments, ref lineCount);
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Always render the final state before exiting
            Render(deployments, ref lineCount);
        }
    }

    public static void Render(ImmutableArray<CliDeployment> deployments, ref int lineCount)
    {
        Console.Write(Renderer.HideCursor);
        var output = GetOutput(deployments, lineCount);
        Console.Write(output);
        Console.Write(Renderer.ShowCursor);
        lineCount = output.Count(c => c == '\n');
    }

    public static string GetOutput(ImmutableArray<CliDeployment> deployments, int prevLineCount)
    {
        var sb = new StringBuilder();
        var entrypoint = deployments.SingleOrDefault(d => d.IsEntryPoint);
        if (entrypoint is null)
        {
            return "";
        }

        if (prevLineCount > 0)
        {
            sb.Append(RewindLines(prevLineCount));
        }

        var indent = 0;
        AppendLine(indent, $"{EraseLine}{entrypoint.Name} {GetState(entrypoint.State)} ({GetDuration(entrypoint.StartTime, entrypoint.EndTime)})", sb);
        RenderOperations(sb, deployments, entrypoint, indent);

        return sb.ToString();
    }

    private static void RenderOperations(StringBuilder sb, ImmutableArray<CliDeployment> deployments, CliDeployment deployment, int indent)
    {
        var tenantId = Guid.NewGuid();
        var orderedOperations = deployment.Operations
            .OrderBy(x => x.EndTime is { } ? x.EndTime.Value : DateTime.MaxValue)
            .ThenBy(x => x.StartTime)
            .ToList();

        foreach (var operation in orderedOperations)
        {
            AppendLine(indent, $"{EraseLine}{operation.Name} {GetState(operation.State)} ({GetDuration(operation.StartTime, operation.EndTime)})", sb);
            if (operation.Error is not null)
            {
                AppendLine(indent + 1, $"{EraseLine}{Red}{operation.Error}{Reset}", sb);
            }

            if (operation.Type.Equals("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
            {
                var subDeployment = deployments.Single(d => d.Id == operation.Id);
                RenderOperations(sb, deployments, subDeployment, indent + 1);
            }
        }
    }

    private static string GetState(string state) => state.ToLowerInvariant() switch
    {
        "succeeded" => $"{Bold}{Green}{state}{Reset}",
        "failed" => $"{Bold}{Red}{state}{Reset}",
        "accepted" => $"{Bold}{Gray}{state}{Reset}",
        "running" => $"{Bold}{Gray}{state}{Reset}",
        _ => $"{Bold}{state}{Reset}",
    };

    private static string GetDuration(DateTime startTime, DateTime? endTime)
    {
        var duration = (endTime ?? DateTime.UtcNow) - startTime;
        return $"{duration.TotalSeconds:0.0}s";
    }

    private static void AppendLine(int indent, string text, StringBuilder sb)
    {
        if (text.Length == 0)
        {
            sb.AppendLine();
            return;
        }

        while (text.Length > 0)
        {
            var indentString = new string(' ', indent * 2);
            var wrapIndex = Math.Min(text.Length + indentString.Length, TerminalWidth - 1);
            sb.Append(indentString);
            sb.Append(text[..(wrapIndex - indentString.Length)]);
            sb.AppendLine();
            text = text[(wrapIndex - indentString.Length)..];
        }
    }

    private static int TerminalWidth => (Console.IsOutputRedirected || Console.BufferWidth == 0) ? int.MaxValue : Console.BufferWidth;
    private static int TerminalHeight => (Console.IsOutputRedirected || Console.BufferHeight == 0) ? int.MaxValue : Console.BufferHeight;
}
