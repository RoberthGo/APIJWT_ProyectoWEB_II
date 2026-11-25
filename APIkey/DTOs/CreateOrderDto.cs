using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace APIkey.DTOs;

public class CreateOrderItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "El identificador de producto debe ser un entero positivo.")]
    public int ProductId { get; set; }

    [Range(1, 32767, ErrorMessage = "La cantidad debe ser un entero entre 1 y 32,767.")]
    public int Quantity { get; set; }
}

public class CreateOrderDto
{
    [Required(ErrorMessage = "Debes incluir al menos un producto en el pedido.")]
    [MinLength(1, ErrorMessage = "Debes incluir al menos un producto en el pedido.")]
    public List<CreateOrderItemDto> Items { get; set; } = new();

    [Required(ErrorMessage = "El nombre del destinatario es obligatorio.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "El destinatario debe tener entre 2 y 80 caracteres.")]
    public string ShipName { get; set; } = string.Empty;

    [Required(ErrorMessage = "La dirección de envío es obligatoria.")]
    [StringLength(200, MinimumLength = 4, ErrorMessage = "La dirección debe tener entre 4 y 200 caracteres.")]
    public string ShipAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "La ciudad de envío es obligatoria.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "La ciudad debe tener entre 2 y 60 caracteres.")]
    public string ShipCity { get; set; } = string.Empty;

    [Required(ErrorMessage = "El país de envío es obligatorio.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "El país debe tener entre 2 y 60 caracteres.")]
    public string ShipCountry { get; set; } = string.Empty;
}
