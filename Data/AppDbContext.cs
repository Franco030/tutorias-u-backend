using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using backend.Models;

namespace backend.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Categoria> Categorias { get; set; }

    public virtual DbSet<EstadosAprobacion> EstadosAprobacions { get; set; }

    public virtual DbSet<Materia> Materias { get; set; }

    public virtual DbSet<SolicitudMateria> SolicitudMaterias { get; set; }

    public virtual DbSet<TutorSolicitudesCredenciale> TutorSolicitudesCredenciales { get; set; }

    public virtual DbSet<TutorMateria> TutorMaterias { get; set; }

    public virtual DbSet<Usuario> Usuarios { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.HasIndex(e => e.Nombre, "UQ_Categorias_Nombre").IsUnique();

            entity.Property(e => e.Nombre).HasMaxLength(100);
        });

        modelBuilder.Entity<EstadosAprobacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__EstadosA__3214EC0797E5DD0A");

            entity.ToTable("EstadosAprobacion");

            entity.Property(e => e.Estado).HasMaxLength(50);
        });

        modelBuilder.Entity<Materia>(entity =>
        {
            entity.Property(e => e.Nombre).HasMaxLength(100);

            entity.HasOne(d => d.Categoria).WithMany(p => p.Materia)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Materias_Categorias");
        });

        modelBuilder.Entity<SolicitudMateria>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Solicitu__3214EC079F9AF643");

            entity.HasOne(d => d.Materia).WithMany(p => p.SolicitudMateria)
                .HasForeignKey(d => d.MateriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SolicitudMaterias_Materias");

            entity.HasOne(d => d.Solicitud).WithMany(p => p.SolicitudMateria)
                .HasForeignKey(d => d.SolicitudId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SolicitudMaterias_Solicitudes");
        });

        modelBuilder.Entity<TutorSolicitudesCredenciale>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__TutorSol__3214EC076F132999");

            entity.Property(e => e.FechaSolicitud).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.UrlCredencial).HasMaxLength(2048);

            entity.HasOne(d => d.Usuario).WithMany(p => p.TutorSolicitudesCredenciales)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TutorSolicitudes_Usuarios");
        });

        modelBuilder.Entity<TutorMateria>(entity =>
        {
            entity.HasKey(e => new { e.TutorId, e.MateriaId });

            entity.ToTable("TutorMaterias");

            entity.HasOne(d => d.Materia).WithMany(p => p.TutorMaterias)
                .HasForeignKey(d => d.MateriaId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TutorMaterias_Materias");

            entity.HasOne(d => d.Tutor).WithMany(p => p.TutorMaterias)
                .HasForeignKey(d => d.TutorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TutorMaterias_Usuarios");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Usuarios__3214EC07340F923A");

            entity.HasIndex(e => e.Email, "UQ__Usuarios__A9D105341BC20FE4").IsUnique();

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
