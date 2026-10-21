using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Requests;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// API real en memoria (sin SQL Server ni Azure) para probar el pipeline.
public sealed class ApiDePrueba : WebApplicationFactory<Program>
{
    public const string JwtKey = "LallaveSuperSecretaDePruebaQueDebeSerLarga12345";
    private readonly string _baseDatos = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", "TutoriasUBackend");
        builder.UseSetting("Jwt:Audience", "TutoriasUFrontend");
        builder.UseSetting("FrontendUrl", "http://localhost:5173");
        builder.UseSetting("LimiteAcceso:PorMinuto", "3");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_baseDatos));
        });
    }
}

public class SeguridadAccesoTests
{
    private static HttpRequestMessage Login(string ip) => new(HttpMethod.Post, "/api/auth/login")
    {
        Content = JsonContent.Create(new { email = "nadie@example.com", password = "x" }),
        Headers = { { "X-Forwarded-For", ip } }
    };

    // Límite de intentos

    [Fact]
    public async Task Login_SeLimitaPorIpConUn429EnEspanol()
    {
        using var api = new ApiDePrueba();
        var cliente = api.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var permitida = await cliente.SendAsync(Login("203.0.113.10"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, permitida.StatusCode);
        }

        var bloqueada = await cliente.SendAsync(Login("203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        Assert.Equal("application/problem+json", bloqueada.Content.Headers.ContentType?.MediaType);
        var problema = await bloqueada.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Demasiados intentos. Espera un minuto e inténtalo de nuevo.", problema!.Title);
        Assert.True(bloqueada.Headers.Contains("Retry-After"));

        // Otra IP no comparte el límite.
        var otraIp = await cliente.SendAsync(Login("203.0.113.20"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otraIp.StatusCode);
    }

    // Token de verificación de correo

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ApiDePrueba.JwtKey,
            ["Jwt:Issuer"] = "TutoriasUBackend",
            ["Jwt:Audience"] = "TutoriasUFrontend",
            ["FrontendUrl"] = "http://localhost:5173"
        })
        .Build();

    private static async Task<(AuthService Auth, AppDbContext Context, string TokenVerificacion)> Registrar()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        string? html = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, contenido) => html = contenido)
            .Returns(Task.CompletedTask);
        var auth = new AuthService(context, Config(), email.Object);

        await auth.RegisterAsync(new RegisterDto { Email = "ana@example.com", Nombre = "Ana", Password = "PasswordSeguro123" });

        var token = Regex.Match(html!, "token=([^'\"&]+)").Groups[1].Value;
        return (auth, context, token);
    }

    [Fact]
    public async Task Verificacion_FuncionaConSuPropioToken()
    {
        var (auth, context, token) = await Registrar();

        await auth.VerifyEmailAsync(token);

        Assert.True((await context.Usuarios.SingleAsync()).IsEmailVerified);
    }

    [Fact]
    public async Task Verificacion_RechazaUnTokenDeSesion()
    {
        var (auth, context, _) = await Registrar();
        var usuario = await context.Usuarios.SingleAsync();
        var sesion = await auth.RefrescarSesionAsync(usuario.Id);

        await Assert.ThrowsAnyAsync<Exception>(() => auth.VerifyEmailAsync(sesion.Token));
        Assert.False((await context.Usuarios.SingleAsync()).IsEmailVerified);
    }

    [Fact]
    public async Task TokenDeVerificacion_NoSirveComoSesionEnLaApi()
    {
        var (_, _, token) = await Registrar();
        using var api = new ApiDePrueba();
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var respuesta = await cliente.GetAsync("/api/auth/sesion");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // Comprobantes privados (cuenta del emulador local: no se conecta a nada)

    private static BlobService Blob() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AZURE_STORAGE_CONNECTION_STRING"] = "UseDevelopmentStorage=true" })
            .Build(),
        NullLogger<BlobService>.Instance);

    private const string Cuenta = "http://127.0.0.1:10000/devstoreaccount1";

    [Theory]
    [InlineData(Cuenta + "/comprobantes/abc.pdf", true)]
    [InlineData(Cuenta + "/uploads/abc.pdf", true)]
    [InlineData(Cuenta + "/otro/abc.pdf", false)]
    [InlineData("https://phishing.example.com/comprobantes/abc.pdf", false)]
    [InlineData("no es una url", false)]
    public void Blob_SoloAceptaComprobantesPropios(string url, bool propio)
    {
        Assert.Equal(propio, Blob().EsComprobantePropio(url));
    }

    [Fact]
    public void Blob_FirmaUnaUrlDeLecturaQueVence()
    {
        var firmada = Blob().GenerarUrlLectura(Cuenta + "/comprobantes/abc.pdf", TimeSpan.FromMinutes(30));

        Assert.NotNull(firmada);
        Assert.StartsWith(Cuenta + "/comprobantes/abc.pdf?", firmada);
        Assert.Contains("sig=", firmada);
        Assert.Contains("sp=r", firmada);
        Assert.Null(Blob().GenerarUrlLectura("https://phishing.example.com/x.pdf", TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task Aplicar_RechazaUnComprobanteQueNoEsNuestro()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Usuarios.Add(new Usuario { Id = 1, Email = "a@example.com", Nombre = "A", Rol = "Tutor", AuthProvider = "Local" });
        context.SaveChanges();
        var controller = new TutoresController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1") }, "mock"))
                }
            }
        };

        var result = await controller.Aplicar(
            new AplicarTutorRequestDto { UrlCredencial = "https://phishing.example.com/kardex.pdf", MateriaIds = { 1 } },
            Blob());

        ProblemaAssert.EsProblema(result, 400, "Sube tu comprobante desde la aplicación.");
        Assert.Empty(context.TutorSolicitudesCredenciales);
    }
}
