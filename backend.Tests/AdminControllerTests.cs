using backend.Controllers;
using backend.Data;
using backend.Enums;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Tests;

public class AdminControllerTests
{
    [Fact]
    public async Task ObtenerSolicitudes_ShouldReturnOnlyPendingTutorsWithoutSensitiveData()
    {
        await using var context = CreateContext();
        context.Usuarios.AddRange(
            new Usuario
            {
                Id = 1,
                Nombre = "Tutor pendiente",
                Email = "pendiente@ejemplo.com",
                PasswordHash = "hash-no-debe-exponerse",
                Rol = "Estudiante",
                AuthProvider = "Local",
                EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
            },
            new Usuario
            {
                Id = 2,
                Nombre = "Tutor aprobado",
                Email = "aprobado@ejemplo.com",
                PasswordHash = "otro-hash",
                Rol = "Tutor",
                AuthProvider = "Local",
                EstadoAprobacionId = (int)EstadoAprobacion.Aprobado
            },
            new Usuario
            {
                Id = 3,
                Nombre = "Estudiante",
                Email = "estudiante@ejemplo.com",
                Rol = "Estudiante",
                AuthProvider = "Local",
                EstadoAprobacionId = (int)EstadoAprobacion.Ninguno
            });
        await context.SaveChangesAsync();

        var result = await new AdminController(context).ObtenerSolicitudes(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var solicitudes = Assert.IsAssignableFrom<IReadOnlyList<backend.DTOs.Responses.TutorSolicitudDto>>(ok.Value);
        var solicitud = Assert.Single(solicitudes);
        Assert.Equal(1, solicitud.Id);
        Assert.Equal("Tutor pendiente", solicitud.Nombre);
        Assert.Equal("pendiente@ejemplo.com", solicitud.Correo);
    }

    [Fact]
    public async Task AprobarTutor_ShouldChangePendingStatus()
    {
        await using var context = CreateContext();
        context.Usuarios.Add(new Usuario
        {
            Id = 10,
            Nombre = "Tutor",
            Email = "tutor@ejemplo.com",
            Rol = "Estudiante",
            AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
        });
        await context.SaveChangesAsync();

        var result = await new AdminController(context).AprobarTutor(10, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        
        var userInDb = await context.Usuarios.FindAsync(10);
        Assert.Equal((int)EstadoAprobacion.Aprobado, userInDb!.EstadoAprobacionId);
        Assert.Equal("Tutor", userInDb.Rol);
    }

    [Fact]
    public void Controller_ShouldRequireAdministratorRole()
    {
        var authorize = Assert.Single(
            typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));

        Assert.Equal("Administrador", ((AuthorizeAttribute)authorize).Roles);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
