using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SpinUp.Api.Models;

namespace SpinUp.Api.Data;

public class SpinUpDbContext(DbContextOptions<SpinUpDbContext> options) : DbContext(options)
{
    public DbSet<ServiceDefinition> ServiceDefinitions => Set<ServiceDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var envConverter = new ValueConverter<Dictionary<string, string>, string>(
            env => JsonSerializer.Serialize(env, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        modelBuilder.Entity<ServiceDefinition>(entity =>
        {
            entity.ToTable("ServiceDefinitions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired().HasMaxLength(100);
            entity.Property(x => x.Path).IsRequired().HasMaxLength(260);
            entity.Property(x => x.Command).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Args).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.Property(x => x.Env).HasConversion(envConverter).HasColumnType("TEXT");
            entity.HasIndex(x => x.Name).IsUnique();
        });
    }
}
