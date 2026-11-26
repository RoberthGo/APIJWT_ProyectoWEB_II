using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using APIkey.Data;
using APIkey.DTOs;
using APIkey.Models;

namespace APIkey.Controllers
{
    [Authorize(Roles = Role.AdminName + "," + Role.EmpleadoName)]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly ContextDB _context;

        public CustomersController(ContextDB context)
        {
            _context = context;
        }

        private async Task<List<CustomerDto>> BuildUnifiedCustomersAsync()
        {
            var northwindCustomers = await _context.Customers
                .AsNoTracking()
                .Select(c => new CustomerDto
                {
                    CustomerId = c.CustomerId,
                    CompanyName = c.CompanyName,
                    ContactName = c.ContactName
                })
                .ToListAsync();

            // Filtrar usuarios activos exclusivamente por su rol CLIENTE en base de datos
            var activeClientUsers = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.IsActive && u.Role != null && u.Role.Name == Role.ClienteName)
                .OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.Username, u.Email })
                .ToListAsync();

            var unified = new List<CustomerDto>(northwindCustomers.Count + activeClientUsers.Count);
            unified.AddRange(northwindCustomers);

            foreach (var user in activeClientUsers)
            {
                unified.Add(new CustomerDto
                {
                    CustomerId = $"USR-{user.Id}",
                    CompanyName = user.Email,
                    ContactName = user.Username
                });
            }

            return unified;
        }

        // GET: api/customers
        // Retorna clientes de Northwind y usuarios registrados con rol CLIENTE en BD en una sola lista unificada
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CustomerDto>>> GetCustomers()
        {
            var customers = await BuildUnifiedCustomersAsync();
            return Ok(customers);
        }

        // GET: api/customers/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<CustomerDto>> GetCustomer(string id)
        {
            var cleanId = (id ?? string.Empty).Trim();
            var unified = await BuildUnifiedCustomersAsync();
            var customer = unified.FirstOrDefault(c =>
                string.Equals(c.CustomerId?.Trim(), cleanId, StringComparison.OrdinalIgnoreCase));

            if (customer == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Cliente no encontrado",
                    detail: "No se encontró el cliente solicitado."
                );
            }

            return Ok(customer);
        }
    }
}
