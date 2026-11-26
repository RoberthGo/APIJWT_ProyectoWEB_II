using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace APIkey.DTOs;

public class AddCartItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "El producto seleccionado no es válido.")]
    public int ProductId { get; set; }

    [Range(1, 32767, ErrorMessage = "La cantidad debe ser un entero entre 1 y 32,767.")]
    public int Quantity { get; set; }
}

public class CheckoutCartDto
{
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

public class CartItemDto
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public bool Discontinued { get; set; }
}

public class CartDto
{
    public int TotalQuantity { get; set; }
    public decimal Total { get; set; }
    public List<CartItemDto> Items { get; set; } = new();
}
