// Data/CiteoDbContext.cs
using CiteoCosmosLab.Models;
using Microsoft.EntityFrameworkCore;

namespace CiteoCosmosLab.Data;

public class CiteoDbContext : DbContext
{
    public CiteoDbContext(DbContextOptions<CiteoDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToContainer("Orders");   
            entity.HasPartitionKey(o => o.ClientId);
            entity.HasKey(o => o.Id);
            entity.UseETagConcurrency();
            entity.HasNoDiscriminator();

            entity.Property(o => o.ClientId).ToJsonProperty("clientId");
            entity.Property(o => o.ClientName).ToJsonProperty("clientName");
            entity.Property(o => o.CreatedAt).ToJsonProperty("createdAt");
            entity.Property(o => o.Status).ToJsonProperty("status");

            entity.OwnsMany(o => o.Lines, lines =>
            {
                lines.ToJsonProperty("lines");
                lines.Property(l => l.ProductCode).ToJsonProperty("productCode");
                lines.Property(l => l.Label).ToJsonProperty("label");
                lines.Property(l => l.Quantity).ToJsonProperty("quantity");
                lines.Property(l => l.UnitPrice).ToJsonProperty("unitPrice");
                lines.Ignore(l => l.Total); // propriété calculée, pas stockée
            });
        });
    }
}