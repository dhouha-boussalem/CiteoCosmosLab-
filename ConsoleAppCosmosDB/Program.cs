using CiteoCosmosLab.Data;
using CiteoCosmosLab.Models;
using CiteoCosmosLab.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using CiteoCosmosLab.Services;

using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using User = CiteoCosmosLab.Models.User;

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
//builder.Services.AddHostedService<OrderChangeFeedService>();

builder.Services.AddScoped<IOrderRepository, CosmosOrderRepository>();
builder.Services.AddDbContext<CiteoDbContext>(options =>
{       
    options.UseCosmos(
        accountEndpoint: "https://127.0.0.1:8081",
        accountKey: "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
        databaseName: "CiteoDb",
        cosmosOptionsAction: cosmosBuilder =>
        {
            cosmosBuilder.HttpClientFactory(() =>
            {
                var handler = new HttpClientHandler 
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                return new HttpClient(handler, disposeHandler: false);
            });
            cosmosBuilder.ConnectionMode(ConnectionMode.Gateway);
            cosmosBuilder.LimitToEndpoint();
        }
    );
    options.UseCamelCaseNamingConvention();
});

// --- JWT Configuration ---
var jwtKey = "MaCleSecreteSuperLongue-MinimumTrentaDeuxCaracteres!";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "CiteoPortal",
            ValidAudience = "CiteoClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

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
    var dbContext = scope.ServiceProvider.GetRequiredService<CiteoDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    Console.WriteLine("EF Core initialisé !");
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


// --- Endpoints EF Core (pour comparer avec le SDK direct) ---

app.MapPost("/ef/orders", async (Order order, CiteoDbContext db) =>
{
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Created($"/ef/orders/{order.Id}", order);
});

app.MapGet("/ef/orders/client/{clientId}", async (string clientId, CiteoDbContext db) =>
{
    var orders = await db.Orders
        .Where(o => o.ClientId == clientId)
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync();

    return Results.Ok(orders);
});

app.MapGet("/ef/orders/{id}", async (string id, string clientId, CiteoDbContext db) =>
{
    // WithPartitionKey = indique à EF Core dans quelle partition chercher
    var order = await db.Orders
        .WithPartitionKey(clientId)
        .FirstOrDefaultAsync(o => o.Id == id);

    return order is not null ? Results.Ok(order) : Results.NotFound();
});


// POST /orders/submit — Créer une commande + un audit log en une seule transaction
app.MapPost("/orders/submit", async (Order order, CosmosClient cosmosClient) =>
{
    var container = cosmosClient.GetContainer("CiteoDb", "Orders");

    order.Status = "Submitted";

    var audit = new AuditLog
    {
        ClientId = order.ClientId,
        OrderId = order.Id,
        Action = "Submitted"
    };

    // Transactional Batch : tout passe ou tout échoue
    var batch = container.CreateTransactionalBatch(new PartitionKey(order.ClientId))
        .CreateItem(order)
        .CreateItem(audit);

    var batchResponse = await batch.ExecuteAsync();

    if (batchResponse.IsSuccessStatusCode)
    {
        Console.WriteLine($"Batch: {batchResponse.RequestCharge} RU");
        return Results.Created($"/orders/{order.Id}", order);
    }
    else
    {
        return Results.Problem($"Batch failed: {batchResponse.StatusCode}");
    }
});

// POST /auth/register — Créer un utilisateur
app.MapPost("/auth/register", async (LoginRequest request, CosmosClient cosmosClient) =>
{
    var container = cosmosClient.GetContainer("CiteoDb", "Orders");

    var user = new CiteoCosmosLab.Models.User
    {
        ClientId = "client-001", // en vrai, assigné par l'admin
        Username = request.Username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        Role = "User"
    };

    await container.CreateItemAsync(user, new PartitionKey(user.ClientId));
    return Results.Created($"/users/{user.Id}", new { user.Id, user.Username });
});

// POST /auth/login — Vérifie en base et retourne JWT + refresh token
app.MapPost("/auth/login", async (LoginRequest request, CosmosClient cosmosClient) =>
{
    var container = cosmosClient.GetContainer("CiteoDb", "Orders");

    // Cherche l'utilisateur par username
    var query = new QueryDefinition(
        "SELECT * FROM c WHERE c.type = 'User' AND c.username = @username")
        .WithParameter("@username", request.Username);

    var iterator = container.GetItemQueryIterator<User>(query);
    User? user = null;

    while (iterator.HasMoreResults)
    {
        var batch = await iterator.ReadNextAsync();
        user = batch.FirstOrDefault();
        if (user != null) break;
    }

    // Vérifie le mot de passe hashé
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Unauthorized();

    // Génère le JWT
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role),
        new Claim("ClientId", user.ClientId),
        new Claim("UserId", user.Id)
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes("MaCleSecreteSuperLongue-MinimumTrentaDeuxCaracteres!"));

    var token = new JwtSecurityToken(
        issuer: "CiteoPortal",
        audience: "CiteoClients",
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(30), // courte durée
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    );

    // Crée un refresh token en base
    var refreshToken = new RefreshToken
    {
        ClientId = user.ClientId,
        UserId = user.Id,
        ExpiresAt = DateTime.UtcNow.AddDays(7) // longue durée
    };

    await container.CreateItemAsync(refreshToken, new PartitionKey(user.ClientId));

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        refreshToken = refreshToken.Token
    });
});

// GET /orders/mine — Retourne les commandes du client authentifié
app.MapGet("/orders/mine", async (ClaimsPrincipal user, IOrderRepository repo) =>
{
    var clientId = user.FindFirst("ClientId")?.Value;
    if (clientId is null) return Results.Forbid();

    var orders = await repo.GetByClientAsync(clientId);
    return Results.Ok(orders);
}).RequireAuthorization();

app.Run();