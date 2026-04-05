// Services/OrderChangeFeedService.cs
using Microsoft.Azure.Cosmos;
using CiteoCosmosLab.Models;

namespace CiteoCosmosLab.Services;

public class OrderChangeFeedService : BackgroundService
{
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<OrderChangeFeedService> _logger;

    public OrderChangeFeedService(CosmosClient cosmosClient, ILogger<OrderChangeFeedService> logger)
    {
        _cosmosClient = cosmosClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var container = _cosmosClient.GetContainer("CiteoDb", "Orders");

        // Le "leases" container stocke la position de lecture du Change Feed
        // C'est comme un offset Kafka — il sait où il en est
        var leaseContainer = await _cosmosClient
            .GetDatabase("CiteoDb")
            .CreateContainerIfNotExistsAsync("leases", "/id");

        _logger.LogInformation("Démarrage du Change Feed Processor...");

        var processor = container
            .GetChangeFeedProcessorBuilder<dynamic>("orderProcessor", HandleChangesAsync)
            .WithInstanceName("instance-1")
            .WithLeaseContainer(leaseContainer.Container)
            .WithStartTime(DateTime.UtcNow) // Commence à partir de maintenant
            .Build();

        await processor.StartAsync();
        _logger.LogInformation("Change Feed Processor démarré !");

        // Tourne en boucle jusqu'à l'arrêt de l'app
        await Task.Delay(Timeout.Infinite, stoppingToken);

        await processor.StopAsync();
    }

    private Task HandleChangesAsync(
        ChangeFeedProcessorContext context,
        IReadOnlyCollection<dynamic> changes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- Change Feed: {Count} changement(s) détecté(s) ---", (object)changes.Count);

        foreach (var doc in changes)
        {
            string id = doc.id?.ToString() ?? "unknown";
            string type = doc.type?.ToString() ?? "Order";
            string clientId = doc.clientId?.ToString() ?? "unknown";

            if (type == "AuditLog")
            {
                _logger.LogInformation("  [AUDIT] Client {ClientId} - Action: {Action}",
                    (object)clientId, (object)(doc.action?.ToString()));
            }
            else
            {
                _logger.LogInformation("  [ORDER] Client {ClientId} - Status: {Status} - Id: {Id}",
                    (object)clientId, (object)(doc.status?.ToString()), (object)id);
            }
        }

        // Ici en vrai tu ferais :
        // - Envoyer un email de confirmation
        // - Synchro vers SAP/Salesforce
        // - Mettre à jour un cache
        // - Publier un message sur Azure Service Bus

        return Task.CompletedTask;
    }
}