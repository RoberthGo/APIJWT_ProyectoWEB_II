using System;
using System.Security.Cryptography;
using System.Text;
using APIkey.DTOs;
using APIkey.Models;

namespace APIkey.Helpers;

public static class SecurityHelper
{
    public static string GenerateApiKey()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        return $"ak_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";
    }

    public static string HashApiKey(string plainTextKey)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(plainTextKey));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        try
        {
            byte[] salt = Convert.FromHexString(parts[0]);
            byte[] expectedHash = Convert.FromHexString(parts[1]);

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 32);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Calcula exclusivamente en servidor las banderas de interfaz y la vista inicial
    /// a partir del rol almacenado en base de datos, sin exponer el nombre del rol al cliente.
    /// </summary>
    public static (string DefaultView, UiPermissionsDto Permissions) BuildUiProfile(string? dbRoleName)
    {
        var normalized = (dbRoleName ?? string.Empty).Trim().ToUpperInvariant();

        if (normalized == Role.AdminName)
        {
            return ("./productos.html", new UiPermissionsDto
            {
                CanViewProductsAdmin = true,
                CanCreateProducts = true,
                CanEditProductDetails = true,
                CanEditProductPricingStock = true,
                CanDeleteProducts = true,
                CanDiscontinueProducts = true,
                CanViewCustomers = true,
                CanViewAllOrders = true,
                CanUpdateOrderStatus = true,
                CanCancelOwnOrders = false,
                CanPurchaseCatalog = false
            });
        }

        if (normalized == Role.EmpleadoName)
        {
            return ("./productos.html", new UiPermissionsDto
            {
                CanViewProductsAdmin = true,
                CanCreateProducts = false,
                CanEditProductDetails = false,
                CanEditProductPricingStock = true,
                CanDeleteProducts = false,
                CanDiscontinueProducts = false,
                CanViewCustomers = true,
                CanViewAllOrders = true,
                CanUpdateOrderStatus = false,
                CanCancelOwnOrders = false,
                CanPurchaseCatalog = false
            });
        }

        return ("./catalogo.html", new UiPermissionsDto
        {
            CanViewProductsAdmin = false,
            CanCreateProducts = false,
            CanEditProductDetails = false,
            CanEditProductPricingStock = false,
            CanDeleteProducts = false,
            CanDiscontinueProducts = false,
            CanViewCustomers = false,
            CanViewAllOrders = false,
            CanUpdateOrderStatus = false,
            CanCancelOwnOrders = true,
            CanPurchaseCatalog = true
        });
    }
}
