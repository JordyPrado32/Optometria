USE bd_optica_modelo_estrella;
GO

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* =========================================
       PREVIO: columnas que el script grande usa
       ========================================= */

    IF COL_LENGTH('dbo.tbl_abono', 'id_cta_cobrar') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD id_cta_cobrar INT NULL;';

    IF COL_LENGTH('dbo.tbl_abono', 'monto_abono') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD monto_abono DECIMAL(18,2) NULL;';

    IF COL_LENGTH('dbo.tbl_abono', 'metodo_pago_id') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD metodo_pago_id INT NULL;';

    IF COL_LENGTH('dbo.tbl_abono', 'referencia_pago') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD referencia_pago VARCHAR(100) NULL;';

    IF COL_LENGTH('dbo.tbl_abono', 'usuario_registro') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD usuario_registro VARCHAR(100) NULL;';

    IF COL_LENGTH('dbo.tbl_abono', 'fecha_registro') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_abono ADD fecha_registro DATETIME2 NULL;';

    IF COL_LENGTH('dbo.tbl_venta', 'id_cliente_facturacion') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_venta ADD id_cliente_facturacion INT NULL;';

    IF COL_LENGTH('dbo.tbl_venta', 'porcentaje_impuesto') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_venta ADD porcentaje_impuesto DECIMAL(5,2) NULL;';

    IF COL_LENGTH('dbo.tbl_venta', 'forma_pago') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_venta ADD forma_pago VARCHAR(50) NULL;';

    IF COL_LENGTH('dbo.tbl_venta', 'dias_credito') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_venta ADD dias_credito INT NULL;';

    IF COL_LENGTH('dbo.tbl_venta', 'observaciones_factura') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_venta ADD observaciones_factura VARCHAR(MAX) NULL;';

    IF COL_LENGTH('dbo.tbl_detalle_venta', 'origen_tipo') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_detalle_venta ADD origen_tipo VARCHAR(40) NULL;';

    IF COL_LENGTH('dbo.tbl_detalle_venta', 'origen_id') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.tbl_detalle_venta ADD origen_id INT NULL;';

    /* =========================================
       Copias desde columnas viejas si existen
       ========================================= */

    IF COL_LENGTH('dbo.tbl_abono', 'monto') IS NOT NULL
       AND COL_LENGTH('dbo.tbl_abono', 'monto_abono') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.tbl_abono
            SET monto_abono = ISNULL(monto, 0)
            WHERE monto_abono IS NULL;
        ';
    END;

    IF COL_LENGTH('dbo.tbl_abono', 'id_metodo_pago') IS NOT NULL
       AND COL_LENGTH('dbo.tbl_abono', 'metodo_pago_id') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.tbl_abono
            SET metodo_pago_id = id_metodo_pago
            WHERE metodo_pago_id IS NULL;
        ';
    END;

    IF COL_LENGTH('dbo.tbl_abono', 'id_usuario') IS NOT NULL
       AND COL_LENGTH('dbo.tbl_abono', 'usuario_registro') IS NOT NULL
       AND OBJECT_ID('dbo.tbl_usuario', 'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE a
            SET usuario_registro = COALESCE(u.usuario, CONCAT(''USER_'', a.id_usuario))
            FROM dbo.tbl_abono a
            LEFT JOIN dbo.tbl_usuario u ON u.id_usuario = a.id_usuario
            WHERE a.usuario_registro IS NULL;
        ';
    END;

    IF COL_LENGTH('dbo.tbl_venta', 'porcentaje_impuesto') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.tbl_venta
            SET porcentaje_impuesto = 0
            WHERE porcentaje_impuesto IS NULL;
        ';
    END;

    IF COL_LENGTH('dbo.tbl_venta', 'forma_pago') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.tbl_venta
            SET forma_pago = ''Efectivo''
            WHERE forma_pago IS NULL;
        ';
    END;

    IF COL_LENGTH('dbo.tbl_venta', 'dias_credito') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.tbl_venta
            SET dias_credito = 0
            WHERE dias_credito IS NULL;
        ';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
