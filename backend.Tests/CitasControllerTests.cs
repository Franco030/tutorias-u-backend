using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
