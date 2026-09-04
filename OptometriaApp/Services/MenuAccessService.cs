using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public sealed class MenuAccessService
{
    private readonly IDbContextFactory<OpticaDbContext> dbContextFactory;
    public event Func<Task>? MenusChanged;

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
                Ruta = m.ruta ?? string.Empty,
                Icono = m.icono,
                Orden = m.orden,
                IdMenuPadre = m.id_menu_padre
            })
            .ToListAsync(cancellationToken);

        if (roleId is null)
        {
            return [];
        }

        if (roleId.Value == 1)
        {
            return activeMenus;
        }

        var configuredPermissions = await dbContext.tbl_rol_menu_permisos
            .AsNoTracking()
            .Where(p => p.id_rol == roleId.Value)
            .ToListAsync(cancellationToken);

        if (configuredPermissions.Count == 0)
        {
            return [];
        }

        var allowedMenuIds = configuredPermissions
            .Where(p => p.puede_ver)
            .Select(p => p.id_menu)
            .ToHashSet();


        var visibleMenus = activeMenus
            .Where(m => allowedMenuIds.Contains(m.IdMenu))
            .ToList();

        var visibleMenuIds = visibleMenus.Select(m => m.IdMenu).ToHashSet();
        var pendingParentIds = visibleMenus
            .Where(m => m.IdMenuPadre.HasValue)
            .Select(m => m.IdMenuPadre!.Value)
            .ToList();

        while (pendingParentIds.Count > 0)
        {
            var parentId = pendingParentIds[0];
            pendingParentIds.RemoveAt(0);

            if (!visibleMenuIds.Contains(parentId))
            {
                var parentMenu = activeMenus.FirstOrDefault(m => m.IdMenu == parentId);
                if (parentMenu is not null)
                {
                    visibleMenus.Add(parentMenu);
                    visibleMenuIds.Add(parentId);

                    if (parentMenu.IdMenuPadre.HasValue)
                    {
                        pendingParentIds.Add(parentMenu.IdMenuPadre.Value);
                    }
                }
            }
        }

        return visibleMenus;
    }

    public async Task NotifyMenusChangedAsync()
    {
        if (MenusChanged is null)
        {
            return;
        }

        foreach (var handler in MenusChanged.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }
}
