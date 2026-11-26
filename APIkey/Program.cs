using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Altcha.AspNetCore;
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

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Add services to the container.
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var safeErrors = new Dictionary<string, string[]>();
            foreach (var entry in context.ModelState)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)
                    || entry.Key.StartsWith("$", StringComparison.Ordinal)
                    || string.Equals(entry.Key, "dto", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.Value?.Errors is not { Count: > 0 } errors) continue;
                var msgs = errors
                    .Select(e => e.ErrorMessage?.Trim())
                    .Where(m => !string.IsNullOrWhiteSpace(m)
                        && !m.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                        && !m.StartsWith("A non-empty", StringComparison.OrdinalIgnoreCase)
                        && !m.StartsWith("Could not", StringComparison.OrdinalIgnoreCase)
                        && !m.StartsWith("Unexpected", StringComparison.OrdinalIgnoreCase)
                        && !m.Contains("System.", StringComparison.OrdinalIgnoreCase)
                        && !m.Contains("JSON", StringComparison.OrdinalIgnoreCase)
                        && !m.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                        && !m.Contains("Path:", StringComparison.OrdinalIgnoreCase)
                        && !m.Contains("byte position", StringComparison.OrdinalIgnoreCase))
                    .Cast<string>()
                    .Distinct()
                    .ToArray();
                if (msgs.Length > 0)
                {
                    safeErrors[entry.Key] = msgs;
                }
            }

            var detail = safeErrors.Values.SelectMany(v => v).FirstOrDefault()
                ?? "Solicitud inválida. Verifica los datos ingresados.";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Datos inválidos",
                Detail = detail
            };
            if (safeErrors.Count > 0)
            {
                problem.Extensions["errors"] = safeErrors;
            }

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json; charset=utf-8" }
            };
        };
    });
builder.Services.AddAltcha(options =>
{
    options.HmacSignatureSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    options.HmacKeySignatureSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
});

// Soporte nativo para ProblemDetails (RFC 7807) sin exponer rutas, trazas ni detalles internos
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = null;
        ctx.ProblemDetails.Type = null;
        ctx.ProblemDetails.Extensions.Remove("traceId");
        ctx.ProblemDetails.Extensions.Remove("exception");
        if (ctx.ProblemDetails.Status is >= 500)
        {
            ctx.ProblemDetails.Extensions.Clear();
            ctx.ProblemDetails.Title = "Error interno del servidor";
            ctx.ProblemDetails.Detail = "Ocurrió un error al procesar la solicitud. Intenta nuevamente más tarde.";
        }
        else if (ctx.ProblemDetails.Status == StatusCodes.Status404NotFound
                 && string.IsNullOrWhiteSpace(ctx.ProblemDetails.Detail))
        {
            ctx.ProblemDetails.Title = "No encontrado";
            ctx.ProblemDetails.Detail = "El recurso solicitado no existe.";
        }
        else if (ctx.ProblemDetails.Status == StatusCodes.Status405MethodNotAllowed)
        {
            ctx.ProblemDetails.Title = "Método no permitido";
            ctx.ProblemDetails.Detail = "La operación solicitada no está permitida.";
        }
        else if (ctx.ProblemDetails.Status == StatusCodes.Status415UnsupportedMediaType)
        {
            ctx.ProblemDetails.Title = "Formato no soportado";
            ctx.ProblemDetails.Detail = "El formato de la solicitud no es válido.";
        }
    };
});

// Configuración JWT tipada y validada una sola vez al arranque.
// Si no se define Jwt:SecretKey (ni en variables de entorno ni en User Secrets),
// se genera automáticamente una clave criptográfica aleatoria de 512 bits en memoria al iniciar.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
var effectiveSecretKey = string.IsNullOrWhiteSpace(jwtOptions.SecretKey)
    ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
    : jwtOptions.SecretKey;
jwtOptions.SecretKey = effectiveSecretKey;
jwtOptions.Validate();

builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.PostConfigure<JwtOptions>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.SecretKey))
    {
        opts.SecretKey = effectiveSecretKey;
    }

    opts.Validate();
});

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
                    detail = "Se requiere iniciar sesión para realizar esta operación.";
                }
                else if (authError == "expired")
                {
                    detail = "Tu sesión ha expirado. Inicia sesión nuevamente.";
                }
                else if (authError == "inactive")
                {
                    detail = "Tu cuenta no se encuentra activa. Inicia sesión nuevamente.";
                }
                else
                {
                    detail = "No autorizado. Inicia sesión nuevamente.";
                }

                var problemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "No autorizado",
                    Detail = detail
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
                    Detail = "No tienes permisos suficientes para realizar esta operación."
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
              .AllowAnyHeader()
              .WithExposedHeaders("Retry-After");
    });
});

// Register ContextDB for dependency injection
builder.Services.AddDbContext<ContextDB>();

var app = builder.Build();

// Manejo centralizado de excepciones (500) en formato ProblemDetails
app.UseExceptionHandler();

// Convierte códigos de estado vacíos en ProblemDetails
app.UseStatusCodePages();

// Cabeceras de seguridad en todas las respuestas
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none';";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers.Remove("X-Powered-By");
    await next();
});

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
app.MapAltchaChallenge("/api/auth/altcha/challenge").AllowAnonymous();

// Aplica automáticamente las migraciones pendientes al iniciar
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ContextDB>();
    dbContext.Database.Migrate();
}

app.Run();
