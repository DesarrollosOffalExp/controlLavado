using ControlLavados.Auth;
using ControlLavados.Components;
using ControlLavados.Data;
using ControlLavados.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<UsuarioService>();

// ---------- Autenticación ----------
// Interruptor: el login con Microsoft 365 (Entra) se activa SOLO si AzureAd:Enabled = true.
// Apagado (default) => la app funciona abierta y auto-loguea como admin (DevAuthHandler).
// Para prenderlo en Azure: App settings AzureAd__Enabled = true.
var entraHabilitado = builder.Configuration.GetValue<bool>("AzureAd:Enabled")
    && !builder.Environment.IsDevelopment();
if (entraHabilitado)
{
    // Login con Microsoft 365 (Entra ID).
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
}
else
{
    // Sin login: se simula el usuario admin para que todo funcione (demo / local).
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, null);
}

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("Admin", p => p.RequireRole(RolClaimsTransformation.RolAdmin));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IClaimsTransformation, RolClaimsTransformation>();

var app = builder.Build();

// Crea la base/tablas si faltan (incluida Usuarios, que se agregó después) y siembra.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw(@"IF OBJECT_ID('dbo.LavadosUsuarios') IS NULL
CREATE TABLE dbo.LavadosUsuarios (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Email nvarchar(120) NOT NULL,
    Nombre nvarchar(120) NULL,
    EsAdmin bit NOT NULL DEFAULT 0,
    Activo bit NOT NULL DEFAULT 1,
    UltimoAcceso datetime2 NULL
);");
    CatalogoService.Seed(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
if (entraHabilitado)
    app.MapControllers(); // endpoints de sign-in / sign-out de Microsoft Identity
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
