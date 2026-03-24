// Models/Order.cs
using System.Text.Json.Serialization;

namespace CiteoCosmosLab.Models;

public class Order
{
    // "id" en minuscule = obligatoire pour Cosmos DB, c'est l'identifiant unique du document
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // C'est notre PARTITION KEY — toutes les requêtes par client seront ultra rapides
    public string ClientId { get; set; } = default!;

    public string ClientName { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Draft"; // Draft, Submitted, Validated

    // Dénormalisé : les lignes DANS la commande (pas de table séparée)
    public List<OrderLine> Lines { get; set; } = new();

    // Cosmos DB ajoute automatiquement ce champ pour le versioning optimiste
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

public class OrderLine
{
    public string ProductCode { get; set; } = default!;
    public string Label { get; set; } = default!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;
}