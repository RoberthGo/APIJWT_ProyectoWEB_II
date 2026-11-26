using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;

namespace APIkey.Models;

[Table("login_attempts")]
public class LoginAttempt
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int AccountKey { get; set; }
    public int Failures { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Las cuentas desconocidas siguen el mismo flujo sin guardar nombres ni crear filas sin límite.
    public static int UnknownAccountKey(string username)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(username.ToUpperInvariant()));
        return -1 - BinaryPrimitives.ReadUInt16BigEndian(hash);
    }

    public void RecordFailure(DateTime now)
    {
        if (++Failures == 3)
        {
            Failures = 0;
            LockedUntil = now.AddMinutes(15);
        }
    }
}
