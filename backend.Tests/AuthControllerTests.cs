using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;
using static backend.Tests.Pruebas;

namespace backend.Tests;

// Cada endpoint de autenticación traduce bien lo que responde el servicio.
public class AuthControllerTests
{
    private static readonly AuthResponseDto Sesion = new() { Token = "t", Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante" };

    private static (AuthController controller, Mock<IAuthService> auth) Crear(int? usuarioId = 1)
    {
        var auth = new Mock<IAuthService>();
        var controller = new AuthController(auth.Object, NullLogger<AuthController>.Instance)
        {
            ControllerContext = Como(usuarioId)
        };
        return (controller, auth);
    }

    // Proveedores externos

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    public async Task Externo_SinToken_Responde400(string proveedor)
    {
        var (controller, auth) = Crear();
        var dto = new ExternalLoginDto { IdToken = "  " };

        var resultado = proveedor == "google" ? await controller.GoogleLogin(dto) : await controller.MicrosoftLogin(dto);

        Assert.Equal(400, Codigo(resultado));
        auth.VerifyNoOtherCalls();
    }

    public static TheoryData<string, Exception?, int> CasosExternos => new()
    {
        { "google", null, 200 },
        { "google", new UnauthorizedAccessException("Sesión no válida"), 401 },
        { "google", new Exception("falla de red"), 500 },
        { "microsoft", null, 200 },
        { "microsoft", new UnauthorizedAccessException("Sesión no válida"), 401 },
        { "microsoft", new Exception("falla de red"), 500 },
    };

    [Theory]
    [MemberData(nameof(CasosExternos))]
    public async Task Externo_TraduceLaRespuestaDelServicio(string proveedor, Exception? error, int codigo)
    {
        var (controller, auth) = Crear();
        var dto = new ExternalLoginDto { IdToken = "token" };
        var llamada = proveedor == "google"
            ? auth.Setup(a => a.LoginWithGoogleAsync(dto))
            : auth.Setup(a => a.LoginWithMicrosoftAsync(dto));
        if (error == null) llamada.ReturnsAsync(Sesion); else llamada.ThrowsAsync(error);

        var resultado = proveedor == "google" ? await controller.GoogleLogin(dto) : await controller.MicrosoftLogin(dto);

        Assert.Equal(codigo, Codigo(resultado));
        if (codigo == 500)
        {
            // El error interno no se filtra al cliente.
            var problema = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(resultado).Value);
            Assert.DoesNotContain("falla de red", problema.Title);
        }
    }

    // Registro, inicio de sesión y verificación

    [Fact]
    public async Task Registro_ExitosoYConError()
    {
        var (controller, auth) = Crear();
        var dto = new RegisterDto { Email = "ana@example.com", Nombre = "Ana", Password = "clave123" };
        Assert.Equal(200, Codigo(await controller.Register(dto)));

        auth.Setup(a => a.RegisterAsync(dto)).ThrowsAsync(new Exception("El correo ya esta registrado"));
        var conError = Assert.IsType<ObjectResult>(await controller.Register(dto));
        Assert.Equal(400, conError.StatusCode);
        Assert.Equal("El correo ya esta registrado", Assert.IsType<ProblemDetails>(conError.Value).Title);
    }

    [Fact]
    public async Task Login_TraduceCadaError()
    {
        var (controller, auth) = Crear();
        var dto = new LoginDto { Email = "ana@example.com", Password = "x" };

        auth.Setup(a => a.LoginWithEmailAsync(dto)).ReturnsAsync(Sesion);
        Assert.Equal(200, Codigo(await controller.Login(dto)));

        auth.Setup(a => a.LoginWithEmailAsync(dto)).ThrowsAsync(new UnauthorizedAccessException("Credenciales incorrectas."));
        Assert.Equal(401, Codigo(await controller.Login(dto)));

        auth.Setup(a => a.LoginWithEmailAsync(dto)).ThrowsAsync(new InvalidOperationException("otra cosa"));
        Assert.Equal(400, Codigo(await controller.Login(dto)));
    }

    [Fact]
    public async Task VerificarCorreo_SinTokenConTokenYConError()
    {
        var (controller, auth) = Crear();

        Assert.Equal(400, Codigo(await controller.VerifyEmail("")));
        Assert.Equal(200, Codigo(await controller.VerifyEmail("token")));

        auth.Setup(a => a.VerifyEmailAsync("viejo")).ThrowsAsync(new Exception("El enlace de verificación no es válido o ha expirado."));
        Assert.Equal(400, Codigo(await controller.VerifyEmail("viejo")));
    }

    // Sesión y rol

    [Fact]
    public async Task Sesion_TokenInvalidoUsuarioInexistenteYExito()
    {
        Assert.Equal(401, Codigo(await Crear(usuarioId: null).controller.RefrescarSesion()));

        var (controller, auth) = Crear();
        auth.Setup(a => a.RefrescarSesionAsync(1)).ReturnsAsync(Sesion);
        Assert.Equal(200, Codigo(await controller.RefrescarSesion()));

        auth.Setup(a => a.RefrescarSesionAsync(1)).ThrowsAsync(new UnauthorizedAccessException("El usuario ya no existe."));
        Assert.Equal(401, Codigo(await controller.RefrescarSesion()));
    }

