USE bd_optica_modelo_estrella;
GO

IF COL_LENGTH('dbo.tbl_producto', 'almacen') IS NULL ALTER TABLE dbo.tbl_producto ADD almacen VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'pasillo') IS NULL ALTER TABLE dbo.tbl_producto ADD pasillo VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'estante') IS NULL ALTER TABLE dbo.tbl_producto ADD estante VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'nivel') IS NULL ALTER TABLE dbo.tbl_producto ADD nivel VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'stock_maximo') IS NULL ALTER TABLE dbo.tbl_producto ADD stock_maximo INT NOT NULL CONSTRAINT DF_tbl_producto_stock_maximo DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'punto_reorden') IS NULL ALTER TABLE dbo.tbl_producto ADD punto_reorden INT NOT NULL CONSTRAINT DF_tbl_producto_punto_reorden DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'cantidad_empaque') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_empaque INT NOT NULL CONSTRAINT DF_tbl_producto_cantidad_empaque DEFAULT (1);
IF COL_LENGTH('dbo.tbl_producto', 'peso_unitario') IS NULL ALTER TABLE dbo.tbl_producto ADD peso_unitario DECIMAL(10,4) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_largo') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_largo DECIMAL(10,4) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_ancho') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_ancho DECIMAL(10,4) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'dimensiones_alto') IS NULL ALTER TABLE dbo.tbl_producto ADD dimensiones_alto DECIMAL(10,4) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'volumen_m3') IS NULL ALTER TABLE dbo.tbl_producto ADD volumen_m3 DECIMAL(15,8) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'requiere_lote') IS NULL ALTER TABLE dbo.tbl_producto ADD requiere_lote BIT NOT NULL CONSTRAINT DF_tbl_producto_requiere_lote DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'requiere_fecha_vencimiento') IS NULL ALTER TABLE dbo.tbl_producto ADD requiere_fecha_vencimiento BIT NOT NULL CONSTRAINT DF_tbl_producto_requiere_fecha_vencimiento DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'dias_vencimiento') IS NULL ALTER TABLE dbo.tbl_producto ADD dias_vencimiento INT NULL;
IF COL_LENGTH('dbo.tbl_producto', 'cuenta_contable') IS NULL ALTER TABLE dbo.tbl_producto ADD cuenta_contable VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'centro_costo') IS NULL ALTER TABLE dbo.tbl_producto ADD centro_costo VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'naturaleza_item') IS NULL ALTER TABLE dbo.tbl_producto ADD naturaleza_item VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'porcentaje_margen') IS NULL ALTER TABLE dbo.tbl_producto ADD porcentaje_margen DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'descuento_mayorista') IS NULL ALTER TABLE dbo.tbl_producto ADD descuento_mayorista DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'descuento_cliente_fijo') IS NULL ALTER TABLE dbo.tbl_producto ADD descuento_cliente_fijo DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'movimiento_frecuencia') IS NULL ALTER TABLE dbo.tbl_producto ADD movimiento_frecuencia VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'dias_rotacion_promedio') IS NULL ALTER TABLE dbo.tbl_producto ADD dias_rotacion_promedio INT NULL;
IF COL_LENGTH('dbo.tbl_producto', 'fecha_ultima_compra') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_ultima_compra DATETIME NULL;
IF COL_LENGTH('dbo.tbl_producto', 'fecha_ultima_venta') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_ultima_venta DATETIME NULL;
IF COL_LENGTH('dbo.tbl_producto', 'fecha_actualizacion_precio') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_actualizacion_precio DATETIME NULL;
IF COL_LENGTH('dbo.tbl_producto', 'id_usuario_actualizacion') IS NULL ALTER TABLE dbo.tbl_producto ADD id_usuario_actualizacion INT NULL;
IF COL_LENGTH('dbo.tbl_producto', 'cantidad_movimientos_mes') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_movimientos_mes INT NOT NULL CONSTRAINT DF_tbl_producto_cantidad_movimientos_mes DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'etiquetas') IS NULL ALTER TABLE dbo.tbl_producto ADD etiquetas VARCHAR(500) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'marca') IS NULL ALTER TABLE dbo.tbl_producto ADD marca VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'modelo') IS NULL ALTER TABLE dbo.tbl_producto ADD modelo VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'color') IS NULL ALTER TABLE dbo.tbl_producto ADD color VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'talla') IS NULL ALTER TABLE dbo.tbl_producto ADD talla VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'es_promocion') IS NULL ALTER TABLE dbo.tbl_producto ADD es_promocion BIT NOT NULL CONSTRAINT DF_tbl_producto_es_promocion DEFAULT (0);
IF COL_LENGTH('dbo.tbl_producto', 'porcentaje_descuento_promo') IS NULL ALTER TABLE dbo.tbl_producto ADD porcentaje_descuento_promo DECIMAL(5,2) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'material_principal') IS NULL ALTER TABLE dbo.tbl_producto ADD material_principal VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'tratamiento_lente') IS NULL ALTER TABLE dbo.tbl_producto ADD tratamiento_lente VARCHAR(200) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'estado_producto') IS NULL ALTER TABLE dbo.tbl_producto ADD estado_producto VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_producto_estado_producto DEFAULT ('Disponible');
IF COL_LENGTH('dbo.tbl_producto', 'motivo_estado') IS NULL ALTER TABLE dbo.tbl_producto ADD motivo_estado VARCHAR(300) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'codigo_barras') IS NULL ALTER TABLE dbo.tbl_producto ADD codigo_barras VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'sku_alterno') IS NULL ALTER TABLE dbo.tbl_producto ADD sku_alterno VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'referencia_fabricante') IS NULL ALTER TABLE dbo.tbl_producto ADD referencia_fabricante VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'proveedor_preferente') IS NULL ALTER TABLE dbo.tbl_producto ADD proveedor_preferente VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'tiempo_entrega_dias') IS NULL ALTER TABLE dbo.tbl_producto ADD tiempo_entrega_dias INT NULL;
IF COL_LENGTH('dbo.tbl_producto', 'cantidad_pedido_optima') IS NULL ALTER TABLE dbo.tbl_producto ADD cantidad_pedido_optima INT NULL;
IF COL_LENGTH('dbo.tbl_producto', 'fecha_creacion') IS NULL ALTER TABLE dbo.tbl_producto ADD fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_producto_fecha_creacion DEFAULT (GETDATE());
IF COL_LENGTH('dbo.tbl_producto', 'usuario_creacion') IS NULL ALTER TABLE dbo.tbl_producto ADD usuario_creacion VARCHAR(100) NULL;
IF COL_LENGTH('dbo.tbl_producto', 'notas_internas') IS NULL ALTER TABLE dbo.tbl_producto ADD notas_internas VARCHAR(MAX) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_producto_usuario_actualizacion')
BEGIN
    ALTER TABLE dbo.tbl_producto
    ADD CONSTRAINT FK_tbl_producto_usuario_actualizacion
        FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_producto_codigo_barras' AND object_id = OBJECT_ID('dbo.tbl_producto'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_producto_codigo_barras
    ON dbo.tbl_producto (codigo_barras)
    WHERE codigo_barras IS NOT NULL;
END;
GO
