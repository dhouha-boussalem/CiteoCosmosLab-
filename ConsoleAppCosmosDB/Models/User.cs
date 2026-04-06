// Models/User.cs
using System.Text.Json.Serialization;

namespace CiteoCosmosLab.Models;

public class User
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ClientId { get; set; } = default!; // partition key
    public string Username { get; set; } = default!;
    public string PasswordHash { get; set; } = default!; // JAMAIS le mot de passe en clair
    public string Role { get; set; } = "User"; // User, Manager, Admin
    public string Type { get; set; } = "User"; // discriminateur
}