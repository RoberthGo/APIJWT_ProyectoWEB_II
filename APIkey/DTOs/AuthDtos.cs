using System;
using System.ComponentModel.DataAnnotations;

namespace APIkey.DTOs;

// POST /api/auth/register
public class RegisterDto
{
    public string? Altcha { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

// POST /api/auth/login
public class LoginDto
{
    public string? Altcha { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

// Banderas de interfaz calculadas en servidor (sin exponer nombres de roles)
public class UiPermissionsDto
{
    public bool CanViewProductsAdmin { get; set; }

    public bool CanCreateProducts { get; set; }

    public bool CanEditProductDetails { get; set; }

    public bool CanEditProductPricingStock { get; set; }

    public bool CanDeleteProducts { get; set; }

    public bool CanDiscontinueProducts { get; set; }

    public bool CanViewCustomers { get; set; }

    public bool CanViewAllOrders { get; set; }

    public bool CanUpdateOrderStatus { get; set; }

    public bool CanCancelOwnOrders { get; set; }

    public bool CanPurchaseCatalog { get; set; }
}

// Respuesta de GET /api/auth/me
public class SessionProfileDto
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DefaultView { get; set; } = "./catalogo.html";

    public UiPermissionsDto Permissions { get; set; } = new();
}

// Respuesta de Login / Registro con JWT Bearer y capacidades de interfaz
public class AuthResponseDto
{
    public int UserId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    public DateTime ExpiresAt { get; set; }

    public string DefaultView { get; set; } = "./catalogo.html";

    public UiPermissionsDto Permissions { get; set; } = new();
}
