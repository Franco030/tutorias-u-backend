using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Data;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;

namespace backend.Tests;

// Recordatorios por correo (un día y una hora antes) y archivo .ics de la cita.
public class RecordatoriosCitasTests
{
    // Miércoles 21 de octubre de 2026, 10:00 en la hora de la plataforma.
    private static readonly DateTime Ahora = new(2026, 10, 21, 10, 0, 0);

    private static AppDbContext ConCita(DateTime inicio, string estado = "Aceptada", int id = 70)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        Semillas.TutorAprobado(context, 2, 3);
        context.Usuarios.Add(new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana Ruiz", Rol = "Estudiante", AuthProvider = "Local" });
        context.Citas.Add(new Cita
        {
            Id = id,
            EstudianteId = 1,
            TutorId = 2,
            MateriaId = 3,
            Estado = estado,
            FechaHoraInicio = inicio,
            LinkReunion = "https://meet.example.com/abc",
            Tema = "Límites laterales"
        });
        context.SaveChanges();
        return context;
    }

    private static (RecordatoriosCitas servicio, Mock<INotificacionesCorreo> notificaciones) Servicio(AppDbContext context, DateTime ahora)
    {
        var notificaciones = new Mock<INotificacionesCorreo>();
        return (new RecordatoriosCitas(context, notificaciones.Object, new HoraFija(ahora)), notificaciones);
    }

    [Fact]
    public async Task UnDiaAntes_EnviaElRecordatorioDelDiaUnaSolaVez()
    {
        var context = ConCita(Ahora.AddHours(20));
        var (servicio, notificaciones) = Servicio(context, Ahora);

        Assert.Equal(1, await servicio.EnviarPendientesAsync());
        Assert.Equal(0, await servicio.EnviarPendientesAsync());

        notificaciones.Verify(n => n.RecordatorioCita(
            It.Is<DatosCitaCorreo>(d => d.CitaId == 70 && d.EstudianteEmail == "ana@example.com" && d.Tema == "Límites laterales"),
            "https://meet.example.com/abc",
            false), Times.Once);
        Assert.Equal(Ahora, (await context.Citas.FindAsync(70))!.RecordatorioDiaEnviado);
    }

    [Fact]
    public async Task UnaHoraAntes_EnviaElRecordatorioDeLaHora()
    {
        var context = ConCita(Ahora.AddHours(20));
        await Servicio(context, Ahora).servicio.EnviarPendientesAsync();

        // 19 h y 15 min después: la cita empieza en 45 minutos.
        var (servicio, notificaciones) = Servicio(context, Ahora.AddHours(19).AddMinutes(15));
        Assert.Equal(1, await servicio.EnviarPendientesAsync());
        Assert.Equal(0, await servicio.EnviarPendientesAsync());

        notificaciones.Verify(n => n.RecordatorioCita(It.IsAny<DatosCitaCorreo>(), It.IsAny<string?>(), true), Times.Once);
        notificaciones.Verify(n => n.RecordatorioCita(It.IsAny<DatosCitaCorreo>(), It.IsAny<string?>(), false), Times.Never);
    }

    [Fact]
    public async Task ConPocaAnticipacion_NoMandaElDelDiaPeroSiElDeLaHora()
    {
        var context = ConCita(Ahora.AddHours(2));

        var (antes, notificacionesAntes) = Servicio(context, Ahora);
        Assert.Equal(0, await antes.EnviarPendientesAsync());
        notificacionesAntes.VerifyNoOtherCalls();

        var (despues, notificacionesDespues) = Servicio(context, Ahora.AddMinutes(70));
        Assert.Equal(1, await despues.EnviarPendientesAsync());
        notificacionesDespues.Verify(n => n.RecordatorioCita(It.IsAny<DatosCitaCorreo>(), It.IsAny<string?>(), true), Times.Once);
    }

    [Theory]
    [InlineData("Pendiente", 20)]
    [InlineData("Cancelada", 20)]
    [InlineData("Rechazada", 20)]
    [InlineData("Aceptada", 30)]
    [InlineData("Aceptada", -1)]
    public async Task NoEnvia_SiLaCitaNoEstaAceptadaOEstaFueraDeLaVentana(string estado, int horas)
    {
        var context = ConCita(Ahora.AddHours(horas), estado);
        var (servicio, notificaciones) = Servicio(context, Ahora);

        Assert.Equal(0, await servicio.EnviarPendientesAsync());
        notificaciones.VerifyNoOtherCalls();
    }

    // Correos

