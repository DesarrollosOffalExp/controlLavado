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
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Esquema propio: todas nuestras tablas viven en 'lavados.*', separadas de
        // las del sistema de etiquetas ('dbo.*'). Evita choques de nombres a futuro.
        modelBuilder.HasDefaultSchema("lavados");

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

        // La base se comparte con el proyecto de etiquetas, que ya tiene una tabla
        // "Usuarios" con otro esquema. Usamos un nombre propio para no chocar.
        modelBuilder.Entity<Usuario>().ToTable("LavadosUsuarios");
    }
}
