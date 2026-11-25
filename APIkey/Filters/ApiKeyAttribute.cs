using System;
using System.Linq;
using System.Threading.Tasks;
using APIkey.Data;
using APIkey.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace APIkey.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    private const string API_KEY_HEADER_NAME = "X-API-KEY";

    public int Order => 0;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Permitir solicitudes preflight de CORS
        if (HttpMethods.IsOptions(context.HttpContext.Request.Method))
        {
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(API_KEY_HEADER_NAME, out var extractedApiKey) ||
            string.IsNullOrWhiteSpace(extractedApiKey))
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autorizado",
                Detail = "No se proporcionó la API Key en la cabecera 'X-API-KEY'.",
                Instance = context.HttpContext.Request.Path
            };

            context.Result = new ObjectResult(problemDetails)
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }

        var providedApiKey = extractedApiKey.ToString().Trim();
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<ContextDB>();
        var providedApiKeyHash = SecurityHelper.HashApiKey(providedApiKey);
        var utcNow = DateTime.UtcNow;

        var matchedKey = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == providedApiKeyHash &&
                        k.IsActive &&
                        (k.ExpiresAt == null || k.ExpiresAt > utcNow))
            .Select(k => new { k.ClientIdentifier })
            .FirstOrDefaultAsync();

        if (matchedKey == null)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autorizado",
                Detail = "La API Key proporcionada no es válida o está expirada.",
                Instance = context.HttpContext.Request.Path
            };

            context.Result = new ObjectResult(problemDetails)
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }

        var normalizedClient = matchedKey.ClientIdentifier.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedClient && u.IsActive);

        if (user == null || user.Role == null)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autorizado",
                Detail = "La cuenta asociada a la API Key no existe o se encuentra inactiva.",
                Instance = context.HttpContext.Request.Path
            };

            context.Result = new ObjectResult(problemDetails)
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" }
            };
            return;
        }

        context.HttpContext.Items["ClientIdentifier"] = user.Username;
        context.HttpContext.Items["UserId"] = user.Id;
        context.HttpContext.Items["UserRole"] = user.Role.Name;
    }
}