    private static (NotificacionesCorreo servicio, List<CorreoPendiente> enviados) Correos()
    {
        var enviados = new List<CorreoPendiente>();
        var cola = new Mock<IColaCorreos>();
        cola.Setup(c => c.Encolar(It.IsAny<CorreoPendiente>())).Callback<CorreoPendiente>(enviados.Add);
        return (new NotificacionesCorreo(cola.Object, new ConfigurationBuilder().Build(), NullLogger<NotificacionesCorreo>.Instance, new HoraFija(Ahora)), enviados);
    }

    // Jueves 22 de octubre de 2026, 16:00 (mañana respecto de Ahora).
    private static readonly DatosCitaCorreo Datos = new(
        "Cálculo", new DateTime(2026, 10, 22, 16, 0, 0), "Ana Ruiz", "ana@example.com",
        "Mateo Herrera", "mateo@example.com", "Límites laterales", 70);

    private static string Ics(CorreoPendiente correo)
    {
        var adjunto = Assert.Single(correo.Adjuntos!);
        Assert.Equal("tutoria.ics", adjunto.Nombre);
        Assert.Equal("text/calendar", adjunto.TipoContenido);
        return Encoding.UTF8.GetString(adjunto.Contenido);
    }

    [Fact]
    public void Recordatorio_LlegaAlEstudianteYAlTutorConSuCalendario()
    {
        var (servicio, enviados) = Correos();

        servicio.RecordatorioCita(Datos, "https://meet.example.com/abc", faltaUnaHora: false);

        Assert.Equal(["ana@example.com", "mateo@example.com"], enviados.Select(c => c.Para));
        Assert.All(enviados, correo =>
        {
            Assert.Equal("Recordatorio: tutoría de Cálculo mañana a las 16:00 h", correo.Asunto);
            Assert.Contains("Unirse a la videollamada", correo.Html);
            Assert.Contains("UID:cita-70@tutoriasu", Ics(correo));
        });
        // Cada quien ve a la otra persona.
        Assert.Contains("Mateo Herrera", enviados[0].Html);
        Assert.Contains("SUMMARY:Tutoría de Cálculo con Mateo Herrera", Ics(enviados[0]));
        Assert.Contains("SUMMARY:Tutoría de Cálculo con Ana Ruiz", Ics(enviados[1]));
        Assert.Contains("Límites laterales", enviados[1].Html);
    }

    [Fact]
    public void RecordatorioDeUnaHora_SinEnlace_MandaAMisTutorias()
    {
        var (servicio, enviados) = Correos();

        servicio.RecordatorioCita(Datos, null, faltaUnaHora: true);

        Assert.All(enviados, correo =>
        {
            Assert.Equal("Tu tutoría de Cálculo empieza en una hora", correo.Asunto);
            Assert.Contains("Ver mis tutorías", correo.Html);
            Assert.DoesNotContain("Unirse a la videollamada", correo.Html);
        });
    }

    [Fact]
    public void Confirmacion_AdjuntaElCalendarioEnHoraUtc()
    {
        var (servicio, enviados) = Correos();

        servicio.CitaAceptada(Datos, "https://meet.example.com/abc", null);

        var ics = Ics(Assert.Single(enviados));
        // 16:00 en Ciudad de México (UTC-6, sin horario de verano) = 22:00 UTC; sesión de 1 h.
        Assert.Contains("DTSTART:20261022T220000Z", ics);
        Assert.Contains("DTEND:20261022T230000Z", ics);
        Assert.Contains("URL:https://meet.example.com/abc", ics);
        Assert.Contains("TRIGGER:-PT15M", ics);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void SinIdDeCita_NoAdjuntaCalendario()
    {
        var (servicio, enviados) = Correos();

        servicio.CitaAceptada(Datos with { CitaId = 0 }, null, null);

        Assert.Null(Assert.Single(enviados).Adjuntos);
    }

    [Fact]
    public void Ics_EscapaElTextoYPliegaLasLineasLargas()
    {
        Assert.Equal("a\\, b\\; c\\\\ d\\ne", CalendarioIcs.Escapar("a, b; c\\ d\ne"));

        var larga = "DESCRIPTION:" + string.Concat(Enumerable.Repeat("áéí ", 40));
        var plegada = CalendarioIcs.Plegar(larga);
        var lineas = plegada.Split("\r\n");

        Assert.True(lineas.Length > 1);
        Assert.All(lineas, linea => Assert.True(Encoding.UTF8.GetByteCount(linea) <= 75));
        Assert.All(lineas.Skip(1), linea => Assert.StartsWith(" ", linea));
        // Desplegar (quitar CRLF + espacio) regresa el texto original.
        Assert.Equal(larga, plegada.Replace("\r\n ", ""));
    }
}
