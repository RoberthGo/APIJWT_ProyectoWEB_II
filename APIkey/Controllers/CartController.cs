using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Models;

namespace APIkey.Controllers
{
    [Authorize(Roles = Role.ClienteName)]
    [Route("api/[controller]")]
    [ApiController]
    public class CartController : ControllerBase
    {
        private readonly ContextDB _context;

        public CartController(ContextDB context)
        {
            _context = context;
        }

        private bool TryGetCurrentUserId(out int userId)
        {
            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(rawUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) && userId > 0;
        }

        private async Task<bool> LockActiveUserAsync(int userId)
        {
            var users = await _context.Users
                .FromSqlInterpolated($"SELECT * FROM users WHERE Id = {userId} AND IsActive = 1 FOR UPDATE")
                .ToListAsync();
            return users.Count > 0;
        }

        private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // Ignorar error de rollback si la transacción ya fue abortada por la BD
            }
        }

        private async Task<CartDto> BuildCartDtoAsync(int userId)
        {
            var items = await _context.CartItems
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.AddedAt)
                .ThenBy(c => c.ProductId)
                .Select(c => new CartItemDto
                {
                    ProductId = c.ProductId,
                    ProductName = c.Product != null ? c.Product.ProductName : $"Producto #{c.ProductId}",
                    UnitPrice = c.Product != null ? (c.Product.UnitPrice ?? 0m) : 0m,
                    Quantity = c.Quantity,
                    Subtotal = (c.Product != null ? (c.Product.UnitPrice ?? 0m) : 0m) * c.Quantity,
                    Discontinued = c.Product != null && c.Product.Discontinued == true
                })
                .ToListAsync();

            return new CartDto
            {
                TotalQuantity = items.Sum(i => i.Quantity),
                Total = items.Sum(i => i.Subtotal),
                Items = items
            };
        }

        // GET: api/cart
        [HttpGet]
        public async Task<ActionResult<CartDto>> GetCart()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo validar tu sesión activa."
                );
            }

            var cart = await BuildCartDtoAsync(userId);
            return Ok(cart);
        }

        // POST: api/cart/items
        // Agrega producto al carrito del usuario y reserva atómicamente su inventario
        [HttpPost("items")]
        public async Task<ActionResult<CartDto>> AddItem(AddCartItemDto dto)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo validar tu sesión activa."
                );
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Bloqueo en orden estricto (users -> cart_items -> products) para evitar interbloqueos y condiciones de carrera
                if (!await LockActiveUserAsync(userId))
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autorizado",
                        detail: "No se pudo validar tu sesión activa."
                    );
                }

                var existingItem = (await _context.CartItems
                    .FromSqlInterpolated($"SELECT * FROM cart_items WHERE UserID = {userId} AND ProductID = {dto.ProductId} FOR UPDATE")
                    .ToListAsync())
                    .FirstOrDefault();

                var product = (await _context.Products
                    .FromSqlInterpolated($"SELECT * FROM products WHERE ProductID = {dto.ProductId} FOR UPDATE")
                    .ToListAsync())
                    .FirstOrDefault();

                if (product == null)
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Producto no encontrado",
                        detail: "El producto seleccionado no existe."
                    );
                }

                if (product.Discontinued == true)
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Producto descontinuado",
                        detail: $"El producto '{product.ProductName}' está descontinuado y no puede agregarse al carrito."
                    );
                }

                var currentStock = product.UnitsInStock ?? 0;
                if (currentStock < dto.Quantity)
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Stock insuficiente",
                        detail: $"Stock insuficiente para '{product.ProductName}'. Disponible: {currentStock}, solicitado: {dto.Quantity}."
                    );
                }

                var newCartQuantity = (existingItem?.Quantity ?? 0) + dto.Quantity;
                if (newCartQuantity < 1 || newCartQuantity > 32767)
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Cantidad inválida",
                        detail: "La cantidad total en el carrito excede el límite permitido."
                    );
                }

                product.UnitsInStock = currentStock - dto.Quantity;

                if (existingItem != null)
                {
                    existingItem.Quantity = newCartQuantity;
                }
                else
                {
                    _context.CartItems.Add(new CartItem
                    {
                        UserId = userId,
                        ProductId = dto.ProductId,
                        Quantity = dto.Quantity,
                        AddedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var cart = await BuildCartDtoAsync(userId);
                return Ok(cart);
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException)
            {
                await RollbackQuietlyAsync(transaction);
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al actualizar el carrito",
                    detail: "No fue posible agregar el producto al carrito. Intenta nuevamente."
                );
            }
        }

        // DELETE: api/cart
        // Cancelación completa del carrito: regresa todos los productos al inventario de forma atómica
        [HttpDelete]
        public async Task<ActionResult<CartDto>> CancelCart()
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo validar tu sesión activa."
                );
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (!await LockActiveUserAsync(userId))
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autorizado",
                        detail: "No se pudo validar tu sesión activa."
                    );
                }

                var cartItems = await _context.CartItems
                    .FromSqlInterpolated($"SELECT * FROM cart_items WHERE UserID = {userId} ORDER BY ProductID FOR UPDATE")
                    .ToListAsync();

                if (cartItems.Count == 0)
                {
                    await transaction.CommitAsync();
                    return Ok(new CartDto());
                }

                foreach (var item in cartItems)
                {
                    if (item.Quantity > 0)
                    {
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE products SET UnitsInStock = LEAST(32767, COALESCE(UnitsInStock, 0) + {item.Quantity}) WHERE ProductID = {item.ProductId}");
                    }
                }

                _context.CartItems.RemoveRange(cartItems);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new CartDto());
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException)
            {
                await RollbackQuietlyAsync(transaction);
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al cancelar el carrito",
                    detail: "No fue posible cancelar el carrito. Intenta nuevamente."
                );
            }
        }

        // POST: api/cart/checkout
        // Confirma la compra de todo el carrito y registra la orden (el stock ya fue reservado al agregar)
        [HttpPost("checkout")]
        public async Task<ActionResult<OrderDto>> CheckoutCart(CheckoutCartDto dto)
        {
            if (!TryGetCurrentUserId(out var userId))
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "No autorizado",
                    detail: "No se pudo validar tu sesión activa."
                );
            }

            var shipName = (dto.ShipName ?? string.Empty).Trim();
            var shipAddress = (dto.ShipAddress ?? string.Empty).Trim();
            var shipCity = (dto.ShipCity ?? string.Empty).Trim();
            var shipCountry = (dto.ShipCountry ?? string.Empty).Trim();

            if (shipName.Length < 2 || shipName.Length > 80
                || shipAddress.Length < 4 || shipAddress.Length > 200
                || shipCity.Length < 2 || shipCity.Length > 60
                || shipCountry.Length < 2 || shipCountry.Length > 60)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Datos inválidos",
                    detail: "Verifica que los datos de envío estén completos y dentro de la longitud permitida."
                );
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (!await LockActiveUserAsync(userId))
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "No autorizado",
                        detail: "No se pudo validar tu sesión activa."
                    );
                }

                var cartItems = await _context.CartItems
                    .FromSqlInterpolated($"SELECT * FROM cart_items WHERE UserID = {userId} ORDER BY ProductID FOR UPDATE")
                    .ToListAsync();

                if (cartItems.Count == 0)
                {
                    await RollbackQuietlyAsync(transaction);
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Carrito vacío",
                        detail: "Tu carrito no tiene productos. Agrega al menos uno antes de comprar."
                    );
                }

                var productIds = cartItems.Select(c => c.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .AsNoTracking()
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId);

                var orderDetails = new List<OrderDetail>(cartItems.Count);
                var itemDtos = new List<OrderItemDto>(cartItems.Count);

                foreach (var item in cartItems)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                    {
                        await RollbackQuietlyAsync(transaction);
                        return Problem(
                            statusCode: StatusCodes.Status404NotFound,
                            title: "Producto no encontrado",
                            detail: "Uno de los productos del carrito ya no está disponible."
                        );
                    }

                    if (product.Discontinued == true)
                    {
                        await RollbackQuietlyAsync(transaction);
                        return Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Producto descontinuado",
                            detail: $"El producto '{product.ProductName}' fue descontinuado. Cancela el carrito para devolver los productos al inventario."
                        );
                    }

                    if (item.Quantity < 1 || item.Quantity > 32767)
                    {
                        await RollbackQuietlyAsync(transaction);
                        return Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Cantidad inválida",
                            detail: "Las cantidades del carrito no son válidas."
                        );
                    }

                    var unitPrice = product.UnitPrice ?? 0m;
                    orderDetails.Add(new OrderDetail
                    {
                        ProductId = product.ProductId,
                        UnitPrice = unitPrice,
                        Quantity = item.Quantity,
                        Discount = 0f
                    });

                    itemDtos.Add(new OrderItemDto
                    {
                        ProductId = product.ProductId,
                        ProductName = product.ProductName,
                        UnitPrice = unitPrice,
                        Quantity = item.Quantity,
                        Subtotal = unitPrice * item.Quantity
                    });
                }

                var now = DateTime.UtcNow;
                var order = new Order
                {
                    UserId = userId,
                    OrderDate = now,
                    RequiredDate = now.AddDays(7),
                    ShippedDate = null,
                    Freight = 0m,
                    ShipName = shipName,
                    ShipAddress = shipAddress,
                    ShipCity = shipCity,
                    ShipCountry = shipCountry,
                    Status = OrdersController.StatusPendiente,
                    OrderDetails = orderDetails
                };

                _context.Orders.Add(order);
                _context.CartItems.RemoveRange(cartItems);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var createdDto = new OrderDto
                {
                    OrderId = order.OrderId,
                    OrderDate = order.OrderDate,
                    RequiredDate = order.RequiredDate,
                    ShippedDate = order.ShippedDate,
                    Freight = order.Freight,
                    ShipName = order.ShipName,
                    ShipAddress = order.ShipAddress,
                    ShipCity = order.ShipCity,
                    ShipCountry = order.ShipCountry,
                    Status = OrdersController.StatusPendiente,
                    Total = itemDtos.Sum(i => i.Subtotal),
                    Items = itemDtos
                };

                return StatusCode(StatusCodes.Status201Created, createdDto);
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException)
            {
                await RollbackQuietlyAsync(transaction);
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al registrar la compra",
                    detail: "No fue posible completar la compra. Verifica los datos e intenta nuevamente."
                );
            }
        }
    }
}
