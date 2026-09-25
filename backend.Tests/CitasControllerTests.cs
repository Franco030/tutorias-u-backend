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
            FechaHoraInicio = DateTime.UtcNow.AddDays(1)
        };

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
        Assert.Equal("Aceptada", citaEnDb.Estado);
        
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
            FechaHoraInicio = DateTime.UtcNow.AddDays(-1)
        };

        // Act
        var result = await controller.AgendarCita(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No puedes agendar en el pasado", badRequestResult.Value);
    }
}
