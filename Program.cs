// See https://aka.ms/new-console-template for more information
using System.Collections.Immutable;
using CliTest;

CancellationTokenSource cts = new();
Console.CancelKeyPress += delegate (object? sender, ConsoleCancelEventArgs e)
{
    e.Cancel = false;
    cts.Cancel();
};

var lineCount = 0;
var startTime = DateTime.UtcNow;
while (true)
{
    if (cts.Token.IsCancellationRequested)
    {
        break;
    }

    var deployments = DeploymentSimulator.Simulate(startTime);

    Console.Write(Renderer.HideCursor);
    var output = Renderer.Render(deployments, lineCount);
    lineCount = output.Count(c => c == '\n');
    Console.Write(output);
    Console.Write(Renderer.ShowCursor);

    if (deployments.SelectMany(d => d.Operations).All(op => op.EndTime != null))
    {
        break;
    }

    Thread.Sleep(50);
}

public static class DeploymentSimulator
{
    public static ImmutableArray<Deployment> Simulate(DateTime startTime)
    {
        Deployment[] deployments = [
            new() {
                Id = "/deployments/RootDeployment",
                Name = "RootDeployment",
                IsEntryPoint = true,
                Operations = [
                    new() {
                        Id = "/resourceGroups/MyResourceGroup",
                        Name = "MyResourceGroup",
                        Type = "Microsoft.Resources/resourceGroups",
                        State = "Succeeded",
                        StartTime = startTime,
                        EndTime = startTime.AddMilliseconds(2359),
                    },
                    new() {
                        Id = "/deployments/NestedDeployment1",
                        Name = "NestedDeployment1",
                        Type = "Microsoft.Resources/deployments",
                        State = "Succeeded",
                        StartTime = startTime.AddMilliseconds(1294),
                        EndTime = startTime.AddSeconds(2),
                    },
                    new() {
                        Id = "/deployments/NestedDeployment2",
                        Name = "NestedDeployment2",
                        Type = "Microsoft.Resources/deployments",
                        State = "Failed",
                        StartTime = startTime.AddMilliseconds(9823),
                        EndTime = startTime.AddSeconds(11),
                        Error = "Some error occurred"
                    }
                ],
            },
            new() {
                Id = "/deployments/NestedDeployment1",
                Name = "NestedDeployment1",
                IsEntryPoint = false,
                Operations = [
                    new() {
                        Id = "/virtualMachines/MyVm",
                        Name = "MyVm",
                        Type = "Microsoft.Compute/virtualMachines",
                        State = "Succeeded",
                        StartTime = startTime.AddMilliseconds(423),
                        EndTime = startTime.AddSeconds(5)
                    }
                ],
            },
            new() {
                Id = "/deployments/NestedDeployment2",
                Name = "NestedDeployment2",
                IsEntryPoint = false,
                Operations = [
                    new() {
                        Id = "/storageAccounts/MyStorageAccount",
                        Name = "MyStorageAccount",
                        Type = "Microsoft.Storage/storageAccounts",
                        State = "Failed",
                        StartTime = startTime.AddMilliseconds(1248),
                        EndTime = startTime.AddSeconds(13),
                        Error = "Quota exceeded"
                    }
                ],
            },
        ];

        var timeNow = DateTime.UtcNow;

        return [.. deployments
            .Select(d => d with
            {
                Operations = [.. d.Operations.Where(x => x.StartTime < timeNow).Select(op => op with
                {
                    State = op.EndTime < timeNow ? op.State : "Running",
                    EndTime = op.EndTime < timeNow ? op.EndTime : null,
                    Error = op.EndTime < timeNow ? op.Error : null,
                })],
            })];
    }
}