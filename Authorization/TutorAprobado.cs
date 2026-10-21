using System.Security.Claims;
using backend.Data;
using backend.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace backend.Authorization;

/*
 * El rol "Tutor" del token no basta: un aspirante elige ese rol al registrarse
 * y solo puede dar tutorías cuando el administrador lo aprueba. Esta política
 * lo comprueba contra la base de datos en cada petición, así que una
 * aprobación revocada también surte efecto sin esperar a que expire el token.
 */
public static class PoliticasAutorizacion
{
    public const string TutorAprobado = "TutorAprobado";
}

public sealed class TutorAprobadoRequirement : IAuthorizationRequirement;

public sealed class TutorAprobadoHandler(AppDbContext context) : AuthorizationHandler<TutorAprobadoRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext authorizationContext,
        TutorAprobadoRequirement requirement)
    {
        var userIdClaim = authorizationContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var usuarioId))
        {
            return;
        }

        var aprobado = await context.Usuarios.AnyAsync(usuario =>
            usuario.Id == usuarioId &&
            usuario.Rol == "Tutor" &&
            usuario.EstadoAprobacionId == (int)EstadoAprobacion.Aprobado);

        if (aprobado)
        {
            authorizationContext.Succeed(requirement);
        }
    }
}
