using CiteoCosmosLab.Models;
using CiteoCosmosLab.Repositories;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// --- Cosmos DB : enregistrement du client en Singleton ---
// Un seul CosmosClient pour toute l'app (thread-safe, gère son propre pool de connexions)
builder.Services.AddSingleton<CosmosClient>(_ =>
{
    return new CosmosClient(
        accountEndpoint: "https://127.0.0.1:8081",
        authKeyOrResourceToken: "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        clientOptions: new CosmosClientOptions
        {
            HttpClientFactory = () =>
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                return new HttpClient(handler, disposeHandler: false);
            },
            ConnectionMode = ConnectionMode.Gateway,
            RequestTimeout = TimeSpan.FromSeconds(30),
            LimitToEndpoint = true,
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        }
    );
});

builder.Services.AddScoped<IOrderRepository, CosmosOrderRepository>();

var app = builder.Build();

// --- Initialisation : créer la database et le container au démarrage ---
try
{
    using var scope = app.Services.CreateScope();
    var cosmosClient = scope.ServiceProvider.GetRequiredService<CosmosClient>();
    Console.WriteLine("Connexion à Cosmos DB...");
    var database = await cosmosClient.CreateDatabaseIfNotExistsAsync("CiteoDb");
    Console.WriteLine("Database créée !");
    await database.Database.CreateContainerIfNotExistsAsync("Orders", "/clientId");
    Console.WriteLine("Container créé !");
}
catch (Exception ex)
{
    Console.WriteLine($"ERREUR Cosmos DB: {ex.Message}");
    Console.WriteLine(ex.ToString());
}

// --- Endpoints ---

// POST /orders — Créer une commande
app.MapPost("/orders", async (Order order, IOrderRepository repo) =>
{
    var created = await repo.CreateAsync(order);
    return Results.Created($"/orders/{created.Id}", created);
});

// GET /orders/{id}?clientId=xxx — Lire une commande (point read)
app.MapGet("/orders/{id}", async (string id, string clientId, IOrderRepository repo) =>
{
    var order = await repo.GetByIdAsync(id, clientId);
    return order is not null ? Results.Ok(order) : Results.NotFound();
});

// GET /orders/client/{clientId} — Lister les commandes d'un client
app.MapGet("/orders/client/{clientId}", async (string clientId, IOrderRepository repo) =>
{
    var orders = await repo.GetByClientAsync(clientId);
    return Results.Ok(orders);
});

// PUT /orders/{id} — Modifier une commande
app.MapPut("/orders/{id}", async (string id, Order order, IOrderRepository repo) =>
{
    order.Id = id;
    var updated = await repo.UpdateAsync(order);
    return Results.Ok(updated);
});

// DELETE /orders/{id}?clientId=xxx — Supprimer une commande
app.MapDelete("/orders/{id}", async (string id, string clientId, IOrderRepository repo) =>
{
    await repo.DeleteAsync(id, clientId);
    return Results.NoContent();
});

app.Run();