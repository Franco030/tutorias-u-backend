using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace backend.Tests;

// Verifica que un error salga como ProblemDetails con el estado esperado.
internal static class ProblemaAssert
{
    public static ProblemDetails EsProblema(IActionResult? result, int estado, string? titulo = null)
    {
        var objeto = Assert.IsType<ObjectResult>(result);
        var problema = Assert.IsAssignableFrom<ProblemDetails>(objeto.Value);
        Assert.Equal(estado, objeto.StatusCode);
        Assert.Equal(estado, problema.Status);
        if (titulo != null)
        {
            Assert.Equal(titulo, problema.Title);
        }
        return problema;
    }
}
