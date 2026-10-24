using backend.DTOs.Requests;
using backend.Services.Interfaces;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.Enums;
using backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).ObtenerSolicitudes(BlobQueFirma(), CancellationToken.None);

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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).ObtenerSolicitudes(BlobQueFirma(), CancellationToken.None);

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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).RechazarTutor(30, new RechazarTutorRequest("Credencial inválida"), CancellationToken.None);

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

        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).RechazarTutor(31, new RechazarTutorRequest("Credencial inválida"), CancellationToken.None);

        ProblemaAssert.EsProblema(result, 404);
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

    [Fact]
    public async Task RechazarTutor_RechazaMotivosDeMasDe500Caracteres()
    {
        var context = CreateContext();
        var result = await new AdminController(context, Mock.Of<INotificacionesCorreo>())
            .RechazarTutor(30, new RechazarTutorRequest(new string('x', 501)), CancellationToken.None);

        ProblemaAssert.EsProblema(result, 400, "El motivo no puede superar los 500 caracteres.");
    }

    [Fact]
    public async Task RechazarTutor_FuncionaSinMotivo()
    {
        var context = CreateContext();
        context.Usuarios.Add(new Usuario { Id = 32, Nombre = "Sin Motivo", Email = "sm@example.com", Rol = "Estudiante", AuthProvider = "Local", EstadoAprobacionId = (int)EstadoAprobacion.Pendiente });
        await context.SaveChangesAsync();
        var notificaciones = new Mock<INotificacionesCorreo>();

        var result = await new AdminController(context, notificaciones.Object).RechazarTutor(32, null, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        notificaciones.Verify(n => n.TutorRechazado("Sin Motivo", "sm@example.com", null), Times.Once);
    }

    // Firma "de mentira": devuelve la misma URL para comparar en las pruebas.
    private static IBlobService BlobQueFirma()
    {
        var blob = new Mock<IBlobService>();
        blob.Setup(b => b.GenerarUrlLectura(It.IsAny<string?>(), It.IsAny<TimeSpan>()))
            .Returns((string? url, TimeSpan _) => url);
        return blob.Object;
    }
}

// Rechazar pasando por HTTP: la lectura del cuerpo (y su validación) no corre
// cuando se llama al controlador directamente. Ahí estaba el error 500.
public class AdminApiTests
{
    private static async Task<(ApiDePrueba Api, HttpClient Cliente)> ComoAdministrador()
    {
        var api = new ApiDePrueba();
        using var scope = api.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "admin@example.com", Nombre = "Coordinación", Rol = "Administrador", AuthProvider = "Local", IsEmailVerified = true, EstadoAprobacionId = (int)EstadoAprobacion.Ninguno },
            new Usuario { Id = 30, Email = "lu@example.com", Nombre = "Lucía", Rol = "Estudiante", AuthProvider = "Local", IsEmailVerified = true, EstadoAprobacionId = (int)EstadoAprobacion.Pendiente });
        await context.SaveChangesAsync();
        var sesion = await scope.ServiceProvider.GetRequiredService<IAuthService>().RefrescarSesionAsync(1);
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.Token);
        return (api, cliente);
    }

    [Fact]
    public async Task Rechazar_ConMotivo_Responde204YCambiaElEstado()
    {
        var (api, cliente) = await ComoAdministrador();
        using var _ = api;

        var respuesta = await cliente.PutAsJsonAsync("/api/admin/tutores/30/rechazar", new { motivo = "Comprobante ilegible" });

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        using var scope = api.Services.CreateScope();
        var lucia = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Usuarios.FindAsync(30);
        Assert.Equal((int)EstadoAprobacion.Rechazado, lucia!.EstadoAprobacionId);
    }

    [Fact]
    public async Task Rechazar_ConMotivoDemasiadoLargo_Responde400EnEspanol()
    {
        var (api, cliente) = await ComoAdministrador();
        using var _ = api;

        var respuesta = await cliente.PutAsJsonAsync("/api/admin/tutores/30/rechazar", new { motivo = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("500 caracteres", await respuesta.Content.ReadAsStringAsync());
    }

    // En un record posicional, la validación con "property:" hace que ASP.NET
    // lance un error al leer el cuerpo: ningún DTO debe tenerla.
    [Fact]
    public void RecordsPosicionales_NoValidanEnLasPropiedades()
    {
        var conProblema =
            from tipo in typeof(Program).Assembly.GetTypes()
            where tipo.Namespace?.StartsWith("backend.DTOs") == true
            let constructor = tipo.GetConstructors().FirstOrDefault(c => c.GetParameters().Length > 0)
            where constructor != null && tipo.GetMethod("<Clone>$") != null
            from parametro in constructor.GetParameters()
            let propiedad = tipo.GetProperty(parametro.Name!)
            where propiedad != null && propiedad.GetCustomAttributes(typeof(ValidationAttribute), true).Length > 0
            select $"{tipo.Name}.{propiedad.Name}";

        Assert.Empty(conProblema);
    }
}
