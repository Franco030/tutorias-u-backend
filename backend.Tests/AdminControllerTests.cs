using backend.Services.Interfaces;
using Moq;
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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).ObtenerSolicitudes(CancellationToken.None);

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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).AprobarTutor(10, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        
        var userInDb = await context.Usuarios.FindAsync(10);
        Assert.Equal((int)EstadoAprobacion.Aprobado, userInDb!.EstadoAprobacionId);
        Assert.Equal("Tutor", userInDb.Rol);
    }

    [Fact]
    public async Task ObtenerSolicitudes_ShouldIncludeLatestCredentialAndSubjects()
    {
        await using var context = CreateContext();
        var materia = new Materia { Id = 5, Nombre = "Cálculo" };
        context.Materias.Add(materia);
        context.Usuarios.Add(new Usuario
        {
            Id = 20,
            Nombre = "Aspirante",
            Email = "aspirante@ejemplo.com",
            Rol = "Estudiante",
            AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
        });
        context.TutorSolicitudesCredenciales.AddRange(
            new TutorSolicitudesCredenciale
            {
                Id = 1,
                UsuarioId = 20,
                UrlCredencial = "https://archivos/vieja.pdf",
                FechaSolicitud = new DateTime(2026, 1, 1)
            },
            new TutorSolicitudesCredenciale
            {
                Id = 2,
                UsuarioId = 20,
                UrlCredencial = "https://archivos/nueva.pdf",
                FechaSolicitud = new DateTime(2026, 2, 1),
                SolicitudMateria = { new SolicitudMateria { Id = 1, MateriaId = 5, Materia = materia } }
            });
        await context.SaveChangesAsync();

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).ObtenerSolicitudes(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var solicitud = Assert.Single(
            Assert.IsAssignableFrom<IReadOnlyList<backend.DTOs.Responses.TutorSolicitudDto>>(ok.Value));
        Assert.Equal("https://archivos/nueva.pdf", solicitud.UrlCredencial);
        Assert.Equal(new DateTime(2026, 2, 1), solicitud.FechaSolicitud);
        Assert.Equal(new[] { "Cálculo" }, solicitud.Materias);
    }

    [Fact]
    public async Task RechazarTutor_ShouldMarkPendingRequestAsRejected()
    {
        await using var context = CreateContext();
        context.Usuarios.Add(new Usuario
        {
            Id = 30,
            Nombre = "Aspirante",
            Email = "rechazo@ejemplo.com",
            Rol = "Estudiante",
            AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
        });
        await context.SaveChangesAsync();

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).RechazarTutor(30, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var userInDb = await context.Usuarios.FindAsync(30);
        Assert.Equal((int)EstadoAprobacion.Rechazado, userInDb!.EstadoAprobacionId);
        Assert.Equal("Estudiante", userInDb.Rol);
    }

    [Fact]
    public async Task RechazarTutor_ShouldReturnNotFoundWhenNotPending()
    {
        await using var context = CreateContext();
        context.Usuarios.Add(new Usuario
        {
            Id = 31,
            Nombre = "Tutor",
            Email = "aprobado2@ejemplo.com",
            Rol = "Tutor",
            AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Aprobado
        });
        await context.SaveChangesAsync();

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).RechazarTutor(31, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
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
