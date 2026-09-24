using backend.Controllers;
using backend.Data;
using backend.DTOs.Requests;
using backend.Models;
using backend.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace backend.Tests
{
    public class TutoresControllerTests
    {
        private readonly AppDbContext _dbContext;
        private readonly TutoresController _controller;

        public TutoresControllerTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AppDbContext(options);

            _controller = new TutoresController(_dbContext);
        }

        [Fact]
        public async Task Aplicar_DebeRetornarOk_CuandoDatosSonValidos()
        {
            var usuario = new Usuario
            {
                Email = "tutor_candidato@ejemplo.com",
                Nombre = "Candidato",
                Rol = "Estudiante",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Ninguno
            };
            _dbContext.Usuarios.Add(usuario);
            await _dbContext.SaveChangesAsync();

            var materia = new Materia { Nombre = "Fisica Avanzada", CategoriaId = 1 };
            _dbContext.Materias.Add(materia);
            await _dbContext.SaveChangesAsync();

            var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                    new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString())
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = userClaims }
            };

            var request = new AplicarTutorRequestDto
            {
                UrlCredencial = "https://miscredenciales.com/mi-titulo.pdf",
                MateriaIds = new List<int> { materia.Id }
            };

            var result = await _controller.Aplicar(request);

            Assert.IsType<OkObjectResult>(result);

            var usuarioActualizado = await _dbContext.Usuarios.FindAsync(usuario.Id);
            Assert.Equal((int)EstadoAprobacion.Pendiente, usuarioActualizado.EstadoAprobacionId);

            var solicitud = await _dbContext.TutorSolicitudesCredenciales
                                            .Include(s => s.SolicitudMateria)
                                            .FirstOrDefaultAsync(s => s.UsuarioId == usuario.Id);

            Assert.NotNull(solicitud);
            Assert.Equal(request.UrlCredencial, solicitud.UrlCredencial);
            Assert.Single(solicitud.SolicitudMateria); 
        }

        [Fact]
        public async Task GetTutorProfile_ReturnsOk_WhenTutorIsApproved()
        {
            var tutor = new Usuario
            {
                Email = "tutor_aprobado@ejemplo.com",
                Nombre = "Profe Roberto",
                Rol = "Tutor",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Aprobado,
                FotoUrl = "http://foto.com/rob.png",
                CalificacionPromedio = 4.8m
            };

            var materia = new Materia { Nombre = "Fisica", CategoriaId = 1 };
            
            tutor.MateriaNavigation.Add(materia);
            _dbContext.Usuarios.Add(tutor);
            await _dbContext.SaveChangesAsync();

            var result = await _controller.GetTutorProfile(tutor.Id);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var responseDto = Assert.IsType<backend.DTOs.TutorPerfilResponseDto>(okResult.Value);

            Assert.Equal(tutor.Id, responseDto.Id);
            Assert.Equal("Profe Roberto", responseDto.Nombre);
            Assert.Equal("http://foto.com/rob.png", responseDto.FotoUrl);
            Assert.Equal(4.8m, responseDto.CalificacionPromedio);
            Assert.Single(responseDto.Materias);
            Assert.Contains("Fisica", responseDto.Materias);
        }

        [Fact]
        public async Task GetTutorProfile_ReturnsNotFound_WhenTutorNotApprovedOrNotFound()
        {
            var tutorPendiente = new Usuario
            {
                Email = "tutor_pendiente@ejemplo.com",
                Nombre = "Candidato a Profe",
                Rol = "Tutor",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Pendiente
            };

            _dbContext.Usuarios.Add(tutorPendiente);
            await _dbContext.SaveChangesAsync();

            var result = await _controller.GetTutorProfile(tutorPendiente.Id);
            Assert.IsType<NotFoundObjectResult>(result);

            var resultInexistente = await _controller.GetTutorProfile(999);
            Assert.IsType<NotFoundObjectResult>(resultInexistente);
        }

        [Fact]
        public async Task GetRecommendedTutors_ReturnsAtMostTenMatchingTutorsOrderedByRating()
        {
            var estudiante = new Usuario
            {
                Email = "estudiante_recomendaciones@ejemplo.com",
                Nombre = "Estudiante",
                Rol = "Estudiante",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Ninguno
            };
            var materiaCoincidente = new Materia { Nombre = "Matematicas", CategoriaId = 1 };
            var materiaNoCoincidente = new Materia { Nombre = "Historia", CategoriaId = 1 };
            estudiante.Materia.Add(materiaCoincidente);

            _dbContext.Usuarios.Add(estudiante);

            for (var i = 0; i < 12; i++)
            {
                var tutor = new Usuario
                {
                    Email = $"tutor_recomendado_{i}@ejemplo.com",
                    Nombre = $"Tutor {i}",
                    Rol = "Tutor",
                    AuthProvider = "local",
                    EstadoAprobacionId = (int)EstadoAprobacion.Aprobado,
                    CalificacionPromedio = i
                };
                tutor.MateriaNavigation.Add(materiaCoincidente);
                _dbContext.Usuarios.Add(tutor);
            }

            var tutorNoCoincidente = new Usuario
            {
                Email = "tutor_no_coincidente@ejemplo.com",
                Nombre = "Tutor no coincidente",
                Rol = "Tutor",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Aprobado,
                CalificacionPromedio = 5
            };
            tutorNoCoincidente.MateriaNavigation.Add(materiaNoCoincidente);
            _dbContext.Usuarios.Add(tutorNoCoincidente);

            var tutorPendiente = new Usuario
            {
                Email = "tutor_pendiente_recomendaciones@ejemplo.com",
                Nombre = "Tutor pendiente",
                Rol = "Tutor",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Pendiente,
                CalificacionPromedio = 5
            };
            tutorPendiente.MateriaNavigation.Add(materiaCoincidente);
            _dbContext.Usuarios.Add(tutorPendiente);

            await _dbContext.SaveChangesAsync();

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, estudiante.Id.ToString()) },
                        "mock"))
                }
            };

            var result = await _controller.GetRecommendedTutors(CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var recommendations = Assert.IsType<List<backend.DTOs.TutorPerfilResponseDto>>(okResult.Value);

            Assert.Equal(10, recommendations.Count);
            Assert.Equal(11, recommendations[0].CalificacionPromedio);
            Assert.Equal(2, recommendations[^1].CalificacionPromedio);
            Assert.DoesNotContain(recommendations, tutor => tutor.Nombre == "Tutor no coincidente");
            Assert.DoesNotContain(recommendations, tutor => tutor.Nombre == "Tutor pendiente");
        }

        [Fact]
        public async Task GetRecommendedTutors_ReturnsEmptyList_WhenStudentHasNoInterests()
        {
            var estudiante = new Usuario
            {
                Email = "estudiante_sin_intereses@ejemplo.com",
                Nombre = "Estudiante sin intereses",
                Rol = "Estudiante",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Ninguno
            };
            _dbContext.Usuarios.Add(estudiante);
            await _dbContext.SaveChangesAsync();

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, estudiante.Id.ToString()) },
                        "mock"))
                }
            };

            var result = await _controller.GetRecommendedTutors(CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var recommendations = Assert.IsType<List<backend.DTOs.TutorPerfilResponseDto>>(okResult.Value);
            Assert.Empty(recommendations);
        }
    }
}
