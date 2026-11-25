using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Models;

namespace APIkey.Controllers
{
    [EnableCors("AllowAll")]
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly ContextDB _context;

        public ProductsController(ContextDB context)
        {
            _context = context;
        }

        // GET: api/Products
        // Retorna la cantidad total de productos y solo nombre, precio y stock de cada uno
        [HttpGet]
        public async Task<ActionResult<ProductsSummaryDto>> GetProducts()
        {
            var total = await _context.Products.CountAsync();
            var products = await _context.Products
                .AsNoTracking()
                .Select(p => new ProductDto
                {
                    ProductId = p.ProductId,
                    ProductName = p.ProductName,
                    UnitPrice = p.UnitPrice,
                    UnitsInStock = p.UnitsInStock
                })
                .ToListAsync();

            return Ok(new ProductsSummaryDto
            {
                TotalProducts = total,
                Products = products
            });
        }

        // GET: api/Products/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProductId == id)
                .Select(p => new ProductDto
                {
                    ProductId = p.ProductId,
                    ProductName = p.ProductName,
                    UnitPrice = p.UnitPrice,
                    UnitsInStock = p.UnitsInStock
                })
                .FirstOrDefaultAsync();

            if (product == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Producto no encontrado",
                    detail: $"No se encontró ningún producto con el identificador {id}."
                );
            }

            return Ok(product);
        }

        // POST: api/Products
        // Solo ADMIN (validado en BD) puede crear productos
        [HttpPost]
        [Authorize(Roles = Role.AdminName)]
        public async Task<ActionResult<ProductDto>> PostProduct(ProductUpsertDto dto)
        {
            try
            {
                var product = new Product
                {
                    ProductName = dto.ProductName,
                    UnitPrice = dto.UnitPrice,
                    UnitsInStock = dto.UnitsInStock,
                    Discontinued = dto.Discontinued
                };

                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                var responseDto = new ProductDto
                {
                    ProductId = product.ProductId,
                    ProductName = product.ProductName,
                    UnitPrice = product.UnitPrice,
                    UnitsInStock = product.UnitsInStock
                };

                return CreatedAtAction(nameof(GetProduct), new { id = product.ProductId }, responseDto);
            }
            catch (DbUpdateException)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al registrar producto",
                    detail: "No fue posible registrar el producto. Verifica los datos ingresados."
                );
            }
        }

        // PUT: api/Products/5
        // ADMIN puede actualizar todos los campos; EMPLEADO solo precio y existencias (validado en BD)
        [HttpPut("{id}")]
        [Authorize(Roles = Role.AdminName + "," + Role.EmpleadoName)]
        public async Task<IActionResult> PutProduct(int id, ProductUpsertDto dto)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Producto no encontrado",
                    detail: $"No se encontró ningún producto con el identificador {id} para actualizar."
                );
            }

            if (User.IsInRole(Role.AdminName))
            {
                product.ProductName = dto.ProductName;
                product.Discontinued = dto.Discontinued;
            }

            product.UnitPrice = dto.UnitPrice;
            product.UnitsInStock = dto.UnitsInStock;

            try
            {
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Error al actualizar producto",
                    detail: "No fue posible actualizar el producto. Verifica los datos ingresados."
                );
            }
        }

        // DELETE: api/Products/5
        // Solo ADMIN (validado en BD) puede eliminar productos
        [HttpDelete("{id}")]
        [Authorize(Roles = Role.AdminName)]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Producto no encontrado",
                    detail: $"No se encontró ningún producto con el identificador {id} para eliminar."
                );
            }

            try
            {
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflicto de integridad referencial",
                    detail: $"El producto con ID {id} no puede ser eliminado porque tiene órdenes u otros registros vinculados."
                );
            }
        }
    }
}
