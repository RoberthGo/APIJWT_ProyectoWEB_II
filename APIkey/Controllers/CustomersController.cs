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
    [RequireRoles(Role.AdminName, Role.EmpleadoName)]
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly ContextDB _context;

        public CustomersController(ContextDB context)
        {
            _context = context;
        }

        private static string ExtractAsciiInitials(string username)
        {
            var decomposed = username.Normalize(NormalizationForm.FormD);
            var cleanBuilder = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    cleanBuilder.Append(char.ToUpperInvariant(ch));
                }
                else
                {
                    cleanBuilder.Append(' ');
                }
            }

            var tokens = cleanBuilder
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 2)
            {
                var sb = new StringBuilder(3);
                foreach (var token in tokens)
                {
                    sb.Append(token[0]);
                    if (sb.Length == 3)
                    {
                        break;
                    }
                }
                return sb.ToString();
            }

            if (tokens.Length == 1)
            {
                return tokens[0].Length >= 2 ? tokens[0][..2] : tokens[0];
            }

            return "CL";
        }

        private static string GenerateDynamicCustomerId(string username, HashSet<string> usedIds)
        {
            var initials = ExtractAsciiInitials(username);
            for (var counter = 1; counter <= 9999; counter++)
            {
                var suffix = counter.ToString(CultureInfo.InvariantCulture);
                var maxPrefixLen = Math.Max(1, 5 - suffix.Length);
                var prefix = initials.Length > maxPrefixLen ? initials[..maxPrefixLen] : initials;
                var candidate = prefix + suffix;

                if (usedIds.Add(candidate))
                {
                    return candidate;
                }
            }

            // Respaldo acotado en base 36 (5 caracteres alfanuméricos) si se agotaran los 9,999 sufijos decimales
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            for (var fallback = 1; fallback <= 60466175; fallback++)
            {
                var value = fallback;
                var chars = new char[5];
                for (var i = 4; i >= 0; i--)
                {
                    chars[i] = alphabet[value % 36];
                    value /= 36;
                }
                var candidate = new string(chars);
                if (usedIds.Add(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("No se pudo generar un identificador único de cliente.");
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

            var usedIds = new HashSet<string>(
                northwindCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerId))
                    .Select(c => c.CustomerId.Trim()),
                StringComparer.OrdinalIgnoreCase
            );

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
                var dynamicId = GenerateDynamicCustomerId(user.Username, usedIds);
                unified.Add(new CustomerDto
                {
                    CustomerId = dynamicId,
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
                    detail: $"No se encontró ningún cliente con el ID '{id}'."
                );
            }

            return Ok(customer);
        }
    }
}
