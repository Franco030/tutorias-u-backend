using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Restablecer la contraseña y reenviar el correo de verificación.
public class RecuperacionCuentaTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ApiDePrueba.JwtKey,
            ["Jwt:Issuer"] = "TutoriasUBackend",
            ["Jwt:Audience"] = "TutoriasUFrontend",
            ["FrontendUrl"] = "http://localhost:5173"
        })
        .Build();

    private static (RecuperacionCuenta servicio, AppDbContext context, List<CorreoPendiente> enviados) Crear()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local", PasswordHash = BCrypt.Net.BCrypt.HashPassword("vieja123"), IsEmailVerified = false },
            new Usuario { Id = 2, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Google", IsEmailVerified = true },
            new Usuario { Id = 3, Email = "lu@example.com", Nombre = "Lucía", Rol = "Estudiante", AuthProvider = "Local", PasswordHash = BCrypt.Net.BCrypt.HashPassword("clave123"), IsEmailVerified = true });
        context.SaveChanges();

        var enviados = new List<CorreoPendiente>();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Callback<CorreoPendiente>(enviados.Add);
        return (new RecuperacionCuenta(context, Config(), cola.Object, NullLogger<RecuperacionCuenta>.Instance), context, enviados);
    }

    private static string TokenDelEnlace(CorreoPendiente correo, string ruta)
    {
        var token = Regex.Match(correo.Html, $@"{ruta}\?token=([^""&<\s]+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "el correo debe traer el enlace con su token");
        return Uri.UnescapeDataString(token);
    }

    [Fact]
    public async Task Restablecer_CambiaLaContrasenaVerificaElCorreoYElEnlaceSirveUnaVez()
    {
        var (servicio, context, enviados) = Crear();

        await servicio.SolicitarRestablecimientoAsync("  ana@example.com ");
        var correo = Assert.Single(enviados);
        Assert.Equal("ana@example.com", correo.Para);
        var token = TokenDelEnlace(correo, "/restablecer-contrasena");

        await servicio.RestablecerAsync(token, "nueva456");
        var ana = await context.Usuarios.FindAsync(1);
        Assert.True(BCrypt.Net.BCrypt.Verify("nueva456", ana!.PasswordHash));
        Assert.True(ana.IsEmailVerified);

        // El mismo enlace ya no sirve: la contraseña cambió.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RestablecerAsync(token, "otra789"));
        Assert.Contains("no es válido o ya venció", error.Message);
    }

    [Fact]
    public async Task Restablecer_RechazaTokensDeOtroPropositoOAlterados()
    {
        var (servicio, _, _) = Crear();
        var deVerificacion = TokensCuenta.Crear(Config(), TokensCuenta.Verificacion, "ana@example.com", TimeSpan.FromHours(1));
        var vencido = TokensCuenta.Crear(Config(), TokensCuenta.Restablecimiento, "ana@example.com", TimeSpan.FromSeconds(-5), "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RestablecerAsync(deVerificacion, "nueva456"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RestablecerAsync(vencido, "nueva456"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RestablecerAsync("no-es-un-token", "nueva456"));
    }

    [Fact]
    public async Task Solicitar_ConCorreoDesconocido_NoEnviaNada()
    {
        var (servicio, _, enviados) = Crear();

        await servicio.SolicitarRestablecimientoAsync("nadie@example.com");

        Assert.Empty(enviados);
    }

    [Fact]
    public async Task Solicitar_ConCuentaDeGoogle_ExplicaQueNoUsaContrasena()
    {
        var (servicio, _, enviados) = Crear();

        await servicio.SolicitarRestablecimientoAsync("mateo@example.com");

        var correo = Assert.Single(enviados);
        Assert.Contains("Continuar con Google", correo.Html);
        Assert.DoesNotContain("restablecer-contrasena?token=", correo.Html);
    }

    [Fact]
    public async Task Reenviar_SoloACuentasLocalesSinVerificar()
    {
        var (servicio, context, enviados) = Crear();

        await servicio.ReenviarVerificacionAsync("lu@example.com");     // ya verificada
        await servicio.ReenviarVerificacionAsync("mateo@example.com");  // Google
        await servicio.ReenviarVerificacionAsync("nadie@example.com");  // no existe
        Assert.Empty(enviados);

        await servicio.ReenviarVerificacionAsync("ana@example.com");
        var correo = Assert.Single(enviados);
        Assert.Equal(RecuperacionCuenta.AsuntoVerificacion, correo.Asunto);

        // El enlace nuevo verifica la cuenta.
        var auth = new AuthService(context, Config(), Mock.Of<IEmailService>());
        await auth.VerifyEmailAsync(TokenDelEnlace(correo, "/verificar-correo"));
        Assert.True((await context.Usuarios.FindAsync(1))!.IsEmailVerified);
    }

    // API

    [Fact]
    public async Task Api_RespondeIgualExistaONoLaCuenta()
    {
        using var api = new ApiDePrueba();
        var cliente = api.CreateClient();
        using (var scope = api.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Usuarios.Add(new Usuario { Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local", PasswordHash = "x" });
            await context.SaveChangesAsync();
        }

        async Task<string> Pedir(string ruta, string email, string ip)
        {
            var respuesta = await cliente.SendAsync(new HttpRequestMessage(HttpMethod.Post, ruta)
            {
                Content = JsonContent.Create(new { email }),
                Headers = { { "X-Forwarded-For", ip } }
            });
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            return await respuesta.Content.ReadAsStringAsync();
        }

        Assert.Equal(
            await Pedir("/api/auth/recuperar-contrasena", "ana@example.com", "203.0.113.30"),
            await Pedir("/api/auth/recuperar-contrasena", "nadie@example.com", "203.0.113.31"));
        Assert.Equal(
            await Pedir("/api/auth/reenviar-verificacion", "ana@example.com", "203.0.113.32"),
            await Pedir("/api/auth/reenviar-verificacion", "nadie@example.com", "203.0.113.33"));
    }

    [Fact]
    public async Task Api_LoginSinVerificar_Responde403ConCodigo()
    {
        using var api = new ApiDePrueba();
        var cliente = api.CreateClient();
        using (var scope = api.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Usuarios.Add(new Usuario
            {
                Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("clave123"), IsEmailVerified = false
            });
            await context.SaveChangesAsync();
        }

        var respuesta = await cliente.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "ana@example.com", password = "clave123" }),
            Headers = { { "X-Forwarded-For", "203.0.113.40" } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("correo-sin-verificar", cuerpo.RootElement.GetProperty("codigo").GetString());
        Assert.StartsWith("Verifica tu correo", cuerpo.RootElement.GetProperty("title").GetString());
    }
}
