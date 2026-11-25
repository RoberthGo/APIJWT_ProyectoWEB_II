using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using APIkey.Data;
using APIkey.Options;
using APIkey.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Soporte nativo para ProblemDetails (RFC 7807)
builder.Services.AddProblemDetails();

// Configuración JWT tipada y validada una sola vez al arranque
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Validate();

builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.PostConfigure<JwtOptions>(opts => opts.Validate());

// Servicio Singleton de emisión de tokens JWT
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var signingKey = jwtOptions.CreateSigningKey();
var problemJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.Items["JwtAuthError"] = context.Exception is SecurityTokenExpiredException
                    ? "expired"
                    : "invalid";
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var rawUserId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                if (!int.TryParse(rawUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
                {
                    context.HttpContext.Items["JwtAuthError"] = "invalid";
                    context.Fail("El token JWT no contiene un identificador de usuario válido.");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices.GetRequiredService<ContextDB>();
                var user = await dbContext.Users
                    .AsNoTracking()
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

                if (user == null || user.Role == null)
                {
                    context.HttpContext.Items["JwtAuthError"] = "inactive";
                    context.Fail("La cuenta asociada al token JWT no existe o se encuentra inactiva.");
                    return;
                }

                // Ignorar cualquier claim de rol entrante y poblar ClaimsPrincipal exclusivamente desde la BD
                var existingClaims = (context.Principal?.Claims ?? Enumerable.Empty<Claim>())
                    .Where(c =>
                        c.Type != ClaimTypes.Role &&
                        !string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase) &&
                        c.Type != ClaimTypes.NameIdentifier &&
                        c.Type != ClaimTypes.Name)
                    .ToList();

                existingClaims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)));
                existingClaims.Add(new Claim(ClaimTypes.Name, user.Username));
                existingClaims.Add(new Claim(ClaimTypes.Role, user.Role.Name.Trim().ToUpperInvariant()));

                var identity = new ClaimsIdentity(
                    existingClaims,
                    context.Scheme.Name,
                    ClaimTypes.Name,
                    ClaimTypes.Role);

                context.Principal = new ClaimsPrincipal(identity);
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();

                var authHeader = context.Request.Headers.Authorization.ToString();
                var authError = context.HttpContext.Items["JwtAuthError"] as string;

                string detail;
                if (string.IsNullOrWhiteSpace(authHeader) ||
                    !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(authHeader["Bearer ".Length..]))
                {
                    detail = "No se proporcionó un token JWT válido en la cabecera 'Authorization: Bearer <token>'.";
                }
                else if (authError == "expired")
                {
                    detail = "El token JWT ha expirado. Inicia sesión nuevamente.";
                }
                else if (authError == "inactive")
                {
                    detail = "La cuenta asociada al token JWT no existe o se encuentra inactiva.";
                }
                else
                {
                    detail = "El token JWT proporcionado no es válido.";
                }

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "No autorizado",
                    Detail = detail,
                    Instance = context.Request.Path
                };

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, problemJsonOptions));
            },
            OnForbidden = async context =>
            {
                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Acceso denegado",
                    Detail = "No tienes permisos suficientes para realizar esta operación.",
                    Instance = context.Request.Path
                };

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, problemJsonOptions));
            }
        };
    });

builder.Services.AddAuthorization();

// Swagger / OpenAPI con esquema JWT Bearer
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Northwind API (JWT)",
        Version = "v1",
        Description = "API REST de Northwind autenticada exclusivamente con JWT Bearer."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Ingresa el token JWT. Ejemplo: Bearer {tu_token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// Configure CORS to allow connections from any origin, method, and header
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register ContextDB for dependency injection
builder.Services.AddDbContext<ContextDB>();

var app = builder.Build();

// Manejo centralizado de excepciones (500) en formato ProblemDetails
app.UseExceptionHandler();

// Convierte códigos de estado vacíos en ProblemDetails
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Enable CORS before Authentication & Authorization
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Aplica automáticamente las migraciones pendientes al iniciar
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ContextDB>();
    dbContext.Database.Migrate();
}

app.Run();
