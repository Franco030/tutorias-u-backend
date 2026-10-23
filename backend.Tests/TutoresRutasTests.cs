using Microsoft.AspNetCore.Mvc;
using Moq;
using backend.Controllers;
using backend.Data;
using backend.DTOs;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Enums;
using backend.Models;
using backend.Services.Interfaces;
using static backend.Tests.Pruebas;

namespace backend.Tests;

// Rutas del controlador de tutores que no cubren las pruebas de flujo.
public class TutoresRutasTests
{
    private static readonly DateTime Ahora = new(2026, 10, 21, 10, 0, 0);

    private static AppDbContext ConDatos()
    {
        var context = Contexto();
        var calculo = new Materia { Id = 1, Nombre = "Cálculo" };
        var fisica = new Materia { Id = 2, Nombre = "Física" };
        context.Materias.AddRange(calculo, fisica);
        var tutor = new Usuario
        {
            Id = 2, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local",
            EstadoAprobacionId = (int)EstadoAprobacion.Aprobado
        };
        tutor.MateriaNavigation.Add(calculo);
        context.Usuarios.AddRange(
            tutor,
            new Usuario { Id = 3, Email = "lu@example.com", Nombre = "Lucía", Rol = "Tutor", AuthProvider = "Local", EstadoAprobacionId = (int)EstadoAprobacion.Pendiente },
            new Usuario { Id = 10, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" });
        context.SaveChanges();
        return context;
    }

    private static TutoresController Tutores(AppDbContext context, int? id, string rol = "Tutor") =>
        new(context) { ControllerContext = Como(id, rol) };

    // Postulación

    [Fact]
    public async Task Aplicar_ConComprobanteAjeno_Responde400()
    {
        var blob = new Mock<IBlobService>();
        blob.Setup(b => b.EsComprobantePropio(It.IsAny<string>())).Returns(false);

        var resultado = await Tutores(ConDatos(), 10, "Estudiante").Aplicar(
            new AplicarTutorRequestDto { UrlCredencial = "https://otro.example.com/x.pdf", MateriaIds = [1] }, blob.Object);

        Assert.Equal(400, Codigo(resultado));
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(99, 404)]
    [InlineData(3, 400)] // ya tiene una solicitud pendiente
    [InlineData(2, 400)] // ya es tutor aprobado
    public async Task Aplicar_RechazaUsuariosQueNoPuedenPostularse(int? usuarioId, int codigo)
    {
        var blob = new Mock<IBlobService>();
        blob.Setup(b => b.EsComprobantePropio(It.IsAny<string>())).Returns(true);

        var resultado = await Tutores(ConDatos(), usuarioId, "Estudiante").Aplicar(
            new AplicarTutorRequestDto { UrlCredencial = "https://propio/x.pdf", MateriaIds = [1] }, blob.Object);

        Assert.Equal(codigo, Codigo(resultado));
    }

    // Catálogo y recomendaciones

    [Fact]
    public async Task Catalogo_SoloTutoresAprobadosConSusMaterias()
    {
        var resultado = Assert.IsType<OkObjectResult>(await Tutores(ConDatos(), null).GetAllTutors());

        var tutor = Assert.Single(Assert.IsAssignableFrom<IEnumerable<TutorPerfilResponseDto>>(resultado.Value));
        Assert.Equal(("Mateo", "Cálculo"), (tutor.Nombre, Assert.Single(tutor.Materias)));
    }

    [Theory]
    [InlineData(null, "Estudiante", 401)]
    [InlineData(99, "Estudiante", 404)]
    [InlineData(2, "Tutor", 400)]
    public async Task Recomendados_RechazaQuienNoEsEstudiante(int? usuarioId, string rol, int codigo)
    {
        var resultado = await Tutores(ConDatos(), usuarioId, rol).GetRecommendedTutors(CancellationToken.None);

        Assert.Equal(codigo, Codigo(resultado));
    }

    // Materias que imparte

    [Fact]
    public async Task MisMaterias_ReemplazaLasMateriasDelTutor()
    {
        var context = ConDatos();

        var resultado = await Tutores(context, 2).ActualizarMisMaterias(new ActualizarMateriasTutorDto { MateriaIds = [2] });

        Assert.Equal(200, Codigo(resultado));
        Assert.Equal([2], context.Usuarios.Find(2)!.MateriaNavigation.Select(m => m.Id));
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(99, 404)]
    [InlineData(3, 400)] // todavía no aprobado
    public async Task MisMaterias_RechazaUsuariosNoValidos(int? usuarioId, int codigo)
    {
        var resultado = await Tutores(ConDatos(), usuarioId).ActualizarMisMaterias(new ActualizarMateriasTutorDto { MateriaIds = [2] });

        Assert.Equal(codigo, Codigo(resultado));
    }

    // Disponibilidad

    public static TheoryData<int?, byte, int, int, int> DisponibilidadInvalida => new()
    {
        { null, 1, 9, 10, 401 },
        { 10, 1, 9, 10, 404 },  // no es tutor
        { 2, 7, 9, 10, 400 },   // día fuera de rango
        { 2, 1, 10, 10, 400 },  // fin no es posterior al inicio
    };

    [Theory]
    [MemberData(nameof(DisponibilidadInvalida))]
    public async Task Disponibilidad_RechazaBloquesInvalidos(int? usuarioId, byte dia, int desde, int hasta, int codigo)
    {
        var resultado = await Tutores(ConDatos(), usuarioId).GuardarDisponibilidad(
        [
            new DisponibilidadRequestDto { DiaSemana = dia, HoraInicio = new TimeOnly(desde, 0), HoraFin = new TimeOnly(hasta, 0) }
        ]);

        Assert.Equal(codigo, Codigo(resultado));
    }

    [Fact]
    public async Task VerDisponibilidad_DeUnTutorQueNoExiste_Responde404()
    {
        Assert.Equal(404, Codigo(await Tutores(ConDatos(), null).GetDisponibilidad(99)));
    }

    [Fact]
    public async Task VerDisponibilidad_OrdenadaPorDiaYHora()
    {
        var context = ConDatos();
        context.TutorDisponibilidads.AddRange(
            new TutorDisponibilidad { TutorId = 2, DiaSemana = 3, HoraInicio = new TimeOnly(9, 0), HoraFin = new TimeOnly(10, 0) },
            new TutorDisponibilidad { TutorId = 2, DiaSemana = 1, HoraInicio = new TimeOnly(16, 0), HoraFin = new TimeOnly(17, 0) },
            new TutorDisponibilidad { TutorId = 2, DiaSemana = 1, HoraInicio = new TimeOnly(9, 0), HoraFin = new TimeOnly(10, 0) });
        context.SaveChanges();

        var resultado = Assert.IsType<OkObjectResult>(await Tutores(context, null).GetDisponibilidad(2));

        var bloques = Assert.IsAssignableFrom<IEnumerable<DisponibilidadResponseDto>>(resultado.Value).ToList();
        Assert.Equal([(1, 9), (1, 16), (3, 9)], bloques.Select(b => ((int)b.DiaSemana, b.HoraInicio.Hour)));
    }

    // Preferencias y días bloqueados sin un tutor en el token

    [Fact]
    public async Task PreferenciasYBloqueos_SinUsuarioEnElToken_Responden401()
    {
        var controller = Tutores(ConDatos(), null);

        Assert.Equal(401, Codigo(await controller.ObtenerPreferencias(CancellationToken.None)));
        Assert.Equal(401, Codigo(await controller.ObtenerMisBloqueos(new HoraFija(Ahora), CancellationToken.None)));
    }

    [Fact]
    public async Task MisBloqueos_DevuelveSoloLosDeHoyEnAdelante()
    {
        var context = ConDatos();
        context.Bloqueos.AddRange(
            new Bloqueo { TutorId = 2, Fecha = new DateOnly(2026, 10, 20), Motivo = "Ayer" },
            new Bloqueo { TutorId = 2, Fecha = new DateOnly(2026, 10, 24), Motivo = "Examen" });
        context.SaveChanges();

        var resultado = await Tutores(context, 2).ObtenerMisBloqueos(new HoraFija(Ahora), CancellationToken.None);

        var bloqueos = Assert.IsType<BloqueosResponseDto>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(["Examen"], bloqueos.Bloqueos.Select(b => b.Motivo));
    }

    [Fact]
    public async Task GuardarBloqueos_ConMotivoDeMasDe200Caracteres_Responde400()
    {
        var resultado = await Tutores(ConDatos(), 2).GuardarBloqueos(
            [new BloqueoDto { Fecha = new DateOnly(2026, 10, 24), Motivo = new string('x', 201) }],
            new HoraFija(Ahora),
            CancellationToken.None);

        Assert.Equal(400, Codigo(resultado));
    }
}
