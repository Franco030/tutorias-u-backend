namespace backend.DTOs.Responses;

public class MateriaResponseDto
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Categoria { get; set; } = string.Empty;
}