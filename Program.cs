using System.Text;
using backend.Data;
using backend.Middleware;
using backend.Services;
using backend.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
// Correos de citas y postulaciones: se encolan y se envían en segundo plano.
builder.Services.AddSingleton<ColaCorreos>();
builder.Services.AddSingleton<IColaCorreos>(sp => sp.GetRequiredService<ColaCorreos>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ColaCorreos>());
builder.Services.AddScoped<INotificacionesCorreo, NotificacionesCorreo>();
// Hora local de la universidad (configurable con "ZonaHoraria").
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IHoraPlataforma, HoraPlataforma>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddAuthorization();

var jwtKey = builder.Configuration["Jwt:Key"];
if (!string.IsNullOrEmpty(jwtKey))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
}

builder.Services.AddCors(options =>
{
    /*
    * Aqui va el CORS, las primeras dos son de desarrollo, en caso de que el frontend traiga otra URL,
    * modifiquen las de localhost para que coincida el puerto (a veces pasa que se levanta en puertos distintos)
    */
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173", // Local Vite dev
            "http://localhost:3000",
            "https://tutorias-u-frontend.vercel.app" // El que esta en produccion, este si no lo cambien
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
// Errores en formato ProblemDetails con títulos en español.
builder.Services.AddProblemDetailsEspanol();

var app = builder.Build();

// Excepciones no controladas -> 500 sin detalles internos; respuestas de error
// sin cuerpo (401/403 del JWT, Forbid(), 404 de ruta) -> ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "TutoriasU esta funcionando correctamente");

app.Run();
