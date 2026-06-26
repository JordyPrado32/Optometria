USE bd_optica_modelo_estrella;
GO

IF COL_LENGTH('dbo.tbl_proveedor', 'ruc') IS NULL ALTER TABLE dbo.tbl_proveedor ADD ruc VARCHAR(13) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'razon_social') IS NULL ALTER TABLE dbo.tbl_proveedor ADD razon_social VARCHAR(300) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'nombre_comercial') IS NULL ALTER TABLE dbo.tbl_proveedor ADD nombre_comercial VARCHAR(300) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'tipo_identificacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tipo_identificacion VARCHAR(2) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'ciudad') IS NULL ALTER TABLE dbo.tbl_proveedor ADD ciudad VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'provincia') IS NULL ALTER TABLE dbo.tbl_proveedor ADD provincia VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'codigo_postal') IS NULL ALTER TABLE dbo.tbl_proveedor ADD codigo_postal VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_nombre') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_nombre VARCHAR(200) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_telefono') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_telefono VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'contacto_correo') IS NULL ALTER TABLE dbo.tbl_proveedor ADD contacto_correo VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'dias_credito_promedio') IS NULL ALTER TABLE dbo.tbl_proveedor ADD dias_credito_promedio INT NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'saldo_pendiente') IS NULL ALTER TABLE dbo.tbl_proveedor ADD saldo_pendiente DECIMAL(15,2) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'limite_credito') IS NULL ALTER TABLE dbo.tbl_proveedor ADD limite_credito DECIMAL(15,2) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'condicion_pago') IS NULL ALTER TABLE dbo.tbl_proveedor ADD condicion_pago VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'banco_nombre') IS NULL ALTER TABLE dbo.tbl_proveedor ADD banco_nombre VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'cuenta_bancaria') IS NULL ALTER TABLE dbo.tbl_proveedor ADD cuenta_bancaria VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'tipo_cuenta') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tipo_cuenta VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'calificacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD calificacion VARCHAR(1) NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'tiempo_entrega_promedio') IS NULL ALTER TABLE dbo.tbl_proveedor ADD tiempo_entrega_promedio INT NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'es_activo') IS NULL ALTER TABLE dbo.tbl_proveedor ADD es_activo BIT NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'fecha_registro') IS NULL ALTER TABLE dbo.tbl_proveedor ADD fecha_registro DATETIME NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'fecha_actualizacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD fecha_actualizacion DATETIME NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'id_usuario_registro') IS NULL ALTER TABLE dbo.tbl_proveedor ADD id_usuario_registro INT NULL;
IF COL_LENGTH('dbo.tbl_proveedor', 'id_usuario_actualizacion') IS NULL ALTER TABLE dbo.tbl_proveedor ADD id_usuario_actualizacion INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_proveedor_usuario_registro')
BEGIN
    ALTER TABLE dbo.tbl_proveedor
    ADD CONSTRAINT FK_tbl_proveedor_usuario_registro
        FOREIGN KEY (id_usuario_registro) REFERENCES dbo.tbl_usuario(id_usuario);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_proveedor_usuario_actualizacion')
BEGIN
    ALTER TABLE dbo.tbl_proveedor
    ADD CONSTRAINT FK_tbl_proveedor_usuario_actualizacion
        FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario);
END;
GO
