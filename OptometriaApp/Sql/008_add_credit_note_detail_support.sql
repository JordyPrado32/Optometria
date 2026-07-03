SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.tbl_nota_credito', 'tipo_nota') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_nota_credito
    ADD tipo_nota VARCHAR(20) NOT NULL
        CONSTRAINT DF_tbl_nota_credito_tipo_nota DEFAULT ('Total');
END;
GO

IF OBJECT_ID('dbo.tbl_detalle_nota_credito', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_detalle_nota_credito
    (
        id_detalle_nota_credito INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        id_nota_credito INT NOT NULL,
        id_detalle_venta INT NOT NULL,
        cantidad_acreditada INT NULL,
        monto_subtotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_detalle_nota_credito_subtotal DEFAULT (0),
        monto_impuesto DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_detalle_nota_credito_impuesto DEFAULT (0),
        monto_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_detalle_nota_credito_total DEFAULT (0),
        porcentaje_impuesto DECIMAL(5,2) NULL,
        descripcion_item VARCHAR(255) NULL,
        fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_detalle_nota_credito_fecha_creacion DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_tbl_detalle_nota_credito_tbl_nota_credito')
BEGIN
    ALTER TABLE dbo.tbl_detalle_nota_credito
    ADD CONSTRAINT FK_tbl_detalle_nota_credito_tbl_nota_credito
        FOREIGN KEY (id_nota_credito) REFERENCES dbo.tbl_nota_credito(id_nota_credito)
        ON DELETE CASCADE;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_tbl_detalle_nota_credito_tbl_detalle_venta')
BEGIN
    ALTER TABLE dbo.tbl_detalle_nota_credito
    ADD CONSTRAINT FK_tbl_detalle_nota_credito_tbl_detalle_venta
        FOREIGN KEY (id_detalle_venta) REFERENCES dbo.tbl_detalle_venta(id_detalle_venta);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_detalle_nota_credito_relacion'
      AND object_id = OBJECT_ID('dbo.tbl_detalle_nota_credito'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_detalle_nota_credito_relacion
        ON dbo.tbl_detalle_nota_credito(id_nota_credito, id_detalle_venta);
END;
GO
