using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class MenuAccessService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;

    public MenuAccessService(IDbContextFactory<OpticaDbContext> dbContextFactory)
    {
        this.dbContextFactory = dbContextFactory;
    }

    public async Task<List<AppMenuItem>> GetMenusForRoleAsync(int? roleId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var activeMenus = await dbContext.tbl_menu_apps
            .AsNoTracking()
            .Where(m => m.activo)
            .OrderBy(m => m.orden)
            .Select(m => new AppMenuItem
            {
                IdMenu = m.id_menu,
                Nombre = m.nombre,
                Ruta = m.ruta,
                Icono = m.icono,
                Orden = m.orden
            })
            .ToListAsync(cancellationToken);

        if (roleId is null)
        {
            return activeMenus;
        }

        var configuredPermissions = await dbContext.tbl_rol_menu_permisos
            .AsNoTracking()
            .Where(p => p.id_rol == roleId.Value)
            .ToListAsync(cancellationToken);

        if (configuredPermissions.Count == 0)
        {
            return activeMenus;
        }

        var allowedMenuIds = configuredPermissions
            .Where(p => p.puede_ver)
            .Select(p => p.id_menu)
            .ToHashSet();

        return activeMenus.Where(m => allowedMenuIds.Contains(m.IdMenu)).ToList();
    }
}
