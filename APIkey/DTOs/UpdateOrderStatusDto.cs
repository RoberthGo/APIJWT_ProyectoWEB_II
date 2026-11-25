using System.ComponentModel.DataAnnotations;

namespace APIkey.DTOs;

public class UpdateOrderStatusDto
{
    [Required(ErrorMessage = "El estado del pedido es obligatorio.")]
    [MaxLength(30)]
    public string Status { get; set; } = string.Empty;
}
