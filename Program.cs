using ControlLavados.Auth;
using ControlLavados.Components;
using ControlLavados.Data;
using ControlLavados.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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
builder.Services.AddHttpClient();
builder.Services.AddScoped<GraphMailService>();

// ---------- Autenticación ----------
// Cookie propia (login con email/contraseña). El login con Microsoft 365 se suma
// si AzureAd:Enabled = true (producción). La pantalla /login ofrece ambos métodos.
var entraHabilitado = builder.Configuration.GetValue<bool>("AzureAd:Enabled")
    && !builder.Environment.IsDevelopment();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/sin-permiso";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
});

if (entraHabilitado)
{
    // Login con Microsoft 365 (Entra ID): firma en la cookie de arriba.
    authBuilder.AddMicrosoftIdentityWebApp(
        builder.Configuration,
        configSectionName: "AzureAd",
        openIdConnectScheme: OpenIdConnectDefaults.AuthenticationScheme,
        cookieScheme: null);
    builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
        o => o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme);
    builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
}

builder.Services.AddAuthorization(options =>
{
    // El acceso se controla por página (atributo [Authorize] + AuthorizeRouteView),
    // así los estáticos y el framework (blazor.web.js) quedan accesibles sin login.
    options.AddPolicy("Admin", p => p.RequireRole(RolClaimsTransformation.RolAdmin));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IClaimsTransformation, RolClaimsTransformation>();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Crea la base/tablas si faltan (incluida Usuarios, que se agregó después) y siembra.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();

    // Esquema propio 'lavados': mueve nuestras tablas de 'dbo' a 'lavados' una sola
    // vez (idempotente). No toca las tablas del sistema de etiquetas.
    db.Database.ExecuteSqlRaw(@"
IF SCHEMA_ID('lavados') IS NULL EXEC('CREATE SCHEMA lavados');
IF OBJECT_ID('dbo.Lavados','U') IS NOT NULL AND OBJECT_ID('lavados.Lavados','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.Lavados');
IF OBJECT_ID('dbo.LavadoOperarios','U') IS NOT NULL AND OBJECT_ID('lavados.LavadoOperarios','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.LavadoOperarios');
IF OBJECT_ID('dbo.Operarios','U') IS NOT NULL AND OBJECT_ID('lavados.Operarios','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.Operarios');
IF OBJECT_ID('dbo.Patentes','U') IS NOT NULL AND OBJECT_ID('lavados.Patentes','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.Patentes');
IF OBJECT_ID('dbo.Frigorificos','U') IS NOT NULL AND OBJECT_ID('lavados.Frigorificos','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.Frigorificos');
IF OBJECT_ID('dbo.LavadosUsuarios','U') IS NOT NULL AND OBJECT_ID('lavados.LavadosUsuarios','U') IS NULL EXEC('ALTER SCHEMA lavados TRANSFER dbo.LavadosUsuarios');");

    // Crea la tabla de usuarios en el esquema lavados si aún no existe (DB nueva/local).
    db.Database.ExecuteSqlRaw(@"IF OBJECT_ID('lavados.LavadosUsuarios') IS NULL
CREATE TABLE lavados.LavadosUsuarios (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Email nvarchar(120) NOT NULL,
    Nombre nvarchar(120) NULL,
    Rol int NOT NULL DEFAULT 0,
    Activo bit NOT NULL DEFAULT 1,
    UltimoAcceso datetime2 NULL,
    PasswordHash nvarchar(255) NULL
);");

    // Perfiles (Operario/Administrativo/Admin): agrega la columna Rol si falta y la
    // rellena desde EsAdmin para los usuarios que ya existían (1 => Admin = 2).
    db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID('lavados.LavadosUsuarios') IS NOT NULL AND COL_LENGTH('lavados.LavadosUsuarios','Rol') IS NULL
BEGIN
    EXEC('ALTER TABLE lavados.LavadosUsuarios ADD Rol int NOT NULL CONSTRAINT DF_LavadosUsuarios_Rol DEFAULT 0');
    IF COL_LENGTH('lavados.LavadosUsuarios','EsAdmin') IS NOT NULL
        EXEC('UPDATE lavados.LavadosUsuarios SET Rol = 2 WHERE EsAdmin = 1');
END");

    // Cuentas locales (email/contraseña): columna de contraseña hasheada.
    db.Database.ExecuteSqlRaw(@"IF COL_LENGTH('lavados.LavadosUsuarios','PasswordHash') IS NULL
    ALTER TABLE lavados.LavadosUsuarios ADD PasswordHash nvarchar(255) NULL;");

    // Invitaciones: token y vencimiento.
    db.Database.ExecuteSqlRaw(@"IF COL_LENGTH('lavados.LavadosUsuarios','InvitacionToken') IS NULL
    ALTER TABLE lavados.LavadosUsuarios ADD InvitacionToken nvarchar(80) NULL;");
    db.Database.ExecuteSqlRaw(@"IF COL_LENGTH('lavados.LavadosUsuarios','InvitacionExpira') IS NULL
    ALTER TABLE lavados.LavadosUsuarios ADD InvitacionExpira datetime2 NULL;");
    CatalogoService.Seed(db);

    // Limpieza de datos: formato único (patentes "AA 999 AA", textos en MAYÚSCULAS)
    // y eliminación de duplicados en todas las tablas. Idempotente.
    var catalogoSvc = scope.ServiceProvider.GetRequiredService<CatalogoService>();
    await catalogoSvc.NormalizarYDeduplicarAsync();
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

// Los estáticos (CSS/JS/favicon, blazor.web.js) deben servirse sin login,
// para que la propia pantalla de /login tenga estilos.
app.MapStaticAssets().AllowAnonymous();
if (entraHabilitado)
    app.MapControllers().AllowAnonymous(); // sign-in de Microsoft accesible sin login

// Cierre de sesión (cookie local y/o Microsoft).
app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
