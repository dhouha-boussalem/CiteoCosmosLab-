using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CiteoCosmosLab.Data;

public class CamelCaseCosmosConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var name = property.Name;
                var camelCase = char.ToLowerInvariant(name[0]) + name[1..];
                CosmosPropertyExtensions.SetJsonPropertyName(property, camelCase);
            }
        }
    }
}