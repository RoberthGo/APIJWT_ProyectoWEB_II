using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Altcha.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Helpers;
using APIkey.Models;
using APIkey.Services;

namespace APIkey.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private static readonly string DummyPasswordHash = SecurityHelper.HashPassword(string.Empty);
        private readonly ContextDB _context;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IAltchaService _altcha;

        public AuthController(ContextDB context, IJwtTokenService jwtTokenService, IAltchaService altcha)
        {
            _context = context;
            _jwtTokenService = jwtTokenService;
            _altcha = altcha;
        }

        // POST: api/auth/register
        // Registra un nuevo usuario con rol CLIENTE en base de datos y emite su token JWT
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponseDto>> PostRegister(RegisterDto dto)
        {
            if (!(await _altcha.VerifyAsync(dto.Altcha)).Verified)
                return CaptchaRequired();

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

                // 2. Generar su token JWT firmado
                var (token, expiresAt) = _jwtTokenService.GenerateToken(user);
                var (defaultView, permissions) = SecurityHelper.BuildUiProfile(Role.ClienteName);

                return StatusCode(StatusCodes.Status201Created, new AuthResponseDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    Token = token,
                    TokenType = "Bearer",
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
        // Valida credenciales contra PBKDF2, lee el rol desde BD y retorna JWT Bearer + capacidades de UI
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<AuthResponseDto>> PostLogin(LoginDto dto)
        {
            var username = dto.Username.Trim();
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && u.IsActive);

            var key = user?.Id ?? LoginAttempt.UnknownAccountKey(username);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            // El upsert toma un bloqueo de fila hasta el commit, incluso para intentos simultáneos.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO login_attempts (AccountKey, Failures) VALUES ({key}, 0) ON DUPLICATE KEY UPDATE AccountKey = AccountKey");
            var attempt = (await _context.LoginAttempts.FindAsync(key))!;
            var now = DateTime.UtcNow;
            if (attempt.LockedUntil > now)
                return LoginLocked(attempt.LockedUntil.Value - now);

            if ((attempt.LockedUntil.HasValue || dto.Altcha != null) && !(await _altcha.VerifyAsync(dto.Altcha)).Verified)
                return CaptchaRequired();

            var passwordMatches = SecurityHelper.VerifyPassword(dto.Password, user?.PasswordHash ?? DummyPasswordHash);
            if (user == null || user.Role == null || !passwordMatches)
            {
                attempt.RecordFailure(now);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                if (attempt.LockedUntil > now)
                    return LoginLocked(attempt.LockedUntil.Value - now);

                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Credenciales inválidas",
                    detail: "El usuario o la contraseña proporcionados son incorrectos."
                );
            }

            _context.LoginAttempts.Remove(attempt);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            var (token, expiresAt) = _jwtTokenService.GenerateToken(user);
            var (defaultView, permissions) = SecurityHelper.BuildUiProfile(user.Role.Name);

            return Ok(new AuthResponseDto
            {
                UserId = user.Id,
                Username = user.Username,
                Token = token,
                TokenType = "Bearer",
                ExpiresAt = expiresAt,
                DefaultView = defaultView,
                Permissions = permissions
            });
        }

        private ObjectResult CaptchaRequired() =>
            UnprocessableEntity(new { detail = "Completa la verificación ALTCHA para continuar." });

        private ObjectResult LoginLocked(TimeSpan remaining)
        {
            Response.Headers.RetryAfter = Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { detail = "Espera a que termine el bloqueo para volver a iniciar sesión." });
        }

        // GET: api/auth/me
        // Valida la sesión activa mediante JWT contra BD y retorna las capacidades de UI sin exponer nombres de rol
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<SessionProfileDto>> GetMe()
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(rawUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo identificar la sesión activa."
                );
            }

            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

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
