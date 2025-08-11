// See https://aka.ms/new-console-template for more information
using System.Collections.Immutable;
using Azure.Deployments.ClientTools;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using CliTest;
using CommandLine;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;

namespace CliTest;

public class CommandLineOptions
{
    public CommandLineOptions(string subscriptionId, string resourceGroup, string file)
    {
        SubscriptionId = subscriptionId;
        ResourceGroup = resourceGroup;
        File = file;
    }

    [Option("subscription-id", Required = true, HelpText = "The target Azure subscription ID.")]
    public string SubscriptionId { get; }

    [Option("resource-group", Required = true, HelpText = "The target Azure resource group name.")]
    public string ResourceGroup { get; }

    [Option("file", Required = true, HelpText = "The path to the .bicepparam file.")]
    public string File { get; }
}

class Program
{
    public static async Task<int> Main(string[] args)
    {
        var program = new Program();

        return await RunWithCancellationAsync(token => program.Main(args, token));
    }

    private async Task<int> Main(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            await Parser.Default.ParseArguments<CommandLineOptions>(args)
                .MapResult(
                    opts => Run(opts, cancellationToken),
                    async errors =>
                    {
                        foreach (var error in errors)
                        {
                            await Console.Error.WriteLineAsync(error.ToString());
                        }

                        return 1;
                    });

            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteAsync(ex.ToString());
            return 1;
        }
    }

    private async Task<int> Run(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddSingleton<DeploymentProcessor>();

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddArmClient("00000000-0000-0000-0000-000000000000");
            clientBuilder.UseCredential(new DefaultAzureCredential());
        });

        // Initialize BicepClient synchronously for DI registration
        var version = "0.37.4";
        var homePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".bicep/bin/{version}/bicep");
        if (!Directory.Exists(homePath))
        {
            Directory.CreateDirectory(homePath);
            await BicepClient.DownloadAndInstall(homePath, bicepVersion: version, cancellationToken: cancellationToken);
        }

        var exeName = OperatingSystem.IsWindows() ? "bicep.exe" : "bicep";
        var path = Path.Combine(homePath, exeName);

        var bicepClient = await BicepClient.Initialize(path, cancellationToken);
        services.AddSingleton<BicepClient>(bicepClient);
        services.AddSingleton<DeploymentProcessor>();
        services.AddSingleton<Renderer>();

        var provider = services.BuildServiceProvider();

        var onComplete = new CancellationTokenSource();
        await Task.WhenAll([
            RenderLoop(provider.GetRequiredService<Renderer>(), onComplete.Token),
            Process(provider.GetRequiredService<DeploymentProcessor>(), options, cancellationToken, onComplete),
        ]);

        return 0;
    }
    
    private async Task RenderLoop(Renderer renderer, CancellationToken cancellationToken)
    {
        await renderer.RenderLoop(TimeSpan.FromMilliseconds(50), cancellationToken);
    }

    private async Task Process(DeploymentProcessor processor, CommandLineOptions options, CancellationToken cancellationToken, CancellationTokenSource onComplete)
    {
        await processor.ProcessAsync(
            subscriptionId: options.SubscriptionId,
            resourceGroupName: options.ResourceGroup,
            bicepParamPath: options.File,
            cancellationToken: cancellationToken);

        await onComplete.CancelAsync();
    }

    private static async Task<int> RunWithCancellationAsync(Func<CancellationToken, Task<int>> runFunc)
    {
        var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            cancellationTokenSource.Cancel();
            e.Cancel = true;
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            cancellationTokenSource.Cancel();
        };

        try
        {
            return await runFunc(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationTokenSource.Token)
        {
            // this is expected - no need to rethrow
            return 1;
        }
    }
}