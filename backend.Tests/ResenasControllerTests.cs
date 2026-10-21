using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Resenas;
using backend.Models;

namespace backend.Tests;

public class ResenasControllerTests
{
    private const int EstudianteId = 1;
    private const int TutorId = 7;

    private static AppDbContext GetDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static ResenasController CrearController(AppDbContext context, int usuarioId = EstudianteId)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim(ClaimTypes.Role, "Estudiante")
        }, "mock"));

        return new ResenasController(context, HoraFija.Actual())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static void Sembrar(AppDbContext context, params Cita[] citas)
    {
        context.Usuarios.AddRange(
            new Usuario { Id = EstudianteId, Email = "ana@example.com", Nombre = "Ana Ruiz", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 2, Email = "luis@example.com", Nombre = "Luis Mora", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = TutorId, Email = "mateo@example.com", Nombre = "Mateo Herrera", Rol = "Tutor", AuthProvider = "Local", CalificacionPromedio = 0 });
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Citas.AddRange(citas);
        context.SaveChanges();
    }

    private static Cita NuevaCita(int id, string estado = "Completada", int estudianteId = EstudianteId) => new()
    {
        Id = id,
        EstudianteId = estudianteId,
        TutorId = TutorId,
        MateriaId = 1,
        Estado = estado,
        FechaHoraInicio = new DateTime(2026, 9, id, 15, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task CrearResena_GuardaYRecalculaElPromedioDelTutor()
    {
        var context = GetDbContext();
        Sembrar(context, NuevaCita(10), NuevaCita(11));
        context.Resenas.Add(new Resena { CitaId = 11, EstudianteId = EstudianteId, TutorId = TutorId, Puntuacion = 4 });
        context.SaveChanges();

        var result = await CrearController(context).CrearResena(new CrearResenaRequestDto
        {
            CitaId = 10,
            Puntuacion = 5,
            Comentario = "  Explica muy claro.  "
        });

        Assert.IsType<CreatedResult>(result);
        var resena = await context.Resenas.SingleAsync(r => r.CitaId == 10);
        Assert.Equal(5, resena.Puntuacion);
        Assert.Equal("Explica muy claro.", resena.Comentario);
        var tutor = await context.Usuarios.FindAsync(TutorId);
        Assert.Equal(4.5m, tutor!.CalificacionPromedio);
    }

    [Fact]
    public async Task CrearResena_RechazaCalificarDosVecesLaMismaCita()
    {
        var context = GetDbContext();
        Sembrar(context, NuevaCita(10));
        context.Resenas.Add(new Resena { CitaId = 10, EstudianteId = EstudianteId, TutorId = TutorId, Puntuacion = 3 });
        context.SaveChanges();

        var result = await CrearController(context).CrearResena(new CrearResenaRequestDto { CitaId = 10, Puntuacion = 5 });

        ProblemaAssert.EsProblema(result, 409);
        Assert.Equal(1, await context.Resenas.CountAsync());
    }

    [Fact]
    public async Task CrearResena_RechazaCitasNoCompletadas()
    {
        var context = GetDbContext();
        Sembrar(context, NuevaCita(10, estado: "Aceptada"));

        var result = await CrearController(context).CrearResena(new CrearResenaRequestDto { CitaId = 10, Puntuacion = 5 });

        ProblemaAssert.EsProblema(result, 400);
        Assert.Empty(context.Resenas);
    }

    [Fact]
    public async Task CrearResena_RechazaCitasDeOtroEstudiante()
    {
        var context = GetDbContext();
        Sembrar(context, NuevaCita(10, estudianteId: 2));

        var result = await CrearController(context).CrearResena(new CrearResenaRequestDto { CitaId = 10, Puntuacion = 5 });

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(context.Resenas);
    }

    [Fact]
    public async Task ObtenerResenasTutor_DevuelvePromedioDesgloseYPagina()
    {
        var context = GetDbContext();
        Sembrar(context, NuevaCita(10), NuevaCita(11), NuevaCita(12, estudianteId: 2));
        context.Resenas.AddRange(
            new Resena { CitaId = 10, EstudianteId = EstudianteId, TutorId = TutorId, Puntuacion = 5, FechaCreacion = new DateTime(2026, 9, 10) },
            new Resena { CitaId = 11, EstudianteId = EstudianteId, TutorId = TutorId, Puntuacion = 4, FechaCreacion = new DateTime(2026, 9, 11) },
            new Resena { CitaId = 12, EstudianteId = 2, TutorId = TutorId, Puntuacion = 5, Comentario = "Excelente", FechaCreacion = new DateTime(2026, 9, 12) });
        context.SaveChanges();

        var result = await CrearController(context).ObtenerResenasTutor(TutorId, pagina: 1, tamanoPagina: 2);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var respuesta = Assert.IsType<ResenasTutorResponseDto>(ok.Value);
        Assert.Equal(3, respuesta.Total);
        Assert.Equal(4.67m, respuesta.Promedio);
        Assert.Equal(new[] { 2, 1, 0, 0, 0 }, respuesta.Desglose.Select(d => d.Cantidad));
        Assert.Equal(2, respuesta.Items.Count);
        Assert.Equal("Luis Mora", respuesta.Items[0].EstudianteNombre);
        Assert.Equal("Excelente", respuesta.Items[0].Comentario);
    }

    [Fact]
    public async Task ObtenerResenasTutor_DevuelveNotFoundSiNoEsTutor()
    {
        var context = GetDbContext();
        Sembrar(context);

        var result = await CrearController(context).ObtenerResenasTutor(EstudianteId);

        ProblemaAssert.EsProblema(result.Result, 404);
    }
}
