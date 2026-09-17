using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using backend.Models;

namespace backend.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<EstadosAprobacion> EstadosAprobacions { get; set; }

    public virtual DbSet<Materia> Materias { get; set; }

    public virtual DbSet<Usuario> Usuarios { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EstadosAprobacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__EstadosA__3214EC07A376A07F");

            entity.ToTable("EstadosAprobacion");

            entity.Property(e => e.Estado).HasMaxLength(50);
        });

        modelBuilder.Entity<Materia>(entity =>
        {
            entity.Property(e => e.Categoria).HasMaxLength(100);
            entity.Property(e => e.Nombre).HasMaxLength(100);
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Usuarios__3214EC077101C9AC");

            entity.HasIndex(e => e.Email, "UQ__Usuarios__A9D105349B37B5BC").IsUnique();

            entity.Property(e => e.AuthProvider).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.EstadoAprobacionId).HasDefaultValue(1, "DF__Usuarios__EstadoAprobacionId");
            entity.Property(e => e.FechaRegistro).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.FotoUrl).HasMaxLength(500);
            entity.Property(e => e.Nombre).HasMaxLength(100);
            entity.Property(e => e.ProviderId).HasMaxLength(100);
            entity.Property(e => e.Rol)
                .HasMaxLength(50)
                .HasDefaultValue("Nuevo", "DF__Usuarios__Rol");

            entity.HasOne(d => d.EstadoAprobacion).WithMany(p => p.Usuarios)
                .HasForeignKey(d => d.EstadoAprobacionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuarios__EstadoAprobacionId");

            entity.HasMany(d => d.Materia).WithMany(p => p.Estudiantes)
                .UsingEntity<Dictionary<string, object>>(
                    "EstudianteInterese",
                    r => r.HasOne<Materia>().WithMany()
                        .HasForeignKey("MateriaId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_EstudianteIntereses_Materias"),
                    l => l.HasOne<Usuario>().WithMany()
                        .HasForeignKey("EstudianteId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_EstudianteIntereses_Usuarios"),
                    j =>
                    {
                        j.HasKey("EstudianteId", "MateriaId");
                        j.ToTable("EstudianteIntereses");
                    });
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
