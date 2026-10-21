using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Authorization;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Citas;
using backend.DTOs.Requests;
using backend.Enums;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Regresiones de la revisión de seguridad y lógica.
public class HallazgosRevisionTests
{
    private static AppDbContext GetDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static IConfiguration Config(Dictionary<string, string?>? valores = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(valores ?? new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "LallaveSuperSecretaDePruebaQueDebeSerLarga12345",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["FrontendUrl"] = "http://localhost:5173"
        })
        .Build();

    private static Usuario Usuario(int id, string rol, EstadoAprobacion estado = EstadoAprobacion.Ninguno) => new()
    {
        Id = id,
        Email = $"u{id}@example.com",
        Nombre = $"Usuario {id}",
        Rol = rol,
        AuthProvider = "Local",
        EstadoAprobacionId = (int)estado
    };

    // 1 · Asignación de rol

    [Fact]
    public async Task AsignarRol_NoPermiteVolverseAdministrador()
    {
        var context = GetDbContext();
        context.Usuarios.Add(Usuario(1, "Nuevo"));
        context.SaveChanges();
        var auth = new AuthService(context, Config(), Mock.Of<IEmailService>());

        await Assert.ThrowsAsync<ArgumentException>(() => auth.AsignarRolAsync(1, "Administrador"));
        Assert.Equal("Nuevo", (await context.Usuarios.FindAsync(1))!.Rol);
    }

    [Fact]
    public async Task AsignarRol_SoloSePuedeElegirUnaVez()
    {
        var context = GetDbContext();
        context.Usuarios.Add(Usuario(1, "Nuevo"));
        context.SaveChanges();
        var auth = new AuthService(context, Config(), Mock.Of<IEmailService>());

        var sesion = await auth.AsignarRolAsync(1, "Estudiante");

        Assert.Equal("Estudiante", sesion.Rol);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.AsignarRolAsync(1, "Tutor"));
    }

    // 3 · Estado al aceptar / rechazar y enlace de reunión

    private static (AppDbContext, CitasController) Citas(string estado, DateTime inicio, DateTime ahora)
    {
        var context = GetDbContext();
        context.Citas.Add(new Cita { Id = 20, EstudianteId = 1, TutorId = 7, MateriaId = 1, Estado = estado, FechaHoraInicio = inicio });
        context.SaveChanges();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "mock"));
        var controller = new CitasController(context, Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(ahora))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        return (context, controller);
    }

    private static readonly DateTime Ahora = new(2026, 10, 22, 10, 0, 0);

    [Theory]
    [InlineData("Cancelada")]
    [InlineData("Rechazada")]
    [InlineData("Aceptada")]
    public async Task AceptarCita_SoloAceptaPendientes(string estado)
    {
        var (context, controller) = Citas(estado, Ahora.AddDays(1), Ahora);

        var result = await controller.AceptarCita(20, new AceptarCitaRequestDto());

        ProblemaAssert.EsProblema(result, 409);
        Assert.Equal(estado, (await context.Citas.FindAsync(20))!.Estado);
    }

    [Fact]
    public async Task AceptarCita_RechazaSolicitudesVencidas()
    {
        var (_, controller) = Citas("Pendiente", Ahora.AddHours(-1), Ahora);

        ProblemaAssert.EsProblema(await controller.AceptarCita(20, new AceptarCitaRequestDto()), 409);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("meet.example.com/abc")]
    public async Task AceptarCita_ValidaQueElEnlaceSeaWeb(string enlace)
    {
        var (context, controller) = Citas("Pendiente", Ahora.AddDays(1), Ahora);

        var result = await controller.AceptarCita(20, new AceptarCitaRequestDto { LinkReunion = enlace });

        ProblemaAssert.EsProblema(result, 400);
        Assert.Equal("Pendiente", (await context.Citas.FindAsync(20))!.Estado);
    }

    [Fact]
    public async Task RechazarCita_NoRechazaUnaCitaYaAceptada()
    {
        var (context, controller) = Citas("Aceptada", Ahora.AddDays(1), Ahora);

        ProblemaAssert.EsProblema(await controller.RechazarCita(20), 409);
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(20))!.Estado);
    }

    // 4 · Agendar solo con tutores aprobados y en sus materias

    private static async Task<IActionResult> Agendar(Action<AppDbContext> semilla)
    {
        var context = GetDbContext();
        semilla(context);
        context.TutorDisponibilidads.Add(new TutorDisponibilidad
        {
            TutorId = 2,
            DiaSemana = (byte)Ahora.DayOfWeek,
            HoraInicio = new TimeOnly(16, 0),
            HoraFin = new TimeOnly(17, 0)
        });
        context.SaveChanges();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "mock"));
        var controller = new CitasController(context, Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(Ahora))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        return await controller.AgendarCita(new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = Ahora.Date.AddHours(16),
            FechaHoraFin = Ahora.Date.AddHours(17)
        });
    }

    [Fact]
    public async Task AgendarCita_RechazaTutoresSinAprobar()
    {
        var result = await Agendar(context =>
        {
            var tutor = Usuario(2, "Tutor", EstadoAprobacion.Pendiente);
            tutor.MateriaNavigation.Add(new Materia { Id = 3, Nombre = "Cálculo" });
            context.Usuarios.Add(tutor);
        });

        ProblemaAssert.EsProblema(result, 400, "El tutor no está disponible para agendar.");
    }

    [Fact]
    public async Task AgendarCita_RechazaMateriasQueElTutorNoImparte()
    {
        var result = await Agendar(context =>
        {
            Semillas.TutorAprobado(context, 2, 4, "Física");
            context.Materias.Add(new Materia { Id = 3, Nombre = "Cálculo" });
        });

        ProblemaAssert.EsProblema(result, 400, "El tutor no imparte esa materia.");
    }

    // 5 · Política TutorAprobado

    [Theory]
    [InlineData(EstadoAprobacion.Aprobado, true)]
    [InlineData(EstadoAprobacion.Ninguno, false)]
    [InlineData(EstadoAprobacion.Pendiente, false)]
    [InlineData(EstadoAprobacion.Rechazado, false)]
    public async Task TutorAprobado_SoloAutorizaTutoresAprobados(EstadoAprobacion estado, bool autorizado)
    {
        var context = GetDbContext();
        context.Usuarios.Add(Usuario(7, "Tutor", estado));
        context.SaveChanges();
        var requisito = new TutorAprobadoRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "Tutor")
        }, "mock"));
        var authContext = new AuthorizationHandlerContext(new[] { requisito }, user, null);

        await new TutorAprobadoHandler(context).HandleAsync(authContext);

        Assert.Equal(autorizado, authContext.HasSucceeded);
    }

    // 6 · Correo sin configurar

    [Fact]
    public async Task EmailService_SinConfiguracionSeConstruyeYFallaSoloAlEnviar()
    {
        var servicio = new EmailService(
            Config(new Dictionary<string, string?> { ["COMMUNICATION_SERVICES_CS"] = "" }),
            NullLogger<EmailService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.SendEmailAsync("a@example.com", "Hola", "<p>Hola</p>"));
    }

    [Fact]
    public async Task Registro_SeDeshaceSiNoSePuedeEnviarElCorreo()
    {
        var context = GetDbContext();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("El servicio de correo no está configurado."));
        var auth = new AuthService(context, Config(), email.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.RegisterAsync(new RegisterDto
        {
            Email = "nuevo@example.com",
            Nombre = "Nuevo",
            Password = "PasswordSeguro123"
        }));

        Assert.False(await context.Usuarios.AnyAsync(u => u.Email == "nuevo@example.com"));
    }
}
