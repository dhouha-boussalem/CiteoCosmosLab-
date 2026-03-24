// Repositories/CosmosOrderRepository.cs
using System.Net;
using CiteoCosmosLab.Models;
using Microsoft.Azure.Cosmos;

namespace CiteoCosmosLab.Repositories;

public class CosmosOrderRepository : IOrderRepository
{
    private readonly Container _container;

    public CosmosOrderRepository(CosmosClient cosmosClient)
    {
        // On récupère le container — la database et le container sont créés au démarrage (dans Program.cs)
        _container = cosmosClient.GetContainer("CiteoDb", "Orders");
    }

    public async Task<Order> CreateAsync(Order order)
    {
        // CreateItemAsync = INSERT
        // Le 2e paramètre indique la partition key pour que Cosmos sache où stocker
        var response = await _container.CreateItemAsync(
            order,
            new PartitionKey(order.ClientId)
        );

        // response.RequestCharge te dit combien de RU ça a coûté — utile pour optimiser
        Console.WriteLine($"Create: {response.RequestCharge} RU");

        return response.Resource;
    }

    public async Task<Order?> GetByIdAsync(string id, string clientId)
    {
        try
        {
            // ReadItemAsync = POINT READ = l'opération la moins chère (~1 RU)
            // Tu fournis l'id + la partition key → Cosmos sait exactement où aller
            var response = await _container.ReadItemAsync<Order>(
                id,
                new PartitionKey(clientId)
            );

            Console.WriteLine($"Read: {response.RequestCharge} RU");
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IEnumerable<Order>> GetByClientAsync(string clientId)
    {
        // Query SQL-like — ici c'est IN-PARTITION car on filtre par clientId (la partition key)
        // Donc c'est efficace et pas cher
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.ClientId = @clientId ORDER BY c.CreatedAt DESC"
        ).WithParameter("@clientId", clientId);

        var iterator = _container.GetItemQueryIterator<Order>(query);
        var results = new List<Order>();

        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            Console.WriteLine($"Query batch: {batch.RequestCharge} RU");
            results.AddRange(batch);
        }

        return results;
    }

    public async Task<Order> UpdateAsync(Order order)
    {
        // ReplaceItemAsync = UPDATE (remplace le document ENTIER)
        // Pas de UPDATE partiel comme en SQL — tu remplaces tout le document
        var response = await _container.ReplaceItemAsync(
            order,
            order.Id,
            new PartitionKey(order.ClientId)
        );

        Console.WriteLine($"Update: {response.RequestCharge} RU");
        return response.Resource;
    }

    public async Task DeleteAsync(string id, string clientId)
    {
        var response = await _container.DeleteItemAsync<Order>(
            id,
            new PartitionKey(clientId)
        );

        Console.WriteLine($"Delete: {response.RequestCharge} RU");
    }
}