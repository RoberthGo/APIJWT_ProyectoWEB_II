using System;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace APIkey.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public int ExpirationMinutes { get; set; } = 120;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException(
                "La configuración 'Jwt:SecretKey' es obligatoria. Defínela mediante variables de entorno (Jwt__SecretKey en Azure) o User Secrets en desarrollo (dotnet user-secrets set \"Jwt:SecretKey\" \"<clave>\").");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("La configuración 'Jwt:Issuer' es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("La configuración 'Jwt:Audience' es obligatoria.");
        }

        if (Encoding.UTF8.GetByteCount(SecretKey) < 32)
        {
            throw new InvalidOperationException("La clave 'Jwt:SecretKey' debe tener al menos 32 bytes.");
        }

        if (ExpirationMinutes <= 0)
        {
            ExpirationMinutes = 120;
        }
    }

    public SymmetricSecurityKey CreateSigningKey() =>
        new(Encoding.UTF8.GetBytes(SecretKey));
}
