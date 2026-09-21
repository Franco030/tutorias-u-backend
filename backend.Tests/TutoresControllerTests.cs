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
            // a) Insertar un usuario de prueba en estado "Ninguno"
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

            // b) Insertar una materia de prueba
            var materia = new Materia { Nombre = "Física Avanzada", CategoriaId = 1 };
            _dbContext.Materias.Add(materia);
            await _dbContext.SaveChangesAsync();

            // c) Simular (Mockear) que el usuario está logueado inyectándole los Claims
            var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                    new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString())
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = userClaims }
            };

            // d) Crear el objeto JSON (DTO) que mandaría el Frontend
            var request = new AplicarTutorRequestDto
            {
                UrlCredencial = "https://miscredenciales.com/mi-titulo.pdf",
                MateriaIds = new List<int> { materia.Id }
            };

            // Act (Ejecución)
            var result = await _controller.Aplicar(request);

            // Assert (Verificación)

            // 1. Verificar que la respuesta HTTP sea 200 OK
            Assert.IsType<OkObjectResult>(result);

            // 2. Verificar que en la base de datos el estado de este usuario cambió a "Pendiente"
            var usuarioActualizado = await _dbContext.Usuarios.FindAsync(usuario.Id);
            Assert.Equal((int)EstadoAprobacion.Pendiente, usuarioActualizado.EstadoAprobacionId);

            // 3. Verificar que se creó el registro de la solicitud y sus materias en la BD
            var solicitud = await _dbContext.TutorSolicitudesCredenciales
                                            .Include(s => s.SolicitudMateria)
                                            .FirstOrDefaultAsync(s => s.UsuarioId == usuario.Id);

            Assert.NotNull(solicitud);
            Assert.Equal(request.UrlCredencial, solicitud.UrlCredencial);
            Assert.Single(solicitud.SolicitudMateria); // Validar que se guardó exactamente 1 materia
        }
    }
}
