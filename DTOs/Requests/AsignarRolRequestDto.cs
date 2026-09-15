namespace backend.DTOs.Requests
{
    public class AsignarRolRequestDto
    {
        public required string NuevoRol { get; set; } // Estudiante, Tutor o Administrador
    }
}
