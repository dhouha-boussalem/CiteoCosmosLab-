// Models/AuditLog.cs
using System.Text.Json.Serialization;

namespace CiteoCosmosLab.Models;

public class AuditLog
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = default!; // même partition key que Order
    public string OrderId { get; set; } = default!;
    public string Action { get; set; } = default!; // "Created", "Submitted", "Validated"
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "AuditLog"; // discriminateur pour distinguer des Orders
}