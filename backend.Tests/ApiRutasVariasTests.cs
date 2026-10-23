using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.DTOs.Resenas;
using backend.Enums;
using backend.Middleware;
using backend.Models;
using backend.Services;
using backend.Services.Interfaces;
using static backend.Tests.Pruebas;

namespace backend.Tests;

// Rutas y casos de error que no cubren las pruebas de flujo.
public class ApiRutasVariasTests
{
    private static readonly DateTime Ahora = new(2026, 10, 22, 10, 0, 0);

    private static AppDbContext ConCitas()
    {
        var context = Contexto();
        context.Materias.Add(new Materia { Id = 1, Nombre = "Cálculo" });
        context.Usuarios.AddRange(
            new Usuario { Id = 1, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 7, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local" });
        Cita Cita(int id, string estado, int hora) => new()
        {
            Id = id, EstudianteId = 1, TutorId = 7, MateriaId = 1, Estado = estado,
            FechaHoraInicio = new DateTime(2026, 10, 23, hora, 0, 0)
        };
        context.Citas.AddRange(
            Cita(1, "Aceptada", 16), Cita(2, "Pendiente", 9), Cita(3, "Cancelada", 11),
            Cita(4, "Rechazada", 12), Cita(5, "Completada", 8));
        context.SaveChanges();
        return context;
    }

    private static CitasController Citas(AppDbContext context, int? id, string rol) =>
        new(context, Mock.Of<INotificacionesCorreo>(), NullLogger<CitasController>.Instance, new HoraFija(Ahora))
        {
            ControllerContext = Como(id, rol)
        };

    // Citas

    [Fact]
    public async Task Proximas_DelTutor_MuestranAlEstudiante()
    {
        var resultado = Assert.IsType<OkObjectResult>(await Citas(ConCitas(), 7, "Tutor").ObtenerProximasCitas());

        var proximas = Assert.IsAssignableFrom<IEnumerable<ProximaCitaResponseDto>>(resultado.Value).ToList();
        Assert.Equal([2, 1], proximas.Select(c => c.Id)); // por hora
        Assert.All(proximas, c => Assert.Equal("Ana", c.RolOpuestoNombre));
    }

    [Fact]
    public async Task Proximas_ConOtroRol_SeNiegan()
    {
        Assert.Equal(403, Codigo(await Citas(ConCitas(), 1, "Administrador").ObtenerProximasCitas()));
    }

    [Fact]
    public async Task HorasOcupadas_IgnoranCanceladasYRechazadas()
    {
        var resultado = await Citas(ConCitas(), 1, "Estudiante").ObtenerHorasOcupadas(7, new DateOnly(2026, 10, 23));

        var horas = Assert.IsAssignableFrom<IEnumerable<string>>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(["08:00", "09:00", "16:00"], horas);
    }

    // Administración

    [Fact]
    public async Task Aprobar_PasaLasMateriasDeLaSolicitudAlTutorSinDuplicar()
    {
        var context = Contexto();
        var calculo = new Materia { Id = 1, Nombre = "Cálculo" };
        var fisica = new Materia { Id = 2, Nombre = "Física" };
        context.Materias.AddRange(calculo, fisica);
        var aspirante = new Usuario
        {
            Id = 30, Email = "lu@example.com", Nombre = "Lucía", Rol = "Estudiante", AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
        };
        aspirante.MateriaNavigation.Add(calculo);
        var solicitud = new TutorSolicitudesCredenciale { Id = 5, UrlCredencial = "x", FechaSolicitud = Ahora, Usuario = aspirante };
        solicitud.SolicitudMateria.Add(new SolicitudMateria { MateriaId = 1, Materia = calculo });
        solicitud.SolicitudMateria.Add(new SolicitudMateria { MateriaId = 2, Materia = fisica });
        aspirante.TutorSolicitudesCredenciales.Add(solicitud);
        context.Usuarios.Add(aspirante);
        context.SaveChanges();

        var resultado = await new AdminController(context, Mock.Of<INotificacionesCorreo>()).AprobarTutor(30, CancellationToken.None);

        Assert.Equal(204, Codigo(resultado));
        var tutor = context.Usuarios.Find(30)!;
        Assert.Equal(("Tutor", (int)EstadoAprobacion.Aprobado), (tutor.Rol, tutor.EstadoAprobacionId));
        Assert.Equal([1, 2], tutor.MateriaNavigation.Select(m => m.Id).Order());
    }

    [Fact]
    public async Task Aprobar_SinSolicitudPendiente_Responde404()
    {
        var resultado = await new AdminController(Contexto(), Mock.Of<INotificacionesCorreo>()).AprobarTutor(99, CancellationToken.None);

        Assert.Equal(404, Codigo(resultado));
    }

    // Reseñas

    [Theory]
    [InlineData(null, 1, 401)]
    [InlineData(1, 99, 404)]
    public async Task Resena_SinUsuarioOSinCita(int? usuarioId, int citaId, int codigo)
    {
        var controller = new ResenasController(ConCitas(), new HoraFija(Ahora)) { ControllerContext = Como(usuarioId) };

        var resultado = await controller.CrearResena(new CrearResenaRequestDto { CitaId = citaId, Puntuacion = 5 });

        Assert.Equal(codigo, Codigo(resultado));
    }

    // Archivos

    [Fact]
    public async Task SubirComprobante_ConFallaDelAlmacenamiento_Responde500SinDetalles()
    {
        var blob = new Mock<IBlobService>();
        blob.Setup(b => b.SubirComprobanteAsync(It.IsAny<IFormFile>())).ThrowsAsync(new InvalidOperationException("cadena de conexión rota"));
        var controller = new FilesController(blob.Object, NullLogger<FilesController>.Instance);
        var archivo = new FormFile(new MemoryStream("%PDF-1.4"u8.ToArray()), 0, 8, "file", "kardex.pdf");

        var resultado = Assert.IsType<ObjectResult>((await controller.UploadFile(new FileUploadRequestDto { File = archivo })).Result);

        Assert.Equal(500, resultado.StatusCode);
        Assert.DoesNotContain("cadena", Assert.IsType<ProblemDetails>(resultado.Value).Title);
    }

    private static FormFile Archivo(string nombre, byte[] contenido) =>
        new(new MemoryStream(contenido), 0, contenido.Length, "file", nombre);

    [Fact]
    public void Validador_RechazaArchivosVaciosYAceptaWebpReal()
    {
        Assert.Throws<ArgumentException>(() => ValidadorArchivos.Validar(Archivo("vacio.pdf", [])));

        var webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        Assert.Equal((".webp", "image/webp"), ValidadorArchivos.Validar(Archivo("foto.webp", webp)));

        // Extensión .webp con otro contenido.
        var error = Assert.Throws<ArgumentException>(() => ValidadorArchivos.Validar(Archivo("foto.webp", "%PDF-1.4 abc"u8.ToArray())));
        Assert.Contains("no corresponde", error.Message);
    }

    // Títulos de error en español

    [Theory]
    [InlineData(404, "Not Found", "El recurso solicitado no existe.")]
    [InlineData(503, null, "Ocurrió un error inesperado en el servidor.")]   // 5xx sin título propio
    [InlineData(418, "I'm a teapot", "La solicitud no es válida.")]          // 4xx sin título propio
    [InlineData(409, "Ese horario ya está reservado.", "Ese horario ya está reservado.")] // título propio: no se toca
    public void Traducir_DaTitulosEnEspanolSinPisarLosPropios(int estado, string? titulo, string esperado)
    {
        var problema = new ProblemDetails { Status = estado, Title = titulo };

        ProblemDetailsEspanol.Traducir(problema);

        Assert.Equal(esperado, problema.Title);
    }

    [Fact]
    public void Traducir_ErroresDeValidacion()
    {
        var problema = new ValidationProblemDetails { Status = 400, Title = "One or more validation errors occurred." };

        ProblemDetailsEspanol.Traducir(problema);

        Assert.Equal("Revisa los datos enviados.", problema.Title);
    }
}
