using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;

namespace OptometriaApp.Api;

public static class ProductApiEndpoints
{
    public static IEndpointRouteBuilder MapProductApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api")
            .WithTags("Integracion movil");

        group.MapGet("/salud", () => Results.Ok(new
            {
                estado = "ok",
                servicio = "OptometriaApp"
            }))
            .WithName("ApiSalud")
            .WithSummary("Comprueba que la aplicacion alojada en AWS esta disponible")
            .Produces(StatusCodes.Status200OK)
            .AllowAnonymous();

        group.MapGet("/productos", ObtenerProductosAsync)
            .WithName("ApiProductos")
            .WithSummary("Consulta productos y servicios activos para la aplicacion movil")
            .Produces<List<ProductoApiResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });

        return endpoints;
    }

    private static async Task<IResult> ObtenerProductosAsync(
        int? limite,
        string? buscar,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cantidad = Math.Clamp(limite ?? 50, 1, 200);
        var termino = buscar?.Trim();

        if (termino?.Length > 80)
        {
            return Results.BadRequest(new { message = "La busqueda no puede superar 80 caracteres." });
        }

        try
        {
            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken);

            var query = dbContext.tbl_productos
                .AsNoTracking()
                .Where(producto => producto.activo == true)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(termino))
            {
                query = query.Where(producto =>
                    producto.nombre_producto.Contains(termino) ||
                    producto.codigo_producto.Contains(termino) ||
                    (producto.tipo_item != null && producto.tipo_item.Contains(termino)));
            }

            var productos = await query
                .OrderBy(producto => producto.nombre_producto)
                .Select(producto => new ProductoApiResponse(
                    producto.id_producto,
                    producto.codigo_producto,
                    producto.nombre_producto,
                    producto.tipo_item,
                    producto.precio_venta,
                    producto.stock_actual))
                .Take(cantidad)
                .ToListAsync(cancellationToken);

            return Results.Ok(productos);
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("ProductApi")
                .LogError(exception, "No se pudo consultar productos desde SQL Server.");

            return Results.Problem(
                title: "No se pudo consultar SQL Server",
                detail: "El servicio de datos no se encuentra disponible. Revisa la conectividad privada desde AWS.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public sealed record ProductoApiResponse(
    int IdProducto,
    string CodigoProducto,
    string NombreProducto,
    string? TipoItem,
    decimal PrecioVenta,
    int? StockActual);
