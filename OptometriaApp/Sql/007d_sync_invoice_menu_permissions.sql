USE bd_optica_modelo_estrella;
GO

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.tbl_menu_app', 'U') IS NOT NULL
    BEGIN
        MERGE dbo.tbl_menu_app AS target
        USING
        (
            VALUES
                ('Facturas', '/invoices', 'invoice', 21, 1),
                ('Mis facturas', '/my-invoices', 'receipt', 22, 1),
                ('Mis notas de credito', '/my-credit-notes', 'arrow-counterclockwise', 23, 1),
                ('Cuentas por cobrar', '/accounts-receivable', 'cash-coin', 24, 1)
        ) AS source(nombre, ruta, icono, orden, activo)
        ON target.ruta = source.ruta
        WHEN MATCHED THEN
            UPDATE SET
                target.nombre = source.nombre,
                target.icono = source.icono,
                target.orden = source.orden,
                target.activo = source.activo
        WHEN NOT MATCHED THEN
            INSERT (nombre, ruta, icono, orden, activo)
            VALUES (source.nombre, source.ruta, source.icono, source.orden, source.activo);
    END;

    IF OBJECT_ID('dbo.tbl_rol_menu_permiso', 'U') IS NOT NULL
       AND OBJECT_ID('dbo.tbl_menu_app', 'U') IS NOT NULL
    BEGIN
        IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 1)
        BEGIN
            MERGE dbo.tbl_rol_menu_permiso AS target
            USING
            (
                SELECT 1 AS id_rol, id_menu,
                       CAST(1 AS BIT) AS puede_ver,
                       CAST(1 AS BIT) AS puede_crear,
                       CAST(1 AS BIT) AS puede_editar,
                       CAST(1 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app
                WHERE ruta IN ('/invoices', '/my-invoices', '/my-credit-notes', '/accounts-receivable')
            ) AS source
            ON target.id_rol = source.id_rol AND target.id_menu = source.id_menu
            WHEN MATCHED THEN
                UPDATE SET
                    target.puede_ver = source.puede_ver,
                    target.puede_crear = source.puede_crear,
                    target.puede_editar = source.puede_editar,
                    target.puede_eliminar = source.puede_eliminar
            WHEN NOT MATCHED THEN
                INSERT (id_rol, id_menu, puede_ver, puede_crear, puede_editar, puede_eliminar)
                VALUES (source.id_rol, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);
        END;

        IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 2)
        BEGIN
            MERGE dbo.tbl_rol_menu_permiso AS target
            USING
            (
                SELECT 2 AS id_rol, id_menu,
                       CAST(1 AS BIT) AS puede_ver,
                       CAST(1 AS BIT) AS puede_crear,
                       CAST(1 AS BIT) AS puede_editar,
                       CAST(0 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app
                WHERE ruta IN ('/invoices', '/my-invoices', '/my-credit-notes', '/accounts-receivable')
            ) AS source
            ON target.id_rol = source.id_rol AND target.id_menu = source.id_menu
            WHEN MATCHED THEN
                UPDATE SET
                    target.puede_ver = source.puede_ver,
                    target.puede_crear = source.puede_crear,
                    target.puede_editar = source.puede_editar,
                    target.puede_eliminar = source.puede_eliminar
            WHEN NOT MATCHED THEN
                INSERT (id_rol, id_menu, puede_ver, puede_crear, puede_editar, puede_eliminar)
                VALUES (source.id_rol, source.id_menu, source.puede_ver, source.puede_crear, source.puede_editar, source.puede_eliminar);
        END;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
