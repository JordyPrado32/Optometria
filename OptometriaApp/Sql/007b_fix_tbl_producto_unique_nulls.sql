USE bd_optica_modelo_estrella;
GO

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* =========================================================
       Detecta y elimina UQ viejas sobre columnas NULLables
       de tbl_producto, excepto codigo_producto.
       Esto corrige el error:
       duplicate key value is (<NULL>)
       ========================================================= */

    DECLARE @sql NVARCHAR(MAX) = N'';

    ;WITH UniqueConstraints AS
    (
        SELECT
            kc.name AS constraint_name,
            c.name AS column_name
        FROM sys.key_constraints kc
        INNER JOIN sys.tables t
            ON t.object_id = kc.parent_object_id
        INNER JOIN sys.index_columns ic
            ON ic.object_id = kc.parent_object_id
           AND ic.index_id = kc.unique_index_id
        INNER JOIN sys.columns c
            ON c.object_id = ic.object_id
           AND c.column_id = ic.column_id
        WHERE kc.[type] = 'UQ'
          AND t.name = 'tbl_producto'
          AND c.name <> 'codigo_producto'
          AND c.is_nullable = 1
    )
    SELECT @sql = @sql + N'
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = ''' + constraint_name + N''')
BEGIN
    ALTER TABLE dbo.tbl_producto DROP CONSTRAINT [' + constraint_name + N'];
END;
'
    FROM UniqueConstraints;

    ;WITH UniqueIndexes AS
    (
        SELECT
            i.name AS index_name,
            c.name AS column_name
        FROM sys.indexes i
        INNER JOIN sys.tables t
            ON t.object_id = i.object_id
        INNER JOIN sys.index_columns ic
            ON ic.object_id = i.object_id
           AND ic.index_id = i.index_id
        INNER JOIN sys.columns c
            ON c.object_id = ic.object_id
           AND c.column_id = ic.column_id
        WHERE t.name = 'tbl_producto'
          AND i.is_unique = 1
          AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0
          AND c.name <> 'codigo_producto'
          AND c.is_nullable = 1
          AND (i.filter_definition IS NULL OR i.filter_definition NOT LIKE '%IS NOT NULL%')
    )
    SELECT @sql = @sql + N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = ''' + index_name + N''' AND object_id = OBJECT_ID(''dbo.tbl_producto''))
BEGIN
    DROP INDEX [' + index_name + N'] ON dbo.tbl_producto;
END;
'
    FROM UniqueIndexes;

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* =========================================================
       Recrea unicidad correcta solo cuando hay valor real
       ========================================================= */

    IF COL_LENGTH('dbo.tbl_producto', 'codigo_barras') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_producto_codigo_barras' AND object_id = OBJECT_ID('dbo.tbl_producto'))
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_producto_codigo_barras
            ON dbo.tbl_producto (codigo_barras)
            WHERE codigo_barras IS NOT NULL;
    END;

    IF COL_LENGTH('dbo.tbl_producto', 'sku_alterno') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_producto_sku_alterno' AND object_id = OBJECT_ID('dbo.tbl_producto'))
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_producto_sku_alterno
            ON dbo.tbl_producto (sku_alterno)
            WHERE sku_alterno IS NOT NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
