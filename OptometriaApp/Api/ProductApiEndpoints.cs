using Microsoft.AspNetCore.Authorization;
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
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> ObtenerProductosAsync(
        int? limite,
        IDbContextFactory<OpticaDbContext> dbContextFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var cantidad = Math.Clamp(limite ?? 50, 1, 200);

        try
        {
            await using var dbContext = await dbContextFactory
                .CreateDbContextAsync(cancellationToken);

            var productos = await dbContext.tbl_productos
                .AsNoTracking()
                .Where(producto => producto.activo == true)
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
                detail: "Comprueba la VPN, el reenvio del puerto, las credenciales y los permisos del usuario SQL.",
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
