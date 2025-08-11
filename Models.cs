using System.Net.Http.Headers;

namespace CliTest;

public record CliDeployment
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string State { get; init; }

    public required DateTime StartTime { get; init; }

    public DateTime? EndTime { get; init; }

    public required CliDeploymentOperation[] Operations { get; init; }

    public bool IsEntryPoint { get; init; }
}

public record CliDeploymentOperation
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required string State { get; init; }

    public required DateTime StartTime { get; init; }

    public DateTime? EndTime { get; init; }

    public string? Error { get; init; }
}
