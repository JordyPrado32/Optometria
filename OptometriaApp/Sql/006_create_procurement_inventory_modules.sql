USE bd_optica_modelo_estrella;
GO

IF OBJECT_ID('dbo.tbl_lote_producto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_lote_producto
    (
        id_lote INT IDENTITY(1,1) PRIMARY KEY,
        id_producto INT NOT NULL,
        numero_lote VARCHAR(50) NOT NULL,
        numero_serie VARCHAR(50) NULL,
        id_orden_compra INT NULL,
        cantidad_inicial INT NOT NULL,
        cantidad_disponible INT NOT NULL,
        cantidad_vendida INT DEFAULT 0,
        cantidad_devuelta INT DEFAULT 0,
        cantidad_merma INT DEFAULT 0,
        fecha_fabricacion DATE NULL,
        fecha_vencimiento DATE NULL,
        costo_unitario DECIMAL(15,2) NULL,
        precio_venta_unitario DECIMAL(15,2) NULL,
        valor_total_costo DECIMAL(15,2) NULL,
        estado_lote VARCHAR(30) DEFAULT 'Disponible',
        almacen VARCHAR(50) NULL,
        pasillo VARCHAR(10) NULL,
        estante VARCHAR(10) NULL,
        nivel VARCHAR(10) NULL,
        fecha_ingreso DATETIME DEFAULT GETDATE(),
        fecha_ultima_salida DATETIME NULL,
        id_usuario_ingreso INT NULL,
        observaciones VARCHAR(500) NULL,
        CONSTRAINT UQ_lote_numero UNIQUE (numero_lote, id_producto)
    );
END;
GO

IF OBJECT_ID('dbo.tbl_orden_compra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_orden_compra
    (
        id_orden_compra INT IDENTITY(1,1) PRIMARY KEY,
        numero_orden VARCHAR(20) NOT NULL UNIQUE,
        id_proveedor INT NOT NULL,
        id_usuario_solicita INT NOT NULL,
        id_usuario_autoriza INT NULL,
        fecha_orden DATETIME DEFAULT GETDATE(),
        fecha_requerida DATE NULL,
        fecha_recepcion_esperada DATE NULL,
        fecha_recepcion_real DATETIME NULL,
        subtotal DECIMAL(15,2) DEFAULT 0,
        descuento_general DECIMAL(15,2) DEFAULT 0,
        impuesto_total DECIMAL(15,2) DEFAULT 0,
        total DECIMAL(15,2) DEFAULT 0,
        condicion_pago VARCHAR(50) NULL,
        dias_credito INT NULL,
        fecha_vencimiento_pago DATE NULL,
        moneda VARCHAR(3) DEFAULT 'USD',
        tasa_cambio DECIMAL(10,6) NULL,
        estado_orden VARCHAR(30) DEFAULT 'Pendiente',
        tipo_orden VARCHAR(20) DEFAULT 'Compra',
        referencia_externa VARCHAR(100) NULL,
        observaciones VARCHAR(MAX) NULL,
        activo BIT DEFAULT 1,
        fecha_creacion DATETIME DEFAULT GETDATE(),
        fecha_actualizacion DATETIME NULL
    );
END;
GO

IF OBJECT_ID('dbo.tbl_detalle_orden_compra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_detalle_orden_compra
    (
        id_detalle_orden_compra INT IDENTITY(1,1) PRIMARY KEY,
        id_orden_compra INT NOT NULL,
        id_producto INT NOT NULL,
        id_lote INT NULL,
        cantidad_solicitada INT NOT NULL,
        cantidad_recibida INT DEFAULT 0,
        cantidad_rechazada INT DEFAULT 0,
        cantidad_pendiente INT NULL,
        precio_unitario DECIMAL(15,2) NOT NULL,
        precio_total_linea DECIMAL(15,2) NULL,
        descuento_linea DECIMAL(5,2) NULL,
        impuesto_linea DECIMAL(15,2) NULL,
        codigo_fiscal_fe VARCHAR(10) NULL,
        unidad_medida_fe VARCHAR(10) NULL,
        estado_linea VARCHAR(30) DEFAULT 'Pendiente',
        fecha_recepcion_esperada DATE NULL,
        observaciones VARCHAR(500) NULL
    );
END;
GO

IF OBJECT_ID('dbo.tbl_recepcion_compra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_recepcion_compra
    (
        id_recepcion INT IDENTITY(1,1) PRIMARY KEY,
        id_orden_compra INT NOT NULL,
        numero_recepcion VARCHAR(20) NOT NULL UNIQUE,
        numero_guia_remision VARCHAR(30) NULL,
        id_usuario_recibe INT NOT NULL,
        fecha_recepcion DATETIME DEFAULT GETDATE(),
        cantidad_total_recibida INT NULL,
        cantidad_total_rechazada INT DEFAULT 0,
        observaciones_recepcion VARCHAR(MAX) NULL,
        estado_recepcion VARCHAR(30) DEFAULT 'Completa',
        activo BIT DEFAULT 1
    );
END;
GO

IF OBJECT_ID('dbo.tbl_liquidacion_compra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_liquidacion_compra
    (
        id_liquidacion_compra INT IDENTITY(1,1) PRIMARY KEY,
        id_orden_compra INT NOT NULL,
        numero_liquidacion VARCHAR(20) NOT NULL UNIQUE,
        id_usuario_registro INT NOT NULL,
        fecha_liquidacion DATETIME DEFAULT GETDATE(),
        numero_factura VARCHAR(50) NULL,
        numero_autorizacion VARCHAR(100) NULL,
        subtotal DECIMAL(15,2) DEFAULT 0,
        descuento_total DECIMAL(15,2) DEFAULT 0,
        impuesto_total DECIMAL(15,2) DEFAULT 0,
        total DECIMAL(15,2) DEFAULT 0,
        saldo_pagado DECIMAL(15,2) DEFAULT 0,
        saldo_pendiente DECIMAL(15,2) DEFAULT 0,
        estado_liquidacion VARCHAR(30) DEFAULT 'Pendiente',
        observaciones VARCHAR(MAX) NULL,
        activo BIT DEFAULT 1,
        fecha_creacion DATETIME DEFAULT GETDATE(),
        fecha_actualizacion DATETIME NULL
    );
END;
GO

IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_referencia_documento') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_referencia_documento INT NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'tipo_documento_referencia') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD tipo_documento_referencia VARCHAR(30) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_lote') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_lote INT NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'numero_lote') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD numero_lote VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'costo_unitario') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD costo_unitario DECIMAL(15,2) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'costo_total_movimiento') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD costo_total_movimiento DECIMAL(15,2) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'saldo_en_dinero') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD saldo_en_dinero DECIMAL(15,2) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'metodo_valuacion') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD metodo_valuacion VARCHAR(30) NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'id_usuario_autoriza') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD id_usuario_autoriza INT NULL;
IF COL_LENGTH('dbo.tbl_movimiento_inventario', 'comprobante_numero') IS NULL ALTER TABLE dbo.tbl_movimiento_inventario ADD comprobante_numero VARCHAR(50) NULL;
GO

