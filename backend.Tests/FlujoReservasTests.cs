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
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Fechas bloqueadas, antelación mínima, tema de la cita y enlace fijo del tutor.
public class FlujoReservasTests
{
    // Miércoles 21 de octubre de 2026, 10:00 en la hora de la plataforma.
    private static readonly DateTime Ahora = new(2026, 10, 21, 10, 0, 0);

    private static AppDbContext GetDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ControllerContext Contexto(int usuarioId, string rol) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                new Claim(ClaimTypes.Role, rol)
            }, "mock"))
        }
    };

    private static CitasController Citas(AppDbContext context, int usuarioId, string rol, INotificacionesCorreo? notificaciones = null) =>
        new(context, notificaciones ?? Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(Ahora))
        {
            ControllerContext = Contexto(usuarioId, rol)
        };

    private static TutoresController Tutores(AppDbContext context, int usuarioId) =>
        new(context) { ControllerContext = Contexto(usuarioId, "Tutor") };

    // Tutor 2 aprobado, con la materia 3 y disponibilidad diaria de 16:00 a 17:00.
    private static AppDbContext ConTutor(int antelacionHoras = 0, string? link = null)
    {
        var context = GetDbContext();
        Semillas.TutorAprobado(context, 2, 3);
        var tutor = context.Usuarios.Find(2)!;
        tutor.AntelacionMinimaHoras = antelacionHoras;
        tutor.LinkReunion = link;
        foreach (var dia in Enumerable.Range(0, 7))
        {
            context.TutorDisponibilidads.Add(new TutorDisponibilidad
            {
                TutorId = 2,
                DiaSemana = (byte)dia,
                HoraInicio = new TimeOnly(16, 0),
                HoraFin = new TimeOnly(17, 0)
            });
        }
        context.SaveChanges();
        return context;
    }

    private static CrearCitaRequestDto Solicitud(DateTime dia, string? tema = null) => new()
    {
        TutorId = 2,
        MateriaId = 3,
        FechaHoraInicio = dia.Date.AddHours(16),
        FechaHoraFin = dia.Date.AddHours(17),
        Tema = tema
    };

    // Antelación mínima

    [Fact]
    public async Task Agendar_RechazaReservasConMenosAntelacionQueLaDelTutor()
    {
        // Hoy a las 16:00 está a 6 h; el tutor pide 12 h.
        var result = await Citas(ConTutor(antelacionHoras: 12), 1, "Estudiante").AgendarCita(Solicitud(Ahora));

        ProblemaAssert.EsProblema(result, 400, "Este tutor recibe reservas con al menos 12 h de anticipación.");
    }

    [Fact]
    public async Task Agendar_AceptaReservasQueRespetanLaAntelacion()
    {
        var result = await Citas(ConTutor(antelacionHoras: 12), 1, "Estudiante").AgendarCita(Solicitud(Ahora.AddDays(1)));

        Assert.IsType<CreatedResult>(result);
    }

    // Fechas bloqueadas

    [Fact]
    public async Task Agendar_RechazaUnDiaBloqueado()
    {
        var context = ConTutor();
        context.Bloqueos.Add(new Bloqueo { TutorId = 2, Fecha = DateOnly.FromDateTime(Ahora.AddDays(1)) });
        context.SaveChanges();

        var result = await Citas(context, 1, "Estudiante").AgendarCita(Solicitud(Ahora.AddDays(1)));

        ProblemaAssert.EsProblema(result, 400, "El tutor no da tutorías ese día.");
        Assert.Empty(context.Citas);
    }

    [Fact]
    public async Task GuardarBloqueos_ReemplazaLosFuturosEInformaCitasAfectadas()
    {
        var context = ConTutor();
        var manana = DateOnly.FromDateTime(Ahora.AddDays(1));
        context.Bloqueos.AddRange(
            new Bloqueo { TutorId = 2, Fecha = DateOnly.FromDateTime(Ahora.AddDays(-3)), Motivo = "pasado" },
            new Bloqueo { TutorId = 2, Fecha = manana.AddDays(5) });
        context.Citas.Add(new Cita { EstudianteId = 1, TutorId = 2, MateriaId = 3, Estado = "Aceptada", FechaHoraInicio = manana.ToDateTime(new TimeOnly(16, 0)) });
        context.SaveChanges();

        var result = await Tutores(context, 2).GuardarBloqueos(
            new List<BloqueoDto> { new() { Fecha = manana, Motivo = "  Examen  " }, new() { Fecha = manana } },
            new HoraFija(Ahora),
            CancellationToken.None);

        var respuesta = Assert.IsType<BloqueosResponseDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        var bloqueo = Assert.Single(respuesta.Bloqueos);
        Assert.Equal(manana, bloqueo.Fecha);
        Assert.Equal("Examen", bloqueo.Motivo);
        Assert.Equal(1, respuesta.CitasAfectadas);
        // El bloqueo pasado se conserva; el futuro que no se envió se quita.
        Assert.Equal(2, await context.Bloqueos.CountAsync());
    }

    [Fact]
    public async Task GuardarBloqueos_RechazaFechasPasadas()
    {
        var result = await Tutores(ConTutor(), 2).GuardarBloqueos(
            new List<BloqueoDto> { new() { Fecha = DateOnly.FromDateTime(Ahora.AddDays(-1)) } },
            new HoraFija(Ahora),
            CancellationToken.None);

        ProblemaAssert.EsProblema(result.Result, 400);
    }

    [Fact]
    public async Task BloqueosPublicos_SoloDevuelvenFechasDesdeHoySinMotivo()
    {
        var context = ConTutor();
        var hoy = DateOnly.FromDateTime(Ahora);
        context.Bloqueos.AddRange(
            new Bloqueo { TutorId = 2, Fecha = hoy.AddDays(-1), Motivo = "privado" },
            new Bloqueo { TutorId = 2, Fecha = hoy.AddDays(2), Motivo = "privado" });
        context.SaveChanges();

        var result = await Tutores(context, 1).ObtenerBloqueosTutor(2, new HoraFija(Ahora), CancellationToken.None);

        var fechas = Assert.IsAssignableFrom<IReadOnlyList<DateOnly>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { hoy.AddDays(2) }, fechas);
    }

    // Tema de la cita

    [Fact]
    public async Task Agendar_GuardaElTemaYElTutorLoVeEnLaSolicitud()
    {
        var context = ConTutor();
        context.Usuarios.Add(new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" });
        context.SaveChanges();

        await Citas(context, 1, "Estudiante").AgendarCita(Solicitud(Ahora.AddDays(1), "  Límites y derivadas  "));
        var pendientes = await Citas(context, 2, "Tutor").ObtenerPendientes();

        var cita = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CitaPendienteResponseDto>>(
            Assert.IsType<OkObjectResult>(pendientes.Result).Value));
        Assert.Equal("Límites y derivadas", cita.Tema);
    }

    [Fact]
    public void CorreoDeSolicitud_IncluyeElTemaSoloSiExiste()
    {
        var enviados = new List<CorreoPendiente>();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Callback<CorreoPendiente>(enviados.Add);
        var servicio = new NotificacionesCorreo(cola.Object, new ConfigurationBuilder().Build(), NullLogger<NotificacionesCorreo>.Instance, new HoraFija(Ahora));
        DatosCitaCorreo Datos(string? tema) => new("Cálculo", Ahora, "Ana", "a@example.com", "Mateo", "m@example.com", tema);

        servicio.CitaSolicitada(Datos("Repasar <límites>"));
        servicio.CitaSolicitada(Datos(null));

        Assert.Contains("Quiere repasar", enviados[0].Html);
        Assert.Contains("Repasar &lt;límites&gt;", enviados[0].Html);
        Assert.DoesNotContain("Quiere repasar", enviados[1].Html);
    }

    // Enlace fijo y preferencias

    [Fact]
    public async Task AceptarSinEnlace_UsaElEnlaceFijoDelTutor()
    {
        var context = ConTutor(link: "https://meet.example.com/mateo");
        context.Citas.Add(new Cita { Id = 50, EstudianteId = 1, TutorId = 2, MateriaId = 3, Estado = "Pendiente", FechaHoraInicio = Ahora.AddDays(1) });
        context.SaveChanges();

        var result = await Citas(context, 2, "Tutor").AceptarCita(50, new AceptarCitaRequestDto());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("https://meet.example.com/mateo", (await context.Citas.FindAsync(50))!.LinkReunion);
    }

    [Fact]
    public async Task GuardarPreferencias_ValidaYGuarda()
    {
        var context = ConTutor();
        var controller = Tutores(context, 2);

        ProblemaAssert.EsProblema(
            (await controller.GuardarPreferencias(new PreferenciasTutorDto { AntelacionMinimaHoras = 12, LinkReunion = "javascript:alert(1)" }, CancellationToken.None)).Result,
            400);
        ProblemaAssert.EsProblema(
            (await controller.GuardarPreferencias(new PreferenciasTutorDto { AntelacionMinimaHoras = 200 }, CancellationToken.None)).Result,
            400);

        var result = await controller.GuardarPreferencias(
            new PreferenciasTutorDto { AntelacionMinimaHoras = 24, LinkReunion = " https://meet.example.com/x " },
            CancellationToken.None);

        var guardadas = Assert.IsType<PreferenciasTutorDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(24, guardadas.AntelacionMinimaHoras);
        Assert.Equal("https://meet.example.com/x", guardadas.LinkReunion);
        Assert.Equal(24, (await context.Usuarios.FindAsync(2))!.AntelacionMinimaHoras);
    }
}
