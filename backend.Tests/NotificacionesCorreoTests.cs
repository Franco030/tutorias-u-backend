using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Citas;
using backend.Enums;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

public class NotificacionesCorreoTests
{
    private static readonly DateTime Inicio = new(2026, 10, 22, 16, 0, 0, DateTimeKind.Utc);

    private static AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static AppDbContext SembrarCita(string estado)
    {
        var context = GetDbContext();
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana Ruiz", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 7, Email = "mateo@example.com", Nombre = "Mateo Herrera", Rol = "Tutor", AuthProvider = "Local" });
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Citas.Add(new Cita
        {
            Id = 20,
            EstudianteId = 1,
            TutorId = 7,
            MateriaId = 1,
            Estado = estado,
            FechaHoraInicio = Inicio
        });
        context.SaveChanges();
        return context;
    }

    private static CitasController CrearController(AppDbContext context, INotificacionesCorreo notificaciones, int usuarioId, string rol)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim(ClaimTypes.Role, rol)
        }, "mock"));

        return new CitasController(context, notificaciones, NullLogger<CitasController>.Instance, HoraFija.Actual())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static bool EsLaCita(DatosCitaCorreo datos) =>
        datos.Materia == "Cálculo" &&
        datos.FechaHoraInicio == Inicio &&
        datos.EstudianteEmail == "ana@example.com" &&
        datos.TutorEmail == "mateo@example.com" &&
        datos.EstudianteNombre == "Ana Ruiz" &&
        datos.TutorNombre == "Mateo Herrera";

    [Fact]
    public async Task AceptarCita_AvisaAlEstudianteConEnlaceYNotas()
    {
        var context = SembrarCita("Pendiente");
        var notificaciones = new Mock<INotificacionesCorreo>();

        var result = await CrearController(context, notificaciones.Object, 7, "Tutor").AceptarCita(20, new AceptarCitaRequestDto
        {
            LinkReunion = "https://meet.example.com/abc",
            Notas = "Trae tus dudas."
        });

        Assert.IsType<OkObjectResult>(result);
        notificaciones.Verify(n => n.CitaAceptada(
            It.Is<DatosCitaCorreo>(d => EsLaCita(d)), "https://meet.example.com/abc", "Trae tus dudas."), Times.Once);
    }

    [Fact]
    public async Task RechazarCita_AvisaAlEstudianteConElMotivo()
    {
        var context = SembrarCita("Pendiente");
        var notificaciones = new Mock<INotificacionesCorreo>();

        var result = await CrearController(context, notificaciones.Object, 7, "Tutor")
            .RechazarCita(20, new RechazarCitaRequestDto { Motivo = " Tengo examen ese día. " });

        Assert.IsType<OkObjectResult>(result);
        notificaciones.Verify(n => n.CitaRechazada(It.Is<DatosCitaCorreo>(d => EsLaCita(d)), "Tengo examen ese día."), Times.Once);
        var cita = await context.Citas.FindAsync(20);
        Assert.Contains("Motivo de rechazo: Tengo examen ese día.", cita!.Notas);
    }

    [Theory]
    [InlineData(1, "Estudiante", false)]
    [InlineData(7, "Tutor", true)]
    public async Task CancelarCita_AvisaALaContraparte(int usuarioId, string rol, bool canceladaPorTutor)
    {
        var context = SembrarCita("Aceptada");
        var notificaciones = new Mock<INotificacionesCorreo>();

        var result = await CrearController(context, notificaciones.Object, usuarioId, rol)
            .CancelarCita(20, new CancelarCitaRequestDto { Motivo = "Se me cruzó otra clase." });

        Assert.IsType<OkObjectResult>(result);
        notificaciones.Verify(n => n.CitaCancelada(
            It.Is<DatosCitaCorreo>(d => EsLaCita(d)), canceladaPorTutor, "Se me cruzó otra clase."), Times.Once);
    }

    [Fact]
    public async Task UnaFallaDelCorreoNoRompeLaOperacion()
    {
        var context = SembrarCita("Pendiente");
        var notificaciones = new Mock<INotificacionesCorreo>();
        notificaciones
            .Setup(n => n.CitaAceptada(It.IsAny<DatosCitaCorreo>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("Azure no responde"));

        var result = await CrearController(context, notificaciones.Object, 7, "Tutor")
            .AceptarCita(20, new AceptarCitaRequestDto { LinkReunion = "https://meet.example.com/abc" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(20))!.Estado);
    }

    [Fact]
    public async Task AprobarYRechazarTutor_AvisanAlAspirante()
    {
        var context = GetDbContext();
        context.EstadosAprobacions.Add(new EstadosAprobacion { Id = (int)EstadoAprobacion.Pendiente, Estado = "Pendiente" });
        context.Usuarios.AddRange(
            new Usuario { Id = 40, Email = "lu@example.com", Nombre = "Lucía Campos", Rol = "Estudiante", AuthProvider = "Local", EstadoAprobacionId = (int)EstadoAprobacion.Pendiente },
            new Usuario { Id = 41, Email = "die@example.com", Nombre = "Diego Salinas", Rol = "Estudiante", AuthProvider = "Local", EstadoAprobacionId = (int)EstadoAprobacion.Pendiente });
        context.SaveChanges();
        var notificaciones = new Mock<INotificacionesCorreo>();
        var controller = new AdminController(context, notificaciones.Object);

        await controller.AprobarTutor(40, CancellationToken.None);
        await controller.RechazarTutor(41, CancellationToken.None);

        notificaciones.Verify(n => n.TutorAprobado("Lucía Campos", "lu@example.com"), Times.Once);
        notificaciones.Verify(n => n.TutorRechazado("Diego Salinas", "die@example.com"), Times.Once);
    }

    private static (NotificacionesCorreo servicio, List<CorreoPendiente> enviados) CrearServicio()
    {
        var enviados = new List<CorreoPendiente>();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Callback<CorreoPendiente>(enviados.Add);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendUrl"] = "https://tutorias.example.com/" })
            .Build();
        return (new NotificacionesCorreo(cola.Object, configuration, NullLogger<NotificacionesCorreo>.Instance), enviados);
    }

    private static readonly DatosCitaCorreo Datos = new(
        "Cálculo", Inicio, "Ana <b>Ruiz</b>", "ana@example.com", "Mateo Herrera", "mateo@example.com");

    [Fact]
    public void CitaSolicitada_LlegaAlTutorConDatosYBotonParaRevisar()
    {
        var (servicio, enviados) = CrearServicio();

        servicio.CitaSolicitada(Datos);

        var correo = Assert.Single(enviados);
        Assert.Equal("mateo@example.com", correo.Para);
        Assert.Contains("Ana &lt;b&gt;Ruiz&lt;/b&gt;", correo.Html);
        Assert.DoesNotContain("<b>Ruiz</b>", correo.Html);
        Assert.Contains("Cálculo", correo.Html);
        Assert.Contains("jueves 22 de octubre de 2026, 16:00 h", correo.Html);
        Assert.Contains("href=\"https://tutorias.example.com/tutor/mis-tutorias\"", correo.Html);
        Assert.DoesNotContain("{{", correo.Html);
    }

    [Fact]
    public void CitaAceptada_IncluyeElEnlaceSoloSiEsHttp()
    {
        var (servicio, enviados) = CrearServicio();

        servicio.CitaAceptada(Datos, "https://meet.example.com/abc", "Repasa\nderivadas");
        servicio.CitaAceptada(Datos, "javascript:alert(1)", null);

        Assert.Equal("ana@example.com", enviados[0].Para);
        Assert.Contains("href=\"https://meet.example.com/abc\"", enviados[0].Html);
        Assert.Contains("Repasa<br>derivadas", enviados[0].Html);
        Assert.DoesNotContain("javascript:", enviados[1].Html);
        Assert.Contains("Ver mis tutorías", enviados[1].Html);
        Assert.DoesNotContain("Notas del tutor", enviados[1].Html);
    }

    [Fact]
    public void CitaCancelada_MuestraElMotivoOUnAvisoSinMotivo()
    {
        var (servicio, enviados) = CrearServicio();

        servicio.CitaCancelada(Datos, canceladaPorTutor: false, "Se me cruzó otra clase.");
        servicio.CitaCancelada(Datos, canceladaPorTutor: true, "  ");

        Assert.Equal("mateo@example.com", enviados[0].Para);
        Assert.Contains("Se me cruzó otra clase.", enviados[0].Html);
        Assert.Equal("ana@example.com", enviados[1].Para);
        Assert.Contains("No se indicó un motivo.", enviados[1].Html);
    }
}
