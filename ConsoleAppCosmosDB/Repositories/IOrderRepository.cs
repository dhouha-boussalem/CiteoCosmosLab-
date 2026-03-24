// Repositories/IOrderRepository.cs
using CiteoCosmosLab.Models;

namespace CiteoCosmosLab.Repositories;

public interface IOrderRepository
{
    Task<Order> CreateAsync(Order order);
    Task<Order?> GetByIdAsync(string id, string clientId);
    Task<IEnumerable<Order>> GetByClientAsync(string clientId);
    Task<Order> UpdateAsync(Order order);
    Task DeleteAsync(string id, string clientId);
}