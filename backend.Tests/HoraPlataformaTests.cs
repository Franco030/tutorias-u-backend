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
using backend.DTOs.Responses;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

public class HoraPlataformaTests
{
    private sealed class TiempoFijo(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }

    private static IConfiguration Config(string? zona = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ZonaHoraria"] = zona })
        .Build();

    [Fact]
    public void Ahora_ConvierteUtcALaHoraLocalDeLaPlataforma()
    {
        // 22:00 UTC son las 16:00 en Ciudad de México (UTC-6, sin horario de verano).
        var hora = new HoraPlataforma(new TiempoFijo(new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.Zero)), Config());

        Assert.Equal(new DateTime(2026, 10, 22, 16, 0, 0), hora.Ahora);
        Assert.Equal(DateTimeKind.Unspecified, hora.Ahora.Kind);
    }

    [Fact]
    public void Ahora_RespetaLaZonaConfigurada()
    {
        var hora = new HoraPlataforma(new TiempoFijo(new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.Zero)), Config("UTC"));

        Assert.Equal(new DateTime(2026, 10, 22, 22, 0, 0), hora.Ahora);
    }

    private static AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static CitasController CrearController(AppDbContext context, DateTime ahora, int usuarioId, string rol)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim(ClaimTypes.Role, rol)
        }, "mock"));
        return new CitasController(context, Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(ahora))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    [Fact]
    public async Task AgendarCita_AceptaUnBloqueQueAunNoLlegaEnHoraLocal()
    {
        // Son las 10:00 locales (16:00 UTC): el bloque de las 16:00 sigue disponible.
        var context = GetDbContext();
        var ahora = new DateTime(2026, 10, 22, 10, 0, 0);
        Semillas.TutorAprobado(context, 2, 3);
        context.TutorDisponibilidads.Add(new TutorDisponibilidad
        {
            TutorId = 2,
            DiaSemana = (byte)ahora.DayOfWeek,
            HoraInicio = new TimeOnly(16, 0),
            HoraFin = new TimeOnly(17, 0)
        });
        await context.SaveChangesAsync();

        var result = await CrearController(context, ahora, 1, "Estudiante").AgendarCita(new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            // Un cliente antiguo la manda con "Z": se interpreta como hora local.
            FechaHoraInicio = new DateTime(2026, 10, 22, 16, 0, 0, DateTimeKind.Utc),
            FechaHoraFin = new DateTime(2026, 10, 22, 17, 0, 0, DateTimeKind.Utc)
        });

        Assert.IsType<CreatedResult>(result);
        var cita = await context.Citas.SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 22, 16, 0, 0), cita.FechaHoraInicio);
    }

    [Fact]
    public async Task Historial_NoCompletaUnaCitaQueAunNoOcurreEnHoraLocal()
    {
        var context = GetDbContext();
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 7, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local" });
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Citas.Add(new Cita
        {
            Id = 30,
            EstudianteId = 1,
            TutorId = 7,
            MateriaId = 1,
            Estado = "Aceptada",
            FechaHoraInicio = new DateTime(2026, 10, 22, 16, 0, 0)
        });
        await context.SaveChangesAsync();
        var controller = CrearController(context, new DateTime(2026, 10, 22, 10, 0, 0), 1, "Estudiante");

        var historial = Assert.IsType<OkObjectResult>(await controller.ObtenerHistorial());
        var proximas = Assert.IsType<OkObjectResult>(await controller.ObtenerProximasCitas());

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ProximaCitaResponseDto>>(historial.Value));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<ProximaCitaResponseDto>>(proximas.Value));
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(30))!.Estado);
    }

    [Fact]
    public async Task SesionEnCurso_SigueEnProximasYSeCompletaAlTerminarSuHora()
    {
        var context = GetDbContext();
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 7, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local" });
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Citas.AddRange(
            new Cita { Id = 31, EstudianteId = 1, TutorId = 7, MateriaId = 1, Estado = "Aceptada", FechaHoraInicio = new DateTime(2026, 10, 22, 16, 0, 0) },
            // Una solicitud que nadie aceptó no se queda en próximas al pasar su hora.
            new Cita { Id = 32, EstudianteId = 1, TutorId = 7, MateriaId = 1, Estado = "Pendiente", FechaHoraInicio = new DateTime(2026, 10, 22, 15, 0, 0) });
        await context.SaveChangesAsync();

        async Task<(List<int> proximas, List<int> historial)> Consultar(DateTime ahora)
        {
            var controller = CrearController(context, ahora, 1, "Estudiante");
            var historial = Assert.IsType<OkObjectResult>(await controller.ObtenerHistorial());
            var proximas = Assert.IsType<OkObjectResult>(await controller.ObtenerProximasCitas());
            return (
                Assert.IsAssignableFrom<IEnumerable<ProximaCitaResponseDto>>(proximas.Value).Select(c => c.Id).ToList(),
                Assert.IsAssignableFrom<IEnumerable<ProximaCitaResponseDto>>(historial.Value).Select(c => c.Id).ToList());
        }

        // 16:20: la sesión de las 16:00 está en curso.
        var enCurso = await Consultar(new DateTime(2026, 10, 22, 16, 20, 0));
        Assert.Equal([31], enCurso.proximas);
        Assert.Equal([32], enCurso.historial);
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(31))!.Estado);

        // 17:00: terminó su hora; pasa al historial como completada.
        var terminada = await Consultar(new DateTime(2026, 10, 22, 17, 0, 0));
        Assert.Empty(terminada.proximas);
        Assert.Equal([31, 32], terminada.historial);
        Assert.Equal("Completada", (await context.Citas.FindAsync(31))!.Estado);
    }
}
