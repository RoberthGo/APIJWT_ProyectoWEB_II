using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APIkey.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRolesAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    private readonly HashSet<string> _allowedRoles;

    public int Order => 1;

    public RequireRolesAttribute(params string[] allowedRoles)
    {
        _allowedRoles = new HashSet<string>(
            allowedRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim().ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsOptions(context.HttpContext.Request.Method))
        {
            return Task.CompletedTask;
        }

        var currentRole = context.HttpContext.Items["UserRole"] as string;
        if (string.IsNullOrWhiteSpace(currentRole))
        {
            var unauthorized = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autorizado",
                Detail = "No se pudo verificar la identidad del usuario.",
                Instance = context.HttpContext.Request.Path
            };

            context.Result = new ObjectResult(unauthorized)
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                ContentTypes = { "application/problem+json" }
            };
            return Task.CompletedTask;
        }

        if (_allowedRoles.Count > 0 && !_allowedRoles.Contains(currentRole.Trim()))
        {
            var forbidden = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Acceso denegado",
                Detail = "No tienes permisos suficientes para realizar esta operación.",
                Instance = context.HttpContext.Request.Path
            };

            context.Result = new ObjectResult(forbidden)
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { "application/problem+json" }
            };
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
