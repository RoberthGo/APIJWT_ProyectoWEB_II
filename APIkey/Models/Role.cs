using System.Collections.Generic;

namespace APIkey.Models;

public class Role
{
    public const int AdminId = 1;
    public const int EmpleadoId = 2;
    public const int ClienteId = 3;

    public const string AdminName = "ADMIN";
    public const string EmpleadoName = "EMPLEADO";
    public const string ClienteName = "CLIENTE";

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
