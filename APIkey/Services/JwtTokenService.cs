using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using APIkey.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace APIkey.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        _secretKey = jwtSection["SecretKey"]
            ?? throw new InvalidOperationException("La configuración 'Jwt:SecretKey' es obligatoria.");
        _issuer = jwtSection["Issuer"]
            ?? throw new InvalidOperationException("La configuración 'Jwt:Issuer' es obligatoria.");
        _audience = jwtSection["Audience"]
            ?? throw new InvalidOperationException("La configuración 'Jwt:Audience' es obligatoria.");

        if (Encoding.UTF8.GetByteCount(_secretKey) < 32)
        {
            throw new InvalidOperationException("La clave 'Jwt:SecretKey' debe tener al menos 32 bytes.");
        }

        if (!int.TryParse(jwtSection["ExpirationMinutes"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _expirationMinutes)
            || _expirationMinutes <= 0)
        {
            _expirationMinutes = 120;
        }
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user)
    {
        var now = DateTime.UtcNow;
        var issuedAtUtc = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
        var expiresAtUtc = DateTime.SpecifyKind(issuedAtUtc.AddMinutes(_expirationMinutes), DateTimeKind.Utc);
        var issuedAtUnix = new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds();

        // No se incluye el nombre del rol en el payload del JWT para evitar su exposición en el cliente;
        // la autorización por rol se resuelve y valida siempre contra la base de datos en OnTokenValidated.
        var claims = new List<System.Security.Claims.Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, issuedAtUnix.ToString(CultureInfo.InvariantCulture), System.Security.Claims.ClaimValueTypes.Integer64)
        };

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: credentials
        );

        var token = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        return (token, expiresAtUtc);
    }
}
