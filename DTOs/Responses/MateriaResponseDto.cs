namespace backend.DTOs.Responses;

public class MateriaResponseDto
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public int CategoriaId { get; set; }

    public string Categoria { get; set; } = string.Empty;
}