using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;

namespace backend.Tests;

// Apoyos comunes de las pruebas de controladores.
public static class Pruebas
{
    public static AppDbContext Contexto() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    // Usuario autenticado con id y rol; con id null, un token sin identificador.
    public static ControllerContext Como(int? id, string rol = "Estudiante") => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                id == null
                    ? [new Claim(ClaimTypes.Role, rol)]
                    : [new Claim(ClaimTypes.NameIdentifier, id.ToString()!), new Claim(ClaimTypes.Role, rol)],
                "mock"))
        }
    };

    // Código HTTP de la respuesta de una acción.
    public static int Codigo(IActionResult resultado) => resultado switch
    {
        ObjectResult objeto => objeto.StatusCode ?? 200,
        StatusCodeResult estado => estado.StatusCode,
        ForbidResult => 403,
        _ => throw new InvalidOperationException($"Respuesta inesperada: {resultado.GetType().Name}")
    };

    public static int Codigo<T>(ActionResult<T> resultado) => resultado.Result == null ? 200 : Codigo(resultado.Result);
}
