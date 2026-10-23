using Microsoft.AspNetCore.Mvc;
using backend.Controllers;
using backend.Data;
using backend.DTOs.Requests;
using backend.DTOs.Responses;
using backend.Models;
using static backend.Tests.Pruebas;

namespace backend.Tests;

// Intereses del estudiante y catálogo de materias.
public class EstudiantesMateriasTests
{
    private static AppDbContext ConCatalogo()
    {
        var context = Contexto();
        var ciencias = new Categoria { Id = 1, Nombre = "Ciencias" };
        context.Categorias.Add(ciencias);
        context.Materias.AddRange(
            new Materia { Id = 2, Nombre = "Física", CategoriaId = 1, Categoria = ciencias },
            new Materia { Id = 1, Nombre = "Cálculo", CategoriaId = 1, Categoria = ciencias });
        context.Usuarios.AddRange(
            new Usuario { Id = 10, Email = "ana@example.com", Nombre = "Ana", Rol = "Estudiante", AuthProvider = "Local" },
            new Usuario { Id = 11, Email = "mateo@example.com", Nombre = "Mateo", Rol = "Tutor", AuthProvider = "Local" });
        context.SaveChanges();
        return context;
    }

    private static EstudiantesController Estudiantes(AppDbContext context, int? id, string rol = "Estudiante") =>
        new(context) { ControllerContext = Como(id, rol) };

    [Fact]
    public async Task GuardarIntereses_ReemplazaLasMateriasYCompletaElOnboarding()
    {
        var context = ConCatalogo();

        var resultado = await Estudiantes(context, 10).GuardarIntereses(
            new EstudianteInteresesRequestDto { MateriaIds = [1, 2, 2] }, CancellationToken.None);

        Assert.Equal(200, Codigo(resultado));
        var ana = context.Usuarios.Find(10)!;
        Assert.True(ana.OnboardingCompleto);
        Assert.Equal([1, 2], ana.Materia.Select(m => m.Id).Order());

        // Volver a guardar reemplaza (no acumula).
        await Estudiantes(context, 10).GuardarIntereses(new EstudianteInteresesRequestDto { MateriaIds = [2] }, CancellationToken.None);
        Assert.Equal([2], context.Usuarios.Find(10)!.Materia.Select(m => m.Id));
    }

    [Theory]
    [InlineData(null, new[] { 1 }, 401)]
    [InlineData(99, new[] { 1 }, 404)]
    [InlineData(11, new[] { 1 }, 400)]   // no es estudiante
    [InlineData(10, new int[0], 400)]    // sin materias
    [InlineData(10, new[] { 1, 77 }, 400)] // una materia no existe
    public async Task GuardarIntereses_RechazaSolicitudesInvalidas(int? usuarioId, int[] materias, int codigo)
    {
        var context = ConCatalogo();

        var resultado = await Estudiantes(context, usuarioId).GuardarIntereses(
            new EstudianteInteresesRequestDto { MateriaIds = [.. materias] }, CancellationToken.None);

        Assert.Equal(codigo, Codigo(resultado));
        Assert.False(context.Usuarios.Find(10)!.OnboardingCompleto);
    }

    [Fact]
    public async Task OmitirIntereses_CompletaElOnboardingSinMaterias()
    {
        var context = ConCatalogo();

        var resultado = await Estudiantes(context, 10).OmitirIntereses(CancellationToken.None);

        Assert.Equal(204, Codigo(resultado));
        var ana = context.Usuarios.Find(10)!;
        Assert.True(ana.OnboardingCompleto);
        Assert.Empty(ana.Materia);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(99, 404)]
    [InlineData(11, 400)] // no es estudiante
    public async Task OmitirIntereses_RechazaSolicitudesInvalidas(int? usuarioId, int codigo)
    {
        var context = ConCatalogo();

        var resultado = await Estudiantes(context, usuarioId).OmitirIntereses(CancellationToken.None);

        Assert.Equal(codigo, Codigo(resultado));
        Assert.False(context.Usuarios.Find(11)!.OnboardingCompleto);
    }

    [Fact]
    public async Task ObtenerIntereses_DevuelveLasMateriasConSuCategoria()
    {
        var context = ConCatalogo();
        await Estudiantes(context, 10).GuardarIntereses(new EstudianteInteresesRequestDto { MateriaIds = [1] }, CancellationToken.None);

        var resultado = Assert.IsType<OkObjectResult>(await Estudiantes(context, 10).ObtenerIntereses(CancellationToken.None));

        var interes = Assert.Single(Assert.IsAssignableFrom<IEnumerable<MateriaResponseDto>>(resultado.Value));
        Assert.Equal(("Cálculo", "Ciencias"), (interes.Nombre, interes.Categoria));
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(99, 404)]
    public async Task ObtenerIntereses_SinUsuarioValido(int? usuarioId, int codigo)
    {
        var resultado = await Estudiantes(ConCatalogo(), usuarioId).ObtenerIntereses(CancellationToken.None);

        Assert.Equal(codigo, Codigo(resultado));
    }

    [Fact]
    public async Task Materias_SeListanPorIdConSuCategoria()
    {
        var resultado = await new MateriasController(ConCatalogo()).ObtenerMaterias(CancellationToken.None);

        var materias = Assert.IsAssignableFrom<IReadOnlyList<MateriaResponseDto>>(Assert.IsType<OkObjectResult>(resultado.Result).Value);
        Assert.Equal(["Cálculo", "Física"], materias.Select(m => m.Nombre));
        Assert.All(materias, m => Assert.Equal("Ciencias", m.Categoria));
    }
}
