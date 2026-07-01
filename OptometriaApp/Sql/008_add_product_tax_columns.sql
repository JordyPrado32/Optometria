IF COL_LENGTH('dbo.tbl_producto', 'tiene_iva') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_producto
    ADD tiene_iva BIT NOT NULL
        CONSTRAINT DF_tbl_producto_tiene_iva DEFAULT (0);
END;

IF COL_LENGTH('dbo.tbl_producto', 'porcentaje_iva') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_producto
    ADD porcentaje_iva DECIMAL(5,2) NOT NULL
        CONSTRAINT DF_tbl_producto_porcentaje_iva DEFAULT (0);
END;

UPDATE dbo.tbl_producto
SET porcentaje_iva = 0
WHERE porcentaje_iva IS NULL;

UPDATE dbo.tbl_producto
SET tiene_iva = CASE WHEN ISNULL(porcentaje_iva, 0) > 0 THEN 1 ELSE 0 END
WHERE tiene_iva IS NULL;
