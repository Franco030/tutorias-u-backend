using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Citas;
using backend.Models;
using backend.Services.Interfaces;

namespace backend.Tests;

public class DobleReservaTests
{
    // Simula el índice único UX_Citas_Tutor_Horario rechazando el guardado.
    private sealed class ContextoConIndiceUnico(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public bool RechazarGuardado { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            RechazarGuardado
                ? throw new DbUpdateException("Cannot insert duplicate key row in object 'dbo.Citas' with unique index 'UX_Citas_Tutor_Horario'.")
                : base.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task AgendarCita_DevuelveConflictoSiOtraReservaGanaElBloque()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new ContextoConIndiceUnico(options);
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
        context.RechazarGuardado = true;

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Estudiante")
        }, "mock"));
        var controller = new CitasController(context, Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(ahora))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };

        var result = await controller.AgendarCita(new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = new DateTime(2026, 10, 22, 16, 0, 0),
            FechaHoraFin = new DateTime(2026, 10, 22, 17, 0, 0)
        });

        ProblemaAssert.EsProblema(result, 409, "El tutor ya tiene un compromiso en este horario");
    }
}
