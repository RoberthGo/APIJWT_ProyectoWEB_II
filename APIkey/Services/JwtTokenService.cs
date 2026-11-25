using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using APIkey.Models;
using APIkey.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace APIkey.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _options.Validate();
        _signingCredentials = new SigningCredentials(
            _options.CreateSigningKey(),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user)
    {
        var now = DateTime.UtcNow;
        var issuedAtUtc = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
        var expiresAtUtc = DateTime.SpecifyKind(issuedAtUtc.AddMinutes(_options.ExpirationMinutes), DateTimeKind.Utc);
        var issuedAtUnix = new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds();

        // No se incluye el nombre del rol en el payload del JWT para evitar su exposición en el cliente;
        // la autorización por rol se resuelve y valida siempre contra la base de datos en OnTokenValidated.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, issuedAtUnix.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: _signingCredentials
        );

        var token = _tokenHandler.WriteToken(tokenDescriptor);
        return (token, expiresAtUtc);
    }
}
