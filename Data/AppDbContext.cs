using Microsoft.EntityFrameworkCore;

namespace backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // Aqui van los DbSets
        // public DbSet<Modelo/Entidad/Clase/Tabla> NombreDeLaEntidad {get; set; }
    }
}
