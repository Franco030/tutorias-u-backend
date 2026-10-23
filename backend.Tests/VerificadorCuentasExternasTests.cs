using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using backend.Services;

namespace backend.Tests;

// Tokens de Google y Microsoft: solo se aceptan los que el proveedor firmó
// para esta app. Las llaves de "Microsoft" son de prueba (no hay red).
public class VerificadorCuentasExternasTests
{
    private const string ClientIdMicrosoft = "467af7d5-3833-4dac-8597-dc0822e39baa";
    private const string ClientIdGoogle = "123-tutoriasu.apps.googleusercontent.com";
    private const string Organizacion = "8f2a1c5e-0000-4000-8000-000000000001";
    private const string EmisorPlantilla = "https://login.microsoftonline.com/{tenantid}/v2.0";
    private static readonly string Emisor = EmisorPlantilla.Replace("{tenantid}", Organizacion);

    private static readonly RsaSecurityKey LlaveMicrosoft = new(RSA.Create(2048)) { KeyId = "llave-ms" };
    private static readonly RsaSecurityKey LlaveAjena = new(RSA.Create(2048)) { KeyId = "llave-ms" };

    private static VerificadorCuentasExternas Verificador(
        HttpMessageHandler? google = null, string? clientIdMicrosoft = ClientIdMicrosoft, string? clientIdGoogle = ClientIdGoogle)
    {
        var metadatos = new OpenIdConnectConfiguration { Issuer = EmisorPlantilla };
        metadatos.SigningKeys.Add(LlaveMicrosoft);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Microsoft:ClientId"] = clientIdMicrosoft,
            ["Google:ClientId"] = clientIdGoogle,
        }).Build();
        return new VerificadorCuentasExternas(
            new HttpClient(google ?? new GoogleFalso(_ => new HttpResponseMessage(HttpStatusCode.BadRequest))),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(metadatos),
            config,
            NullLogger<VerificadorCuentasExternas>.Instance);
    }

    private static string TokenMicrosoft(
        SecurityKey? llave = null,
        string audiencia = ClientIdMicrosoft,
        string? emisor = null,
        string organizacion = Organizacion,
        DateTime? vence = null,
        string? correo = "luis@uadec.edu.mx")
    {
        var reclamos = new Dictionary<string, object> { ["tid"] = organizacion, ["name"] = "Luis Mora", ["oid"] = "oid-1" };
        if (correo is not null) reclamos["preferred_username"] = correo;
        var expira = vence ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor ?? Emisor,
            Audience = audiencia,
            Claims = reclamos,
            NotBefore = expira.AddHours(-2),
            IssuedAt = expira.AddHours(-2),
            Expires = expira,
            SigningCredentials = new SigningCredentials(llave ?? LlaveMicrosoft, SecurityAlgorithms.RsaSha256),
        });
    }

    // Microsoft

    [Fact]
    public async Task Microsoft_TokenFirmadoParaLaApp_DevuelveLaCuenta()
    {
        var cuenta = await Verificador().VerificarMicrosoftAsync(TokenMicrosoft());

        Assert.Equal(("luis@uadec.edu.mx", "Luis Mora", "oid-1"), (cuenta.Email, cuenta.Nombre, cuenta.ProveedorId));
    }

    [Fact]
    public async Task Microsoft_TokenSinFirma_SeRechaza()
    {
        // El ataque de antes: un JWT con "alg": "none" y el correo de otra persona.
        static string B64(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var falso = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}." +
                    $"{B64($"{{\"iss\":\"{Emisor}\",\"aud\":\"{ClientIdMicrosoft}\",\"tid\":\"{Organizacion}\",\"preferred_username\":\"admin@uadec.edu.mx\",\"exp\":4102444800}}")}.";

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verificador().VerificarMicrosoftAsync(falso));
    }

    public static TheoryData<string, string> TokensInvalidos => new()
    {
        { "firmado con otra llave", TokenMicrosoft(llave: LlaveAjena) },
        { "para otra app", TokenMicrosoft(audiencia: "00000000-otra-app") },
        { "vencido", TokenMicrosoft(vence: DateTime.UtcNow.AddMinutes(-10)) },
        { "emisor que no es Microsoft", TokenMicrosoft(emisor: "https://login.example.com/{tenantid}/v2.0") },
        { "emisor de otra organización que la del token", TokenMicrosoft(organizacion: "otra-organizacion") },
        { "sin correo", TokenMicrosoft(correo: null) },
        { "texto que no es un token", "esto-no-es-un-jwt" },
    };

    [Theory]
    [MemberData(nameof(TokensInvalidos))]
    public async Task Microsoft_TokenInvalido_SeRechaza(string caso, string token)
    {
        Assert.NotEmpty(caso);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verificador().VerificarMicrosoftAsync(token));
    }

    [Fact]
    public async Task Microsoft_SinClientIdConfigurado_RechazaTodo()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verificador(clientIdMicrosoft: null).VerificarMicrosoftAsync(TokenMicrosoft()));
    }

    [Fact]
    public async Task Api_MicrosoftSoloEntraConTokenFirmado()
    {
        // API real; solo se cambian las llaves de Microsoft por las de prueba.
        var metadatos = new OpenIdConnectConfiguration { Issuer = EmisorPlantilla };
        metadatos.SigningKeys.Add(LlaveMicrosoft);
        using var api = new ApiDePrueba().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IConfigurationManager<OpenIdConnectConfiguration>>();
            s.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(new StaticConfigurationManager<OpenIdConnectConfiguration>(metadatos));
        }));
        var cliente = api.CreateClient();
        Task<HttpResponseMessage> Entrar(string token) =>
            cliente.PostAsJsonAsync("/api/auth/microsoft", new { idToken = token });

        var falsificado = await Entrar(TokenMicrosoft(llave: LlaveAjena, correo: "admin@uadec.edu.mx"));
        var valido = await Entrar(TokenMicrosoft());

        Assert.Equal(HttpStatusCode.Unauthorized, falsificado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valido.StatusCode);
        var usuario = Assert.Single(api.Services.CreateScope().ServiceProvider.GetRequiredService<backend.Data.AppDbContext>().Usuarios);
        Assert.Equal("luis@uadec.edu.mx", usuario.Email);
    }

    // Google (token de acceso, el que envía el botón del frontend)

    private sealed class GoogleFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Recibidas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibidas.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GoogleFalso Google(string tokenInfo) => new(solicitud =>
        solicitud.RequestUri!.AbsolutePath == "/tokeninfo"
            ? Json(tokenInfo)
            : Json("""{"name":"Sofía Paz","picture":"https://foto.example.com/s.png"}"""));

    [Fact]
    public async Task Google_TokenDeEstaApp_DevuelveLaCuentaConNombreYFoto()
    {
        var google = Google($$"""{"aud":"{{ClientIdGoogle}}","sub":"sub-2","email":"sofia@gmail.com","email_verified":"true"}""");

        var cuenta = await Verificador(google).VerificarGoogleAsync("ya29.token");

        Assert.Equal(new("sofia@gmail.com", "Sofía Paz", "https://foto.example.com/s.png", "sub-2"), cuenta);
        // El perfil se pide con el token en el encabezado, no en la URL.
        Assert.Equal("Bearer", google.Recibidas[1].Headers.Authorization?.Scheme);
    }

    [Theory]
    [InlineData("""{"aud":"999-otra-app.apps.googleusercontent.com","email":"sofia@gmail.com","email_verified":"true"}""")] // token de otra app
    [InlineData("""{"aud":"123-tutoriasu.apps.googleusercontent.com","email":"sofia@gmail.com","email_verified":"false"}""")] // correo sin verificar
    [InlineData("""{"aud":"123-tutoriasu.apps.googleusercontent.com","email_verified":"true"}""")] // sin correo
    public async Task Google_TokenQueNoSirve_SeRechaza(string tokenInfo)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verificador(Google(tokenInfo)).VerificarGoogleAsync("ya29.token"));
    }

    [Fact]
    public async Task Google_TokenVencidoOInexistente_SeRechaza()
    {
        // tokeninfo responde 400 a un token que Google no reconoce.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verificador().VerificarGoogleAsync("ya29.vencido"));
    }

    [Fact]
    public async Task Google_IdTokenInvalido_SeRechaza()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verificador().VerificarGoogleAsync("no-es-un-id-token"));
    }

    [Fact]
    public async Task Google_SinClientIdConfigurado_RechazaTodo()
    {
        var google = Google($$"""{"aud":"{{ClientIdGoogle}}","email":"sofia@gmail.com","email_verified":"true"}""");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verificador(google, clientIdGoogle: null).VerificarGoogleAsync("ya29.token"));
        Assert.Empty(google.Recibidas);
    }
}
