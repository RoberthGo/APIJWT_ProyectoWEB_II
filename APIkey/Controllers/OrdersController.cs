using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Filters;
using APIkey.Models;

namespace APIkey.Controllers
{
    [EnableCors("AllowAll")]
    [ApiKey]
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        public const string StatusPendiente = "PENDIENTE";
        public const string StatusPendienteEnvio = "PENDIENTE_ENVIO";
        public const string StatusEnTransito = "EN_TRANSITO";
        public const string StatusEntregado = "ENTREGADO";
        public const string StatusCancelado = "CANCELADO";

        private static readonly TimeSpan TransitWindow = TimeSpan.FromDays(7);

        private readonly ContextDB _context;

        public OrdersController(ContextDB context)
        {
            _context = context;
        }

        private async Task<User?> ResolveCurrentUserAsync()
        {
            var clientIdentifier = HttpContext.Items["ClientIdentifier"] as string;
            if (string.IsNullOrWhiteSpace(clientIdentifier))
            {
                return null;
            }

            var normalized = clientIdentifier.Trim().ToLowerInvariant();
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized && u.IsActive);
        }

        private static string RemoveDiacritics(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string? NormalizeStatus(string? rawStatus)
        {
            if (string.IsNullOrWhiteSpace(rawStatus))
            {
                return null;
            }

            var key = RemoveDiacritics(rawStatus.Trim())
                .ToUpperInvariant()
                .Replace('-', '_')
                .Replace(' ', '_');

            while (key.Contains("__", StringComparison.Ordinal))
            {
                key = key.Replace("__", "_", StringComparison.Ordinal);
            }

            return key switch
            {
                "PENDIENTE" => StatusPendiente,
                "PENDIENTE_ENVIO" or "PENDIENTE_DE_ENVIO" => StatusPendienteEnvio,
                "EN_TRANSITO" => StatusEnTransito,
                "ENTREGADO" => StatusEntregado,
                "CANCELADO" => StatusCancelado,
                _ => null
            };
        }

        private static string ResolveEffectiveStatus(string? storedStatus, DateTime? shippedDate, DateTime? requiredDate, DateTime? referenceUtc = null)
        {
            var explicitStatus = NormalizeStatus(storedStatus);
            if (explicitStatus != null)
            {
                return explicitStatus;
            }

            if (shippedDate == null)
            {
                return StatusPendiente;
            }

            var now = referenceUtc ?? DateTime.UtcNow;
            if (shippedDate.Value > now)
            {
                return StatusEnTransito;
            }

            if (requiredDate != null)
            {
                return now < requiredDate.Value ? StatusEnTransito : StatusEntregado;
            }

            return (now - shippedDate.Value) <= TransitWindow ? StatusEnTransito : StatusEntregado;
        }

        private static IQueryable<OrderDto> ProjectOrderDtos(IQueryable<Order> query)
        {
            return query.Select(o => new OrderDto
            {
                OrderId = o.OrderId,
                OrderDate = o.OrderDate,
                RequiredDate = o.RequiredDate,
                ShippedDate = o.ShippedDate,
                Freight = o.Freight,
                ShipName = o.ShipName,
                ShipAddress = o.ShipAddress,
                ShipCity = o.ShipCity,
                ShipCountry = o.ShipCountry,
                Status = o.Status ?? string.Empty,
                Total = o.OrderDetails.Sum(od => od.UnitPrice * od.Quantity),
                Items = o.OrderDetails.Select(od => new OrderItemDto
                {
                    ProductId = od.ProductId,
                    ProductName = od.Product != null ? od.Product.ProductName : $"Producto #{od.ProductId}",
                    UnitPrice = od.UnitPrice,
                    Quantity = od.Quantity,
                    Subtotal = od.UnitPrice * od.Quantity
                }).ToList()
            });
        }

        private static OrderDto FinalizeOrderDto(OrderDto dto, DateTime? referenceUtc = null)
        {
            dto.Status = ResolveEffectiveStatus(dto.Status, dto.ShippedDate, dto.RequiredDate, referenceUtc);
            return dto;
        }

        // GET: api/orders
        // Solo ADMIN y EMPLEADO (validado en BD) pueden listar todas las órdenes
        [HttpGet]
        [RequireRoles(Role.AdminName, Role.EmpleadoName)]
        public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders()
        {
            var orders = await ProjectOrderDtos(
                _context.Orders
                    .AsNoTracking()
                    .OrderByDescending(o => o.OrderId)
            ).ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                FinalizeOrderDto(order, now);
            }

            return Ok(orders);
        }

        // GET: api/orders/mine
        // Retorna únicamente las órdenes vinculadas al usuario propietario de la API Key activa
        [HttpGet("mine")]
        public async Task<ActionResult<IEnumerable<OrderDto>>> GetMyOrders()
        {
            var user = await ResolveCurrentUserAsync();
            if (user == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado",
                    detail: "No se pudo identificar un usuario activo asociado a la API Key proporcionada."
                );
            }

            var orders = await ProjectOrderDtos(
                _context.Orders
                    .AsNoTracking()
                    .Where(o => o.UserId == user.Id)
                    .OrderByDescending(o => o.OrderId)
            ).ToListAsync();

            var now = DateTime.UtcNow;
            foreach (var order in orders)
            {
                FinalizeOrderDto(order, now);
            }

            return Ok(orders);
        }

        // GET: api/orders/{id}
        // ADMIN y EMPLEADO pueden consultar cualquier orden; CLIENTE solo sus propias órdenes
        [HttpGet("{id}")]
        public async Task<ActionResult<OrderDto>> GetOrder(int id)
        {
            var user = await ResolveCurrentUserAsync();
            if (user == null || user.Role == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado",
                    detail: "No se pudo identificar un usuario activo asociado a la API Key proporcionada."
                );
            }

            var orderEntity = await _context.Orders
                .AsNoTracking()
                .Where(o => o.OrderId == id)
                .Select(o => new { o.OrderId, o.UserId })
                .FirstOrDefaultAsync();

            if (orderEntity == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Orden no encontrada",
                    detail: $"No se encontró ninguna orden con el ID {id}."
                );
            }

            var dbRole = user.Role.Name.Trim().ToUpperInvariant();
            if (dbRole != Role.AdminName && dbRole != Role.EmpleadoName && orderEntity.UserId != user.Id)
            {
                return Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Acceso denegado",
                    detail: "No tienes permiso para consultar una orden que no pertenece a tu cuenta."
                );
            }

            var order = await ProjectOrderDtos(
                _context.Orders
                    .AsNoTracking()
                    .Where(o => o.OrderId == id)
            ).FirstAsync();

            FinalizeOrderDto(order);
            return Ok(order);
        }

        // POST: api/orders
        // Solo CLIENTE (validado en BD) puede registrar compras desde el catálogo
        [HttpPost]
        [RequireRoles(Role.ClienteName)]
        public async Task<ActionResult<OrderDto>> PostOrder(CreateOrderDto dto)
        {
            var user = await ResolveCurrentUserAsync();
            if (user == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado",
                    detail: "Debes autenticarte con una cuenta de usuario válida para registrar un pedido."
                );
            }

            var groupedItems = dto.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToList();

            if (groupedItems.Count == 0 || groupedItems.Any(i => i.Quantity < 1 || i.Quantity > 32767))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Cantidad inválida",
                    detail: "Las cantidades solicitadas deben ser enteros positivos dentro del rango permitido."
                );
            }

            var productIds = groupedItems.Select(i => i.ProductId).ToList();

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId);

                var orderDetails = new List<OrderDetail>();
                var itemDtos = new List<OrderItemDto>();

                foreach (var item in groupedItems)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                    {
                        await transaction.RollbackAsync();
                        return Problem(
                            statusCode: StatusCodes.Status404NotFound,
                            title: "Producto no encontrado",
                            detail: $"El producto #{item.ProductId} no existe."
                        );
                    }

                    if (product.Discontinued == true)
                    {
                        await transaction.RollbackAsync();
                        return Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Producto descontinuado",
                            detail: $"El producto '{product.ProductName}' está descontinuado y no puede comprarse."
                        );
                    }

                    var currentStock = product.UnitsInStock ?? 0;
                    if (currentStock < item.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Stock insuficiente",
                            detail: $"Stock insuficiente para '{product.ProductName}'. Disponible: {currentStock}, solicitado: {item.Quantity}."
                        );
                    }

                    product.UnitsInStock = currentStock - item.Quantity;

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
                    UserId = user.Id,
                    OrderDate = now,
                    RequiredDate = now.AddDays(7),
                    ShippedDate = null,
                    Freight = 0m,
                    ShipName = dto.ShipName.Trim(),
                    ShipAddress = dto.ShipAddress.Trim(),
                    ShipCity = dto.ShipCity.Trim(),
                    ShipCountry = dto.ShipCountry.Trim(),
                    Status = StatusPendiente,
                    OrderDetails = orderDetails
                };

                _context.Orders.Add(order);
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
                    Status = StatusPendiente,
                    Total = itemDtos.Sum(i => i.Subtotal),
                    Items = itemDtos
                };

                return CreatedAtAction(nameof(GetOrder), new { id = order.OrderId }, createdDto);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al registrar el pedido",
                    detail: "No fue posible registrar el pedido. Verifica los datos e intenta nuevamente."
                );
            }
        }

        // PATCH: api/orders/{id}/status
        // Validación estricta del rol del usuario en base de datos.
        // ADMIN puede cambiar entre PENDIENTE, PENDIENTE_ENVIO, EN_TRANSITO, ENTREGADO y CANCELADO.
        // CLIENTE puede cancelar únicamente sus propios pedidos si no han sido entregados ni cancelados.
        // Cancelar devuelve atómicamente el stock de los productos y deja el pedido en estado final irreversible.
        [HttpPatch("{id}/status")]
        public async Task<ActionResult<OrderDto>> PatchOrderStatus(int id, UpdateOrderStatusDto dto)
        {
            var user = await ResolveCurrentUserAsync();
            if (user == null || user.Role == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado",
                    detail: "Debes autenticarte con una cuenta de usuario válida para modificar el estado de un pedido."
                );
            }

            var targetStatus = NormalizeStatus(dto?.Status);
            if (targetStatus == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Estado inválido",
                    detail: "El estado debe ser uno de: PENDIENTE, PENDIENTE_ENVIO, EN_TRANSITO, ENTREGADO o CANCELADO."
                );
            }

            var role = user.Role.Name.Trim().ToUpperInvariant();
            if (role == Role.EmpleadoName)
            {
                return Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Permisos insuficientes",
                    detail: "No tienes permisos para modificar el estado logístico de los pedidos."
                );
            }

            if (role == Role.ClienteName && targetStatus != StatusCancelado)
            {
                return Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Operación no permitida",
                    detail: "Solo puedes cancelar tus propios pedidos activos."
                );
            }

            if (role != Role.AdminName && role != Role.ClienteName)
            {
                return Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Permisos insuficientes",
                    detail: "No tienes permisos para realizar esta operación."
                );
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Bloqueo exclusivo de fila (FOR UPDATE) para lectura actual en MySQL y prevención de doble reintegro de stock ante concurrencia
                var lockedOrders = await _context.Orders
                    .FromSqlInterpolated($"SELECT * FROM orders WHERE OrderID = {id} FOR UPDATE")
                    .ToListAsync();

                var order = lockedOrders.FirstOrDefault();

                if (order == null)
                {
                    await transaction.RollbackAsync();
                    return Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Orden no encontrada",
                        detail: $"No se encontró ninguna orden con el ID {id}."
                    );
                }

                await _context.Entry(order)
                    .Collection(o => o.OrderDetails)
                    .LoadAsync();

                if (role == Role.ClienteName && order.UserId != user.Id)
                {
                    await transaction.RollbackAsync();
                    return Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Acceso denegado",
                        detail: "No tienes permiso para cancelar un pedido que no pertenece a tu cuenta."
                    );
                }

                var now = DateTime.UtcNow;
                var currentStatus = ResolveEffectiveStatus(order.Status, order.ShippedDate, order.RequiredDate, now);

                if (currentStatus == StatusCancelado)
                {
                    await transaction.RollbackAsync();
                    return Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Pedido cancelado irreversible",
                        detail: "Este pedido ya fue cancelado y se encuentra en un estado final irreversible."
                    );
                }

                if (currentStatus == StatusEntregado && targetStatus == StatusCancelado)
                {
                    await transaction.RollbackAsync();
                    return Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Cancelación no permitida",
                        detail: "Un pedido ya entregado no puede ser cancelado."
                    );
                }

                if (currentStatus == targetStatus)
                {
                    if (string.IsNullOrWhiteSpace(order.Status))
                    {
                        order.Status = currentStatus;
                        await _context.SaveChangesAsync();
                    }
                    await transaction.CommitAsync();
                }
                else
                {
                    if (targetStatus == StatusCancelado)
                    {
                        var quantitiesByProduct = order.OrderDetails
                            .Where(od => od.Quantity > 0)
                            .GroupBy(od => od.ProductId)
                            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                            .OrderBy(x => x.ProductId)
                            .ToList();

                        foreach (var item in quantitiesByProduct)
                        {
                            await _context.Database.ExecuteSqlInterpolatedAsync(
                                $"UPDATE products SET UnitsInStock = LEAST(32767, COALESCE(UnitsInStock, 0) + {item.Quantity}) WHERE ProductID = {item.ProductId}");
                        }

                        order.Status = StatusCancelado;
                    }
                    else
                    {
                        order.Status = targetStatus;

                        if (targetStatus == StatusPendiente || targetStatus == StatusPendienteEnvio)
                        {
                            order.ShippedDate = null;
                        }
                        else if ((targetStatus == StatusEnTransito || targetStatus == StatusEntregado) && order.ShippedDate == null)
                        {
                            order.ShippedDate = now;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                var updatedDto = await ProjectOrderDtos(
                    _context.Orders
                        .AsNoTracking()
                        .Where(o => o.OrderId == id)
                ).FirstAsync();

                FinalizeOrderDto(updatedDto, now);
                return Ok(updatedDto);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al actualizar el estado del pedido",
                    detail: "No fue posible actualizar el estado del pedido. Intenta nuevamente."
                );
            }
        }
    }
}
