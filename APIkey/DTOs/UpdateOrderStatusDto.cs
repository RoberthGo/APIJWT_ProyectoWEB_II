using System.ComponentModel.DataAnnotations;

namespace APIkey.DTOs;

public class UpdateOrderStatusDto
{
    [Required(ErrorMessage = "El estado del pedido es obligatorio.")]
    [MaxLength(30, ErrorMessage = "El estado del pedido no es válido.")]
    public string Status { get; set; } = string.Empty;
}
