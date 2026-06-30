using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ControlLavados.Services;

/// <summary>
/// Envío de correos vía Microsoft Graph (flujo client-credentials, reusa el registro
/// de Entra). Requiere el permiso de aplicación Mail.Send y una casilla remitente
/// (config Mail:Sender). Si no está configurado, EnviarAsync devuelve false sin romper.
/// </summary>
public class GraphMailService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    public GraphMailService(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    /// <summary>True si están todos los datos para enviar (tenant, client, secret y remitente).</summary>
    public bool Configurado =>
        !string.IsNullOrWhiteSpace(_config["AzureAd:TenantId"]) &&
        !string.IsNullOrWhiteSpace(_config["AzureAd:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config["AzureAd:ClientSecret"]) &&
        !(_config["AzureAd:ClientSecret"] ?? "").StartsWith("REEMPLAZAR") &&
        !string.IsNullOrWhiteSpace(_config["Mail:Sender"]);

    /// <summary>Envía un correo HTML. Devuelve true si Graph lo aceptó.</summary>
    public async Task<bool> EnviarAsync(string para, string asunto, string cuerpoHtml)
    {
        if (!Configurado) return false;
        try
        {
            var token = await ObtenerTokenAsync();
            if (token is null) return false;

            var sender = _config["Mail:Sender"]!;
            var http = _http.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                message = new
                {
                    subject = asunto,
                    body = new { contentType = "HTML", content = cuerpoHtml },
                    toRecipients = new[] { new { emailAddress = new { address = para } } },
                },
                saveToSentItems = false,
            };
            var resp = await http.PostAsJsonAsync(
                $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender)}/sendMail", payload);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ObtenerTokenAsync()
    {
        var tenant = _config["AzureAd:TenantId"];
        var http = _http.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _config["AzureAd:ClientId"]!,
            ["client_secret"] = _config["AzureAd:ClientSecret"]!,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["grant_type"] = "client_credentials",
        });
        var resp = await http.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", form);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}
