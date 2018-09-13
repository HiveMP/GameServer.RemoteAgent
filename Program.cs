using HiveMP.GameServer.Api;
using k8s;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HiveMP.GameServer.RemoteAgent
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("This is the HiveMP game server remote agent.");
            Console.WriteLine("Obtaining cluster credentials...");

            var projectId = Environment.GetEnvironmentVariable("PROJECT_ID");
            var clusterId = Environment.GetEnvironmentVariable("CLUSTER_ID");
            var clusterHost = Environment.GetEnvironmentVariable("CLUSTER_HOST");
            var clusterNamespace = Environment.GetEnvironmentVariable("CLUSTER_NAMESPACE");
            var apiKey = Environment.GetEnvironmentVariable("API_KEY");
            var hiveEndpoint = Environment.GetEnvironmentVariable("HIVEMP_ENDPOINT");
            if (hiveEndpoint == null)
            {
                hiveEndpoint = "https://game-server-api.hivemp.com/v1";
            }

            Kubernetes client;

            if (clusterHost != null)
            {
                var config = new KubernetesClientConfiguration { Host = clusterHost };
                client = new Kubernetes(config);
            }
            else
            {
                var config = KubernetesClientConfiguration.InClusterConfig();
                client = new Kubernetes(config);
            }

            var exitSemaphore = new SemaphoreSlim(0);
            var cancellationTokenSource = new CancellationTokenSource();
            try
            {
                Console.WriteLine("Connecting to HiveMP notification endpoint (" + hiveEndpoint + ")...");
                var notifyClient = new GameServerNotifyClient
                {
                    BaseUrl = hiveEndpoint
                };
                var protocol = await notifyClient.KubernetesNotifyGETAsync(new KubernetesNotifyGETRequest
                {
                    ProjectId = projectId,
                    ClusterId = clusterId,
                    ClusterSecretToken = apiKey,
                });
#pragma warning disable CS4014
                Task.Run(async () =>
                {
                    await protocol.WaitForDisconnect(cancellationTokenSource.Token);

                    Console.WriteLine("Disconnected from HiveMP.");

                    exitSemaphore.Release();
                });
#pragma warning restore CS4014

                Console.WriteLine("Connecting to Kubernetes pod watch endpoint...");
                var watcher = await client.WatchNamespacedPodAsync(null, clusterNamespace);

                watcher.OnError += (error) =>
                {
                    Console.Error.WriteLine(error);
                };

                watcher.OnEvent += (eventType, pod) =>
                {
                    var labels = pod?.Metadata?.Labels;
                    if (labels == null)
                    {
                        return;
                    }

                    if (!labels.ContainsKey("projectId") &&
                        !labels.ContainsKey("gameServerClusterId") &&
                        !labels.ContainsKey("gameServerId"))
                    {
                        return;
                    }

                    var podProjectId = labels["projectId"];
                    var podGameServerClusterId = labels["gameServerClusterId"];
                    var podGameServerId = labels["gameServerId"];

                    if (!string.IsNullOrWhiteSpace(podProjectId) &&
                        !string.IsNullOrWhiteSpace(podGameServerClusterId) &&
                        !string.IsNullOrWhiteSpace(podGameServerId) &&
                        podProjectId == projectId &&
                        podGameServerClusterId == clusterId)
                    {
                        Console.WriteLine("Forwarding notification for " + podProjectId + "/" + podGameServerClusterId + "/" + podGameServerId + "...");
                        Task.Run(async () => await protocol.SendPn(new PodNotifyMessage
                        {
                            Gsid = podGameServerId
                        }));
                    }
                };

                watcher.OnClosed += () =>
                {
                    Console.WriteLine("Disconnected from Kubernetes.");
                    exitSemaphore.Release();
                };
                
                Console.WriteLine("Now listening for Kubernetes pod events.");
                await exitSemaphore.WaitAsync();

                cancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);

                exitSemaphore.Release();
            }
        }
    }
}
