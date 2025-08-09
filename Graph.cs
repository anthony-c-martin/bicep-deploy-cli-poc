using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Text;

namespace CliTest;

public record Deployment
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required DeploymentOperation[] Operations { get; init; }

    public bool IsEntryPoint { get; init; }
}

public record DeploymentOperation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required string State { get; init; }

    public required DateTime StartTime { get; init; }

    public DateTime? EndTime { get; init; }

    public string? Error { get; init; }
}

public static class Renderer
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

    public static string Render(ImmutableArray<Deployment> deployments, int prevLineCount)
    {
        var sb = new StringBuilder();
        var entrypoint = deployments.SingleOrDefault(d => d.IsEntryPoint);
        if (entrypoint is null ||
            !entrypoint.Operations.Any())
        {
            throw new InvalidOperationException("No content to render.");
        }

        if (prevLineCount > 0)
        {
            sb.Append(RewindLines(prevLineCount));
        }

        var startTime = entrypoint.Operations.Min(op => op.StartTime);
        RenderOperations(sb, deployments, entrypoint, startTime, "");

        return sb.ToString();
    }

    private static void RenderOperations(StringBuilder sb, ImmutableArray<Deployment> deployments, Deployment deployment, DateTime startTime, string indent)
    {
        var tenantId = Guid.NewGuid();
        var orderedOperations = deployment.Operations
            .OrderBy(x => x.EndTime is { } ? x.EndTime.Value : DateTime.MaxValue)
            .ThenBy(x => x.StartTime)
            .ToList();

        foreach (var operation in orderedOperations)
        {
            var duration = (operation.EndTime ?? DateTime.UtcNow) - operation.StartTime;
            var durationSeconds = duration.TotalSeconds.ToString("0.0");

            sb.AppendLine($"{EraseLine}{indent}{operation.Name} {GetState(operation)} ({durationSeconds}s)");
            if (operation.Error is not null)
            {
                sb.AppendLine($"{EraseLine}{indent}  {Red}{operation.Error}{Reset} -> {GetLink(tenantId.ToString(), operation.Id)}");
            }

            if (operation.Type.Equals("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
            {
                var subDeployment = deployments.Single(d => d.Id == operation.Id);
                RenderOperations(sb, deployments, subDeployment, startTime, indent + "  ");
            }
        }
    }

    private static string GetState(DeploymentOperation operation) => operation.State.ToLowerInvariant() switch
    {
        "succeeded" => $"{Bold}{Green}{operation.State}{Reset}",
        "failed" => $"{Bold}{Red}{operation.State}{Reset}",
        "running" => $"{Bold}{Gray}{operation.State}{Reset}",
        _ => $"{Bold}{operation.State}{Reset}",
    };
}