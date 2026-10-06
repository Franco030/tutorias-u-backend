using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Xunit;
using backend.Controllers;
using backend.Data;
using backend.Models;
using backend.DTOs.Citas;

namespace backend.Tests;

public class CitasControllerTests
{
    private AppDbContext GetDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task ObtenerPendientes_ReturnsOnlyPendingCitasForAuthenticatedTutor()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var fechaHoraInicio = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);
        var estudiante = new Usuario
        {
            Id = 1,
            Email = "estudiante@example.com",
            Nombre = "Ana Estudiante",
            Rol = "Estudiante",
            AuthProvider = "Local"
        };
        var otraEstudiante = new Usuario
        {
            Id = 2,
            Email = "otro@example.com",
            Nombre = "Otro Estudiante",
            Rol = "Estudiante",
            AuthProvider = "Local"
        };
        var materia = new Materia { Id = 1, Nombre = "Matemáticas" };
        var otraMateria = new Materia { Id = 2, Nombre = "Física" };

        context.Citas.AddRange(
            new Cita
            {
                Id = 10,
                TutorId = 7,
                EstudianteId = estudiante.Id,
                MateriaId = materia.Id,
                Estado = "Pendiente",
                FechaHoraInicio = fechaHoraInicio,
                Estudiante = estudiante,
                Materia = materia
            },
            new Cita
            {
                Id = 11,
                TutorId = 7,
                EstudianteId = otraEstudiante.Id,
                MateriaId = otraMateria.Id,
                Estado = "Aceptada",
                FechaHoraInicio = fechaHoraInicio.AddHours(1),
                Estudiante = otraEstudiante,
                Materia = otraMateria
            },
            new Cita
            {
                Id = 12,
                TutorId = 8,
                EstudianteId = otraEstudiante.Id,
                MateriaId = otraMateria.Id,
                Estado = "Pendiente",
                FechaHoraInicio = fechaHoraInicio.AddHours(2),
                Estudiante = otraEstudiante,
                Materia = otraMateria
            });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");
        var authorizeAttribute = typeof(CitasController)
            .GetMethod(nameof(CitasController.ObtenerPendientes))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();

        var result = await controller.ObtenerPendientes();

        Assert.Equal("Tutor", authorizeAttribute.Roles);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var citas = Assert.IsAssignableFrom<IReadOnlyList<CitaPendienteResponseDto>>(okResult.Value);
        var cita = Assert.Single(citas);
        Assert.Equal(10, cita.Id);
        Assert.Equal("Ana Estudiante", cita.EstudianteNombre);
        Assert.Equal("Matemáticas", cita.MateriaNombre);
        Assert.Equal(fechaHoraInicio, cita.FechaHoraInicio);
        Assert.Equal("Pendiente", cita.Estado);
    }

    [Fact]
    public async Task ObtenerPendientes_InvalidUserClaim_ReturnsUnauthorized()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "invalid");

        var result = await controller.ObtenerPendientes();

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task AgendarCita_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var context = GetDbContext(dbName);
        var controller = new CitasController(context);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1")
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var request = new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = NextWeekday(DateTime.UtcNow, DayOfWeek.Monday).AddHours(10),
            FechaHoraFin = NextWeekday(DateTime.UtcNow, DayOfWeek.Monday).AddHours(11)
        };
        context.TutorDisponibilidads.Add(new TutorDisponibilidad
        {
            TutorId = request.TutorId,
            DiaSemana = (byte)request.FechaHoraInicio.DayOfWeek,
            HoraInicio = TimeOnly.FromDateTime(request.FechaHoraInicio),
            HoraFin = TimeOnly.FromDateTime(request.FechaHoraFin)
        });
        await context.SaveChangesAsync();

        // Act
        var result = await controller.AgendarCita(request);

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result);
        var responseValue = createdResult.Value;
        Assert.NotNull(responseValue);

        var citaEnDb = await context.Citas.FirstOrDefaultAsync();
        Assert.NotNull(citaEnDb);
        Assert.Equal(1, citaEnDb.EstudianteId);
        Assert.Equal(2, citaEnDb.TutorId);
        Assert.Equal(3, citaEnDb.MateriaId);
        Assert.Equal("Pendiente", citaEnDb.Estado);
    }

    [Fact]
    public async Task AgendarCita_PastDate_ReturnsBadRequest()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var context = GetDbContext(dbName);
        var controller = new CitasController(context);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1")
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var request = new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = DateTime.UtcNow.AddDays(-1),
            FechaHoraFin = DateTime.UtcNow
        };

        // Act
        var result = await controller.AgendarCita(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No puedes agendar en el pasado", badRequestResult.Value);
    }

    [Fact]
    public async Task AgendarCita_WithoutMatchingAvailability_ReturnsBadRequest()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "1");
        var inicio = NextWeekday(DateTime.UtcNow, DayOfWeek.Monday).AddHours(10);

        var result = await controller.AgendarCita(new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = inicio,
            FechaHoraFin = inicio.AddHours(1)
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await context.Citas.ToListAsync());
    }

    [Fact]
    public async Task AgendarCita_WithDifferentEndTime_ReturnsBadRequest()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "1");
        var inicio = NextWeekday(DateTime.UtcNow, DayOfWeek.Monday).AddHours(10);
        context.TutorDisponibilidads.Add(new TutorDisponibilidad
        {
            TutorId = 2,
            DiaSemana = (byte)inicio.DayOfWeek,
            HoraInicio = TimeOnly.FromDateTime(inicio),
            HoraFin = TimeOnly.FromDateTime(inicio.AddHours(2))
        });
        await context.SaveChangesAsync();

        var result = await controller.AgendarCita(new CrearCitaRequestDto
        {
            TutorId = 2,
            MateriaId = 3,
            FechaHoraInicio = inicio,
            FechaHoraFin = inicio.AddHours(1)
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await context.Citas.ToListAsync());
    }

    [Fact]
    public async Task AceptarCita_ValidRequest_UpdatesCita()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        context.Citas.Add(new Cita
        {
            Id = 20,
            TutorId = 7,
            EstudianteId = 1,
            MateriaId = 1,
            Estado = "Pendiente",
            FechaHoraInicio = DateTime.UtcNow.AddDays(2)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");
        var request = new AceptarCitaRequestDto
        {
            LinkReunion = "https://meet.google.com/abc-defg-hij",
            Notas = "Repasar derivadas antes de la sesión."
        };

        // Act
        var result = await controller.AceptarCita(20, request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var citaEnDb = await context.Citas.FindAsync(20);
        Assert.NotNull(citaEnDb);
        Assert.Equal("Aceptada", citaEnDb.Estado);
        Assert.Equal("https://meet.google.com/abc-defg-hij", citaEnDb.LinkReunion);
        Assert.Equal("Repasar derivadas antes de la sesión.", citaEnDb.Notas);
    }

    [Fact]
    public async Task AceptarCita_WithoutOptionalFields_StillAcceptsCita()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        context.Citas.Add(new Cita
        {
            Id = 21,
            TutorId = 7,
            EstudianteId = 1,
            MateriaId = 1,
            Estado = "Pendiente",
            FechaHoraInicio = DateTime.UtcNow.AddDays(2)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");
        var request = new AceptarCitaRequestDto
        {
            LinkReunion = null,
            Notas = null
        };

        // Act
        var result = await controller.AceptarCita(21, request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var citaEnDb = await context.Citas.FindAsync(21);
        Assert.NotNull(citaEnDb);
        Assert.Equal("Aceptada", citaEnDb.Estado);
        Assert.Null(citaEnDb.LinkReunion);
        Assert.Null(citaEnDb.Notas);
    }

    [Fact]
    public async Task AceptarCita_CitaNotFound_ReturnsNotFound()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "7");
        var request = new AceptarCitaRequestDto();

        // Act
        var result = await controller.AceptarCita(999, request);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AceptarCita_DifferentTutor_ReturnsForbidden()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        context.Citas.Add(new Cita
        {
            Id = 22,
            TutorId = 8,
            EstudianteId = 1,
            MateriaId = 1,
            Estado = "Pendiente",
            FechaHoraInicio = DateTime.UtcNow.AddDays(2)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");
        var request = new AceptarCitaRequestDto();

        // Act
        var result = await controller.AceptarCita(22, request);

        // Assert
        Assert.IsType<ForbidResult>(result);
        var citaEnDb = await context.Citas.FindAsync(22);
        Assert.NotNull(citaEnDb);
        Assert.Equal("Pendiente", citaEnDb.Estado);
    }

    [Fact]
    public async Task AceptarCita_InvalidClaim_ReturnsUnauthorized()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "invalid");

        var result = await controller.AceptarCita(1, new AceptarCitaRequestDto());

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RechazarCita_ValidRequest_UpdatesEstado()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        context.Citas.Add(new Cita
        {
            Id = 30,
            TutorId = 7,
            EstudianteId = 1,
            MateriaId = 1,
            Estado = "Pendiente",
            FechaHoraInicio = DateTime.UtcNow.AddDays(2)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");

        // Act
        var result = await controller.RechazarCita(30);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        var citaEnDb = await context.Citas.FindAsync(30);
        Assert.NotNull(citaEnDb);
        Assert.Equal("Rechazada", citaEnDb.Estado);
    }

    [Fact]
    public async Task RechazarCita_CitaNotFound_ReturnsNotFound()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "7");

        var result = await controller.RechazarCita(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RechazarCita_DifferentTutor_ReturnsForbidden()
    {
        // Arrange
        var context = GetDbContext(Guid.NewGuid().ToString());
        context.Citas.Add(new Cita
        {
            Id = 31,
            TutorId = 8,
            EstudianteId = 1,
            MateriaId = 1,
            Estado = "Pendiente",
            FechaHoraInicio = DateTime.UtcNow.AddDays(2)
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, "7");

        // Act
        var result = await controller.RechazarCita(31);

        // Assert
        Assert.IsType<ForbidResult>(result);
        var citaEnDb = await context.Citas.FindAsync(31);
        Assert.NotNull(citaEnDb);
        Assert.Equal("Pendiente", citaEnDb.Estado);
    }

    [Fact]
    public async Task RechazarCita_InvalidClaim_ReturnsUnauthorized()
    {
        var context = GetDbContext(Guid.NewGuid().ToString());
        var controller = CreateController(context, "invalid");

        var result = await controller.RechazarCita(1);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public void AceptarCita_HasTutorAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(CitasController)
            .GetMethod(nameof(CitasController.AceptarCita))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("Tutor", authorizeAttribute.Roles);
    }

    [Fact]
    public void RechazarCita_HasTutorAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(CitasController)
            .GetMethod(nameof(CitasController.RechazarCita))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("Tutor", authorizeAttribute.Roles);
    }

    private static CitasController CreateController(AppDbContext context, string userId)
    {
        var controller = new CitasController(context);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            "mock"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private static DateTime NextWeekday(DateTime date, DayOfWeek dayOfWeek)
    {
        var daysUntil = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
        return date.Date.AddDays(daysUntil == 0 ? 7 : daysUntil);
    }
}