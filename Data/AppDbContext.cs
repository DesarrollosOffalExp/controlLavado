using ControlLavados.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlLavados.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Lavado> Lavados => Set<Lavado>();
    public DbSet<LavadoOperario> LavadoOperarios => Set<LavadoOperario>();
    public DbSet<Operario> Operarios => Set<Operario>();
    public DbSet<Patente> Patentes => Set<Patente>();
    public DbSet<Frigorifico> Frigorificos => Set<Frigorifico>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lavado>(e =>
        {
            e.Property(l => l.Tipo).HasConversion<int>();
            e.Property(l => l.Estado).HasMaxLength(30);
            e.HasMany(l => l.Operarios)
             .WithOne(o => o.Lavado!)
             .HasForeignKey(o => o.LavadoId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LavadoOperario>(e =>
        {
            e.Property(o => o.Tipo).HasConversion<int>();
        });

        modelBuilder.Entity<Operario>(e => e.Property(o => o.Tipo).HasConversion<int>());
    }
}
