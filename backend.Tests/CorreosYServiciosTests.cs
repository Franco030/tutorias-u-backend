using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Correos de cada aviso y servicios en segundo plano.
public class CorreosYServiciosTests
{
    private static readonly DateTime Ahora = new(2026, 10, 21, 10, 0, 0);

    private static readonly DatosCitaCorreo Datos = new(
        "Cálculo", new DateTime(2026, 10, 22, 16, 0, 0), "Ana Ruiz", "ana@example.com",
        "Mateo Herrera", "mateo@example.com", "Límites", 70);

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor))
            .Build();

    private static (NotificacionesCorreo servicio, List<CorreoPendiente> enviados) Correos()
    {
        var enviados = new List<CorreoPendiente>();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Callback<CorreoPendiente>(enviados.Add);
        var servicio = new NotificacionesCorreo(
            cola.Object, Config(("FrontendUrl", "https://tutorias.example.com")), NullLogger<NotificacionesCorreo>.Instance, new HoraFija(Ahora));
        return (servicio, enviados);
    }

    [Fact]
    public void Rechazo_LlegaAlEstudianteConElMotivo()
    {
        var (servicio, enviados) = Correos();

        servicio.CitaRechazada(Datos, "  Tengo examen ");
        servicio.CitaRechazada(Datos, null);

        Assert.All(enviados, c => Assert.Equal("ana@example.com", c.Para));
        Assert.Contains("Tengo examen", enviados[0].Html);
        Assert.Contains("https://tutorias.example.com/tutores", enviados[0].Html);
        Assert.DoesNotContain("Tengo examen", enviados[1].Html);
    }

    [Theory]
    [InlineData(true, "ana@example.com", "Mateo Herrera", "/mis-tutorias")]
    [InlineData(false, "mateo@example.com", "Ana Ruiz", "/tutor/mis-tutorias")]
    public void Cancelacion_LlegaALaOtraPersona(bool porTutor, string para, string quienCancela, string ruta)
    {
        var (servicio, enviados) = Correos();

        servicio.CitaCancelada(Datos, porTutor, "Me enfermé");

        var correo = Assert.Single(enviados);
        Assert.Equal(para, correo.Para);
        Assert.Contains($"{quienCancela} canceló la sesión", correo.Html);
        Assert.Contains($"https://tutorias.example.com{ruta}", correo.Html);
        Assert.Contains("Me enfermé", correo.Html);
    }

    [Fact]
    public void CierreDeLaSesion_AvisaAlEstudiante()
    {
        var (servicio, enviados) = Correos();

        servicio.SesionImpartida(Datos);
        servicio.InasistenciaRegistrada(Datos);

        Assert.All(enviados, c => Assert.Equal("ana@example.com", c.Para));
        Assert.Contains("Calificar la sesión", enviados[0].Html);
        Assert.Contains("No te conectaste", enviados[1].Asunto);
        Assert.Contains("https://tutorias.example.com/ayuda", enviados[1].Html);
    }

    [Fact]
    public void Postulacion_AprobadaYRechazada()
    {
        var (servicio, enviados) = Correos();

        servicio.TutorAprobado("Lucía", "lu@example.com");
        servicio.TutorRechazado("Diego", "die@example.com", "Documento ilegible");

        Assert.Equal(["lu@example.com", "die@example.com"], enviados.Select(c => c.Para));
        Assert.Contains("Lucía", enviados[0].Html);
        Assert.Contains("Documento ilegible", enviados[1].Html);
    }

    [Fact]
    public void SinDestinatario_NoSeEncola()
    {
        var (servicio, enviados) = Correos();

        servicio.TutorAprobado("Lucía", " ");

        Assert.Empty(enviados);
    }

    // Servicios en segundo plano

    [Fact]
    public async Task ServicioRecordatorios_RevisaAlArrancarYEnviaLosPendientes()
    {
        var notificaciones = new Mock<INotificacionesCorreo>();
        var enviado = new TaskCompletionSource();
        notificaciones.Setup(n => n.RecordatorioCita(It.IsAny<DatosCitaCorreo>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Callback(() => enviado.TrySetResult());

        var baseDatos = Guid.NewGuid().ToString();
        var servicios = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(baseDatos))
            .AddSingleton(notificaciones.Object)
            .AddSingleton<IHoraPlataforma>(new HoraFija(Ahora))
            .AddScoped<RecordatoriosCitas>()
            .BuildServiceProvider();
        using (var scope = servicios.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Semillas.TutorAprobado(context, 2, 3);
            context.Usuarios.Add(new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" });
            context.Citas.Add(new Cita { Id = 70, EstudianteId = 1, TutorId = 2, MateriaId = 3, Estado = "Aceptada", FechaHoraInicio = Ahora.AddHours(20) });
            context.SaveChanges();
        }

        var servicio = new ServicioRecordatorios(
            servicios.GetRequiredService<IServiceScopeFactory>(), Config(), NullLogger<ServicioRecordatorios>.Instance);
        await servicio.StartAsync(CancellationToken.None);
        await enviado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await servicio.StopAsync(CancellationToken.None);

        notificaciones.Verify(n => n.RecordatorioCita(It.Is<DatosCitaCorreo>(d => d.CitaId == 70), It.IsAny<string?>(), false), Times.Once);
    }

    [Fact]
    public async Task ServicioRecordatorios_Desactivado_NoHaceNada()
    {
        var scopes = new Mock<IServiceScopeFactory>();
        var servicio = new ServicioRecordatorios(scopes.Object, Config(("Recordatorios:Activos", "false")), NullLogger<ServicioRecordatorios>.Instance);

        await servicio.StartAsync(CancellationToken.None);
        await servicio.StopAsync(CancellationToken.None);

        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ColaCorreos_UnaFallaDeEnvioNoDetieneLaCola()
    {
        var email = new Mock<IEmailService>();
        var segundo = new TaskCompletionSource();
        email.Setup(e => e.SendEmailAsync("falla@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>>()))
            .ThrowsAsync(new InvalidOperationException("Azure no responde"));
        email.Setup(e => e.SendEmailAsync("ana@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>>()))
            .Callback(() => segundo.TrySetResult())
            .Returns(Task.CompletedTask);
        var servicios = new ServiceCollection().AddSingleton(email.Object).BuildServiceProvider();
        var cola = new ColaCorreos(servicios.GetRequiredService<IServiceScopeFactory>(), NullLogger<ColaCorreos>.Instance);

        await cola.StartAsync(CancellationToken.None);
        cola.Encolar(new CorreoPendiente("falla@example.com", "Uno", "<p>1</p>"));
        cola.Encolar(new CorreoPendiente("ana@example.com", "Dos", "<p>2</p>"));
        await segundo.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cola.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Recuperacion_SiNoSePuedeEncolar_NoRompeLaSolicitud()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Usuarios.Add(new Usuario
        {
            Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("clave123"), IsEmailVerified = false
        });
        context.SaveChanges();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Throws(new InvalidOperationException("cola llena"));
        var servicio = new RecuperacionCuenta(
            context,
            Config(("Jwt:Key", ApiDePrueba.JwtKey), ("Jwt:Issuer", "TutoriasUBackend"), ("Jwt:Audience", "TutoriasUFrontend"), ("FrontendUrl", "http://localhost:5173")),
            cola.Object,
            NullLogger<RecuperacionCuenta>.Instance);

        await servicio.SolicitarRestablecimientoAsync("ana@example.com");
        await servicio.ReenviarVerificacionAsync("ana@example.com");

        cola.Verify(c => c.Encolar(It.IsAny<CorreoPendiente>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EmailService_ConfiguradoSeConstruyeYSinConfigurarNoEnvia()
    {
        var configurado = new EmailService(
            Config(("COMMUNICATION_SERVICES_CS", "endpoint=https://prueba.communication.azure.com/;accesskey=dGVzdA=="), ("EmailSenderAddress", "no-responder@example.com")),
            NullLogger<EmailService>.Instance);
        Assert.NotNull(configurado);

        var sinConfigurar = new EmailService(Config(), NullLogger<EmailService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinConfigurar.SendEmailAsync("a@example.com", "Hola", "<p>Hola</p>", []));
    }
}
