using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.Models;
using backend.Services.Interfaces;

namespace backend.Tests;

// El tutor confirma si la sesión se impartió o si el estudiante no asistió.
public class AsistenciaSesionesTests
{
    // Jueves 22 de octubre de 2026, 17:30: la sesión de las 16:00 ya terminó.
    private static readonly DateTime Ahora = new(2026, 10, 22, 17, 30, 0);

    private static AppDbContext ConCita(DateTime inicio, string estado = "Aceptada")
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 7, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local" });
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Citas.Add(new Cita { Id = 50, EstudianteId = 1, TutorId = 7, MateriaId = 1, Estado = estado, FechaHoraInicio = inicio });
        context.SaveChanges();
        return context;
    }

    private static (CitasController controller, Mock<INotificacionesCorreo> notificaciones) Controller(AppDbContext context, int usuarioId = 7)
    {
        var notificaciones = new Mock<INotificacionesCorreo>();
        var controller = new CitasController(context, notificaciones.Object, NullLogger<CitasController>.Instance, new HoraFija(Ahora))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                        new Claim(ClaimTypes.Role, "Tutor")
                    }, "mock"))
                }
            }
        };
        return (controller, notificaciones);
    }

    [Fact]
    public async Task Impartida_CompletaLaSesionEInvitaACalificar()
    {
        var context = ConCita(new DateTime(2026, 10, 22, 16, 0, 0));
        var (controller, notificaciones) = Controller(context);

        Assert.IsType<OkObjectResult>(await controller.CompletarCita(50));

        Assert.Equal("Completada", (await context.Citas.FindAsync(50))!.Estado);
        notificaciones.Verify(n => n.SesionImpartida(It.Is<DatosCitaCorreo>(d => d.CitaId == 50 && d.EstudianteEmail == "ana@example.com")), Times.Once);
        notificaciones.Verify(n => n.InasistenciaRegistrada(It.IsAny<DatosCitaCorreo>()), Times.Never);
    }

    [Fact]
    public async Task Inasistencia_MarcaLaSesionYAvisaAlEstudiante()
    {
        var context = ConCita(new DateTime(2026, 10, 22, 16, 0, 0));
        var (controller, notificaciones) = Controller(context);

        Assert.IsType<OkObjectResult>(await controller.RegistrarInasistencia(50));

        Assert.Equal("NoAsistio", (await context.Citas.FindAsync(50))!.Estado);
        notificaciones.Verify(n => n.InasistenciaRegistrada(It.Is<DatosCitaCorreo>(d => d.CitaId == 50)), Times.Once);
    }

    [Fact]
    public async Task NoSeConfirmaUnaSesionQueTodaviaNoEmpieza()
    {
        var context = ConCita(new DateTime(2026, 10, 22, 18, 0, 0));
        var (controller, _) = Controller(context);

        var resultado = Assert.IsType<ObjectResult>(await controller.CompletarCita(50));
        Assert.Equal(409, resultado.StatusCode);
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(50))!.Estado);
    }

    [Theory]
    [InlineData("Completada")]
    [InlineData("NoAsistio")]
    [InlineData("Cancelada")]
    [InlineData("Pendiente")]
    public async Task SoloSeConfirmaUnaSesionAceptada(string estado)
    {
        var context = ConCita(new DateTime(2026, 10, 22, 16, 0, 0), estado);
        var (controller, notificaciones) = Controller(context);

        var resultado = Assert.IsType<ObjectResult>(await controller.RegistrarInasistencia(50));
        Assert.Equal(409, resultado.StatusCode);
        Assert.Equal(estado, (await context.Citas.FindAsync(50))!.Estado);
        notificaciones.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OtroTutorNoPuedeConfirmar()
    {
        var context = ConCita(new DateTime(2026, 10, 22, 16, 0, 0));
        var (controller, _) = Controller(context, usuarioId: 99);

        Assert.IsType<ForbidResult>(await controller.CompletarCita(50));
        Assert.Equal("Aceptada", (await context.Citas.FindAsync(50))!.Estado);
    }
}
