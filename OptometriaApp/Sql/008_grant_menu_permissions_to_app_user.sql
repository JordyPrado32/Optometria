USE bd_optica_modelo_estrella;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DATABASE_PRINCIPAL_ID(N'api_integrador') IS NULL
BEGIN
    IF SUSER_ID(N'api_integrador') IS NULL
        THROW 51000, 'No existe el login api_integrador en SQL Server.', 1;

    CREATE USER [api_integrador] FOR LOGIN [api_integrador];
END;

IF OBJECT_ID(N'dbo.tbl_menu_app', N'U') IS NULL
    THROW 51001, 'No existe dbo.tbl_menu_app.', 1;

IF OBJECT_ID(N'dbo.tbl_rol_menu_permiso', N'U') IS NULL
    THROW 51002, 'No existe dbo.tbl_rol_menu_permiso.', 1;

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.tbl_menu_app TO [api_integrador];
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.tbl_rol_menu_permiso TO [api_integrador];
GO

DECLARE
    @menuInsert INT,
    @menuUpdate INT,
    @permissionInsert INT,
    @permissionUpdate INT;

EXECUTE AS USER = N'api_integrador';

SELECT
    @menuInsert = HAS_PERMS_BY_NAME(N'dbo.tbl_menu_app', N'OBJECT', N'INSERT'),
    @menuUpdate = HAS_PERMS_BY_NAME(N'dbo.tbl_menu_app', N'OBJECT', N'UPDATE'),
    @permissionInsert = HAS_PERMS_BY_NAME(N'dbo.tbl_rol_menu_permiso', N'OBJECT', N'INSERT'),
    @permissionUpdate = HAS_PERMS_BY_NAME(N'dbo.tbl_rol_menu_permiso', N'OBJECT', N'UPDATE');

REVERT;

IF @menuInsert <> 1 OR @menuUpdate <> 1
   OR @permissionInsert <> 1 OR @permissionUpdate <> 1
    THROW 51003, 'No fue posible conceder todos los permisos requeridos.', 1;

SELECT N'Permisos de menus concedidos correctamente a api_integrador.' AS resultado;
GO