    [Fact]
    public async Task AsignarRol_TraduceCadaRespuesta()
    {
        var dto = new AsignarRolRequestDto { NuevoRol = "Tutor" };
        Assert.Equal(401, Codigo(await Crear(usuarioId: null).controller.AsignarRol(dto)));

        var (controller, auth) = Crear();
        auth.Setup(a => a.AsignarRolAsync(1, "Tutor")).ReturnsAsync(Sesion);
        Assert.Equal(200, Codigo(await controller.AsignarRol(dto)));

        auth.Setup(a => a.AsignarRolAsync(1, "Tutor")).ThrowsAsync(new InvalidOperationException("Ya tienes un rol."));
        Assert.Equal(409, Codigo(await controller.AsignarRol(dto)));

        auth.Setup(a => a.AsignarRolAsync(1, "Tutor")).ThrowsAsync(new ArgumentException("Rol no válido."));
        Assert.Equal(400, Codigo(await controller.AsignarRol(dto)));
    }

    [Fact]
    public async Task Restablecer_ConEnlaceInvalido_Responde400()
    {
        var (controller, _) = Crear();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = ApiDePrueba.JwtKey,
            ["Jwt:Issuer"] = "TutoriasUBackend",
            ["Jwt:Audience"] = "TutoriasUFrontend",
            ["FrontendUrl"] = "http://localhost:5173"
        }).Build();
        var recuperacion = new RecuperacionCuenta(Contexto(), config, Mock.Of<IColaCorreos>(), NullLogger<RecuperacionCuenta>.Instance);

        var resultado = await controller.RestablecerContrasena(
            new RestablecerContrasenaDto { Token = "no-es-un-token", Password = "nueva456" }, recuperacion, CancellationToken.None);

        Assert.Equal(400, Codigo(resultado));
    }
}

// Inicio de sesión con correo y contraseña, y con Microsoft (solo los rechazos).
public class AuthServiceLoginTests
{
    private static readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = ApiDePrueba.JwtKey,
        ["Jwt:Issuer"] = "TutoriasUBackend",
        ["Jwt:Audience"] = "TutoriasUFrontend",
        ["Jwt:ExpiresInDays"] = "7",
        ["FrontendUrl"] = "http://localhost:5173"
    }).Build();

    private static AuthService Servicio(out backend.Data.AppDbContext context)
    {
        context = Contexto();
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local", PasswordHash = BCrypt.Net.BCrypt.HashPassword("clave123"), IsEmailVerified = true },
            new Usuario { Id = 2, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Google", IsEmailVerified = true });
        context.SaveChanges();
        return new AuthService(context, Config, Mock.Of<IEmailService>(), Mock.Of<IVerificadorCuentasExternas>());
    }

    [Fact]
    public async Task Login_ConCredencialesCorrectas_DevuelveLaSesion()
    {
        var sesion = await Servicio(out _).LoginWithEmailAsync(new LoginDto { Email = "ana@example.com", Password = "clave123" });

        Assert.Equal(("ana@example.com", "Estudiante"), (sesion.Email, sesion.Rol));
        Assert.False(string.IsNullOrEmpty(sesion.Token));
    }

    [Theory]
    [InlineData("nadie@example.com", "clave123", "Credenciales incorrectas.")]
    [InlineData("ana@example.com", "otra", "Credenciales incorrectas.")]
    [InlineData("mateo@example.com", "clave123", "Google/Microsoft")]
    public async Task Login_RechazaConMensajeClaro(string email, string password, string mensaje)
    {
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Servicio(out _).LoginWithEmailAsync(new LoginDto { Email = email, Password = password }));

        Assert.Contains(mensaje, error.Message);
    }

    // La comprobación del token está en VerificadorCuentasExternasTests; aquí,
    // que el inicio de sesión solo confíe en lo que el verificador aceptó.
    [Fact]
    public async Task Externo_ConCuentaVerificada_CreaLaCuentaNueva()
    {
        var context = Contexto();
        var verificador = new Mock<IVerificadorCuentasExternas>();
        verificador.Setup(v => v.VerificarMicrosoftAsync("token-ms", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CuentaExterna("luis@uadec.edu.mx", "Luis Mora", null, "oid-1"));
        verificador.Setup(v => v.VerificarGoogleAsync("ya29.token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CuentaExterna("sofia@gmail.com", "Sofía Paz", "https://foto.example.com/s.png", "sub-2"));
        var servicio = new AuthService(context, Config, Mock.Of<IEmailService>(), verificador.Object);

        var microsoft = await servicio.LoginWithMicrosoftAsync(new ExternalLoginDto { IdToken = "token-ms" });
        var google = await servicio.LoginWithGoogleAsync(new ExternalLoginDto { IdToken = "ya29.token" });

        Assert.Equal(("luis@uadec.edu.mx", "Luis Mora"), (microsoft.Email, microsoft.Nombre));
        Assert.Equal(("sofia@gmail.com", "https://foto.example.com/s.png"), (google.Email, google.FotoUrl));
        Assert.Equal(["Google", "Microsoft"], context.Usuarios.Select(u => u.AuthProvider).Order());
    }

    [Fact]
    public async Task Externo_ConTokenRechazado_NoCreaNiDevuelveSesion()
    {
        var context = Contexto();
        var verificador = new Mock<IVerificadorCuentasExternas>();
        verificador.Setup(v => v.VerificarMicrosoftAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("La sesión de Microsoft no es válida o ha expirado."));
        verificador.Setup(v => v.VerificarGoogleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("La sesión de Google no es válida o ha expirado."));
        var servicio = new AuthService(context, Config, Mock.Of<IEmailService>(), verificador.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => servicio.LoginWithMicrosoftAsync(new ExternalLoginDto { IdToken = "falso" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => servicio.LoginWithGoogleAsync(new ExternalLoginDto { IdToken = "ya29.falso" }));
        Assert.Empty(context.Usuarios);
    }
}
