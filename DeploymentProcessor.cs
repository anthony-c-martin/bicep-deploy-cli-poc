using Azure.ResourceManager;
using Azure.Deployments.ClientTools;
using Azure.ResourceManager.Resources;
using Azure.Core;
using Azure.ResourceManager.Resources.Models;
using Azure.Deployments.Core.Definitions;
using Microsoft.WindowsAzure.ResourceStack.Common.Json;
using System.ComponentModel.DataAnnotations;

namespace CliTest;

public class DeploymentProcessor(BicepClient bicepClient, ArmClient armClient, Renderer renderer)
{
    public async Task ProcessAsync(
        string subscriptionId,
        string resourceGroupName,
        string bicepParamPath,
        string? deploymentName = null,
        CancellationToken cancellationToken = default)
    {
        deploymentName ??= "main";
        var result = await bicepClient.CompileParams(new(
            Path: bicepParamPath,
            ParameterOverrides: []));

        if (result.Template is not { } template ||
            result.Parameters is not { } parameters)
        {
            throw new Exception($"Failed to compile Bicep parameters");
        }

        var deploymentsClient = armClient.GetResourceGroupResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}")).GetArmDeployments();
        var paramsDefinition = parameters.FromJson<DeploymentParametersDefinition>();
        var deploymentProperties = new ArmDeploymentProperties(ArmDeploymentMode.Incremental)
        {
            Template = BinaryData.FromString(template),
            Parameters = BinaryData.FromString(paramsDefinition.Parameters.ToJson()),
        };
        var armDeploymentContent = new ArmDeploymentContent(deploymentProperties);

        var started = await deploymentsClient.CreateOrUpdateAsync(Azure.WaitUntil.Started, deploymentName, armDeploymentContent, cancellationToken);

        var complete = false;
        while (!complete)
        {
            List<CliDeployment> deployments = [];
            Queue<string> deploymentsToFetch = [];
            deploymentsToFetch.Enqueue($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Resources/deployments/{deploymentName}");

            while (deploymentsToFetch.Any())
            {
                var id = deploymentsToFetch.Dequeue();
                var deployment = await GetCliDeploymentAsync(id, deployments.Count == 0, cancellationToken);
                deployments.Add(deployment);

                foreach (var op in deployment.Operations)
                {
                    if (op.Type.Equals("Microsoft.Resources/deployments", StringComparison.OrdinalIgnoreCase))
                    {
                        deploymentsToFetch.Enqueue(op.Id);
                    }
                }
            }

            renderer.Refresh([.. deployments]);

            complete = IsTerminal(deployments.Single(x => x.IsEntryPoint).State);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task<CliDeployment> GetCliDeploymentAsync(string resourceId, bool isEntryPoint, CancellationToken cancellationToken)
    {
        List<CliDeploymentOperation> cliOperations = [];

        var id = ResourceIdentifier.Parse(resourceId);
        var deploymentsClient = armClient.GetArmDeploymentResource(id);

        var deployment = await deploymentsClient.GetAsync(cancellationToken);
        await foreach (var operation in deploymentsClient.GetDeploymentOperationsAsync(cancellationToken: cancellationToken))
        {
            if (operation.Properties.TargetResource is null)
            {
                continue;
            }

            var operationState = operation.Properties.ProvisioningState.ToString();
            var cliOperation = new CliDeploymentOperation
            {
                Id = operation.Properties.TargetResource.Id,
                Name = operation.Properties.TargetResource.ResourceName,
                Type = operation.Properties.TargetResource.ResourceType!,
                StartTime = operation.Properties.Timestamp!.Value.DateTime,
                EndTime = IsTerminal(operationState) ? operation.Properties.Timestamp!.Value.Add(operation.Properties.Duration!.Value).DateTime : null,
                State = operationState,
                Error = GetError(operation),
            };
            cliOperations.Add(cliOperation);
        }

        var deploymentState = deployment.Value.Data.Properties.ProvisioningState!.Value.ToString();
        return new CliDeployment
        {
            Id = deployment.Value.Data.Id.ToString(),
            Name = deployment.Value.Data.Name,
            StartTime = deployment.Value.Data.Properties.Timestamp!.Value.DateTime,
            EndTime = IsTerminal(deploymentState) ? deployment.Value.Data.Properties.Timestamp!.Value.Add(deployment.Value.Data.Properties.Duration!.Value).DateTime : null,
            Operations = [.. cliOperations],
            IsEntryPoint = isEntryPoint,
            State = deploymentState,
        };
    }

    private static string? GetError(ArmDeploymentOperation operation)
    {
        if (operation.Properties.StatusMessage?.Error is not { } error)
        {
            return null;
        }

        return $"{error.Code}: {error.Message}";
    }

    private static bool IsTerminal(string state)
        => state.ToLowerInvariant() is "succeeded" or "failed" or "canceled";
}