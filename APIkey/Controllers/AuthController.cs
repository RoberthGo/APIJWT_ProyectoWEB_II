using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Filters;
using APIkey.Helpers;
using APIkey.Models;

namespace APIkey.Controllers
{
    [EnableCors("AllowAll")]
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ContextDB _context;

        public AuthController(ContextDB context)
        {
            _context = context;
        }

        // POST: api/auth/register
        // Registra un nuevo usuario con rol CLIENTE en base de datos y genera su primera API Key
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponseDto>> PostRegister(RegisterDto dto)
        {
            var usernameExists = await _context.Users
                .AnyAsync(u => u.Username.ToLower() == dto.Username.Trim().ToLower());

            if (usernameExists)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Usuario duplicado",
                    detail: $"El nombre de usuario '{dto.Username}' ya se encuentra registrado."
                );
            }

            var emailExists = await _context.Users
                .AnyAsync(u => u.Email.ToLower() == dto.Email.Trim().ToLower());

            if (emailExists)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Correo duplicado",
                    detail: $"El correo electrónico '{dto.Email}' ya se encuentra registrado."
                );
            }

            try
            {
                // 1. Crear el usuario asignando estrictamente el rol CLIENTE desde base de datos
                var user = new User
                {
                    Username = dto.Username.Trim(),
                    Email = dto.Email.Trim().ToLowerInvariant(),
                    PasswordHash = SecurityHelper.HashPassword(dto.Password),
                    RoleId = Role.ClienteId,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // 2. Generar su primera API Key
                var rawApiKey = SecurityHelper.GenerateApiKey();
                var createdAt = DateTime.UtcNow;
                var expiresAt = createdAt.AddDays(30);

                var apiKeyEntity = new ApiKey
                {
                    ClientIdentifier = user.Username,
                    KeyHash = SecurityHelper.HashApiKey(rawApiKey),
                    CreatedAt = createdAt,
                    ExpiresAt = expiresAt,
                    IsActive = true
                };

                _context.ApiKeys.Add(apiKeyEntity);
                await _context.SaveChangesAsync();

                var (defaultView, permissions) = SecurityHelper.BuildUiProfile(Role.ClienteName);

                return StatusCode(StatusCodes.Status201Created, new AuthResponseDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    ApiKey = rawApiKey,
                    ExpiresAt = expiresAt,
                    DefaultView = defaultView,
                    Permissions = permissions
                });
            }
            catch (DbUpdateException)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al registrar usuario",
                    detail: "No fue posible completar el registro de la cuenta. Verifica los datos ingresados."
                );
            }
        }

        // POST: api/auth/login
        // Valida credenciales contra PBKDF2, lee el rol desde BD y retorna API Key + capacidades de UI
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponseDto>> PostLogin(LoginDto dto)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == dto.Username.Trim().ToLower() && u.IsActive);

            if (user == null || user.Role == null || !SecurityHelper.VerifyPassword(dto.Password, user.PasswordHash))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Credenciales inválidas",
                    detail: "El usuario o la contraseña proporcionados son incorrectos."
                );
            }

            try
            {
                // 1. Invalidar todas las API Keys anteriores activas de este usuario
                var oldKeys = await _context.ApiKeys
                    .Where(k => k.ClientIdentifier.ToLower() == user.Username.ToLower() && k.IsActive)
                    .ToListAsync();

                oldKeys.ForEach(k => k.IsActive = false);

                // 2. Generar y guardar la nueva API Key
                var rawApiKey = SecurityHelper.GenerateApiKey();
                var createdAt = DateTime.UtcNow;
                var expiresAt = createdAt.AddDays(30);

                var apiKeyEntity = new ApiKey
                {
                    ClientIdentifier = user.Username,
                    KeyHash = SecurityHelper.HashApiKey(rawApiKey),
                    CreatedAt = createdAt,
                    ExpiresAt = expiresAt,
                    IsActive = true
                };

                _context.ApiKeys.Add(apiKeyEntity);
                await _context.SaveChangesAsync();

                var (defaultView, permissions) = SecurityHelper.BuildUiProfile(user.Role.Name);

                return Ok(new AuthResponseDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    ApiKey = rawApiKey,
                    ExpiresAt = expiresAt,
                    DefaultView = defaultView,
                    Permissions = permissions
                });
            }
            catch (DbUpdateException)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al iniciar sesión",
                    detail: "No fue posible emitir las credenciales de sesión. Intenta nuevamente."
                );
            }
        }

        // GET: api/auth/me
        // Valida la sesión activa en BD y retorna las capacidades de UI sin exponer nombres de rol
        [HttpGet("me")]
        [ApiKey]
        public async Task<ActionResult<SessionProfileDto>> GetMe()
        {
            var clientIdentifier = HttpContext.Items["ClientIdentifier"] as string;
            if (string.IsNullOrWhiteSpace(clientIdentifier))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo identificar la sesión activa."
                );
            }

            var normalized = clientIdentifier.Trim().ToLowerInvariant();
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized && u.IsActive);

            if (user == null || user.Role == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "La cuenta asociada a la sesión ya no se encuentra activa."
                );
            }

            var (defaultView, permissions) = SecurityHelper.BuildUiProfile(user.Role.Name);

            return Ok(new SessionProfileDto
            {
                UserId = user.Id,
                Username = user.Username,
                DefaultView = defaultView,
                Permissions = permissions
            });
        }
    }
}
