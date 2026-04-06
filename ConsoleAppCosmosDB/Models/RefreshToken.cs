// Models/RefreshToken.cs
using System.Text.Json.Serialization;

namespace CiteoCosmosLab.Models;

public class RefreshToken
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public string Token { get; set; } = Guid.NewGuid().ToString();
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public string Type { get; set; } = "RefreshToken";
}