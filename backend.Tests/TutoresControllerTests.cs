using backend.Controllers;
using backend.Data;
using backend.DTOs.Responses;
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
        public async Task GuardarDisponibilidad_CreatesAndUpdatesTutorsWeekdayBlock()
        {
            var tutor = await AddTutorAsync();
            SetAuthenticatedTutor(tutor.Id);

            var createResult = await _controller.GuardarDisponibilidad(new List<DisponibilidadRequestDto>
            {
                new()
                {
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(9, 0),
                    HoraFin = new TimeOnly(12, 0)
                },
                new()
                {
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(13, 0),
                    HoraFin = new TimeOnly(14, 0)
                }
            });
            var createdBlocks = Assert.IsType<List<DisponibilidadResponseDto>>(
                Assert.IsType<OkObjectResult>(createResult).Value);
            Assert.Equal(2, createdBlocks.Count);

            var updateResult = await _controller.GuardarDisponibilidad(new List<DisponibilidadRequestDto>
            {
                new()
                {
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(10, 0),
                    HoraFin = new TimeOnly(13, 0)
                }
            });
            var response = Assert.IsType<List<DisponibilidadResponseDto>>(
                Assert.IsType<OkObjectResult>(updateResult).Value);

            var block = Assert.Single(await _dbContext.TutorDisponibilidads.ToListAsync());
            Assert.Equal(tutor.Id, block.TutorId);
            Assert.Equal(new TimeOnly(10, 0), Assert.Single(response).HoraInicio);
            Assert.Equal(new TimeOnly(13, 0), block.HoraFin);
        }

        [Fact]
        public async Task GuardarDisponibilidad_ReturnsBadRequest_WhenBlockOverlapsExistingBlock()
        {
            var tutor = await AddTutorAsync();
            SetAuthenticatedTutor(tutor.Id);
            var existingBlock = new TutorDisponibilidad
            {
                TutorId = tutor.Id,
                DiaSemana = (byte)DayOfWeek.Friday,
                HoraInicio = new TimeOnly(9, 0),
                HoraFin = new TimeOnly(10, 0)
            };
            _dbContext.TutorDisponibilidads.Add(existingBlock);
            await _dbContext.SaveChangesAsync();

            var result = await _controller.GuardarDisponibilidad(new List<DisponibilidadRequestDto>
            {
                new()
                {
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(10, 30),
                    HoraFin = new TimeOnly(11, 30)
                },
                new()
                {
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(11, 0),
                    HoraFin = new TimeOnly(12, 0)
                }
            });

            Assert.IsType<BadRequestObjectResult>(result);
            var savedBlock = Assert.Single(await _dbContext.TutorDisponibilidads.ToListAsync());
            Assert.Equal(existingBlock.Id, savedBlock.Id);
            Assert.Equal((byte)DayOfWeek.Friday, savedBlock.DiaSemana);
        }

        [Fact]
        public async Task GetDisponibilidad_ReturnsTutorsAvailabilityInWeekdayOrder()
        {
            var tutor = await AddTutorAsync();
            _dbContext.TutorDisponibilidads.AddRange(
                new TutorDisponibilidad
                {
                    TutorId = tutor.Id,
                    DiaSemana = (byte)DayOfWeek.Tuesday,
                    HoraInicio = new TimeOnly(9, 0),
                    HoraFin = new TimeOnly(10, 0)
                },
                new TutorDisponibilidad
                {
                    TutorId = tutor.Id,
                    DiaSemana = (byte)DayOfWeek.Monday,
                    HoraInicio = new TimeOnly(13, 0),
                    HoraFin = new TimeOnly(14, 0)
                });
            await _dbContext.SaveChangesAsync();

            var result = Assert.IsType<OkObjectResult>(
                await _controller.GetDisponibilidad(tutor.Id));
            var availability = Assert.IsType<List<DisponibilidadResponseDto>>(result.Value);

            Assert.Equal(2, availability.Count);
            Assert.Equal((byte)DayOfWeek.Monday, availability[0].DiaSemana);
            Assert.Equal((byte)DayOfWeek.Tuesday, availability[1].DiaSemana);
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

        private async Task<Usuario> AddTutorAsync()
        {
            var tutor = new Usuario
            {
                Email = "disponibilidad_tutor@ejemplo.com",
                Nombre = "Tutor de disponibilidad",
                Rol = "Tutor",
                AuthProvider = "local",
                EstadoAprobacionId = (int)EstadoAprobacion.Aprobado
            };
            _dbContext.Usuarios.Add(tutor);
            await _dbContext.SaveChangesAsync();
            return tutor;
        }

        private void SetAuthenticatedTutor(int tutorId)
        {
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, tutorId.ToString()) },
                        "mock"))
                }
            };
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
