using ControlLavados.Components;
using ControlLavados.Data;
using ControlLavados.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=ControlLavados;Trusted_Connection=True;TrustServerCertificate=True";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<LavadoService>();
builder.Services.AddScoped<ReporteService>();
builder.Services.AddScoped<CatalogoService>();
builder.Services.AddScoped<ImportacionService>();

var app = builder.Build();

// Crea la base de datos y las tablas si no existen (sin migraciones).
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    CatalogoService.Seed(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