IF OBJECT_ID('dbo.tbl_kardex', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_kardex
    (
        id_kardex INT IDENTITY(1,1) PRIMARY KEY,
        id_producto INT NOT NULL,
        id_lote INT NULL,
        numero_lote VARCHAR(50) NULL,
        fecha_movimiento DATETIME DEFAULT GETDATE(),
        tipo_movimiento VARCHAR(30) NOT NULL,
        id_referencia INT NULL,
        tipo_referencia VARCHAR(30) NULL,
        comprobante_numero VARCHAR(50) NULL,
        cantidad_movimiento INT NOT NULL,
        costo_unitario DECIMAL(15,2) NULL,
        costo_total DECIMAL(15,2) NULL,
        stock_anterior INT NULL,
        stock_nuevo INT NULL,
        saldo_anterior_dinero DECIMAL(15,2) NULL,
        saldo_nuevo_dinero DECIMAL(15,2) NULL,
        precio_promedio_ponderado DECIMAL(15,2) NULL,
        metodo_valuacion VARCHAR(30) DEFAULT 'Promedio',
        id_usuario_movimiento INT NULL,
        descripcion_movimiento VARCHAR(500) NULL,
        glosa_contable VARCHAR(255) NULL,
        cuenta_contable_debito VARCHAR(20) NULL,
        cuenta_contable_credito VARCHAR(20) NULL,
        centro_costo VARCHAR(20) NULL,
        estado_kardex VARCHAR(20) DEFAULT 'Registrado',
        observaciones VARCHAR(MAX) NULL,
        fecha_creacion DATETIME DEFAULT GETDATE()
    );
END;
GO
