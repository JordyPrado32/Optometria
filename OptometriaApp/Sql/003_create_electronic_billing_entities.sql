USE bd_optica_modelo_estrella;
GO

IF OBJECT_ID('dbo.emisor', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.emisor
    (
        emisor_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ruc VARCHAR(13) NOT NULL,
        razon_social VARCHAR(300) NOT NULL,
        nombre_comercial VARCHAR(300) NULL,
        tipo_persona CHAR(1) NOT NULL,
        tipo_identificacion VARCHAR(2) NOT NULL,
        direccion VARCHAR(500) NULL,
        telefono VARCHAR(20) NULL,
        correo VARCHAR(100) NULL,
        provincia VARCHAR(100) NULL,
        ciudad VARCHAR(100) NULL,
        codigo_postal VARCHAR(10) NULL,
        establecimiento_codigo VARCHAR(3) NOT NULL,
        punto_emision_codigo VARCHAR(3) NOT NULL,
        nombre_representante_legal VARCHAR(300) NULL,
        cedula_representante VARCHAR(10) NULL,
        es_contribuyente_especial BIT NOT NULL CONSTRAINT DF_emisor_es_contribuyente_especial DEFAULT (0),
        numero_contribuyente_especial VARCHAR(10) NULL,
        estado BIT NOT NULL CONSTRAINT DF_emisor_estado DEFAULT (1),
        fecha_creacion DATETIME NOT NULL CONSTRAINT DF_emisor_fecha_creacion DEFAULT (GETDATE()),
        fecha_actualizacion DATETIME NOT NULL CONSTRAINT DF_emisor_fecha_actualizacion DEFAULT (GETDATE()),
        id_usuario_creacion INT NOT NULL,
        id_usuario_actualizacion INT NULL,
        CONSTRAINT FK_emisor_usuario_creacion FOREIGN KEY (id_usuario_creacion) REFERENCES dbo.tbl_usuario(id_usuario),
        CONSTRAINT FK_emisor_usuario_actualizacion FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario)
    );
END;
GO

IF OBJECT_ID('dbo.clients', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.clients
    (
        cliente_id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        tipo_cliente VARCHAR(20) NOT NULL,
        tipo_identificacion VARCHAR(2) NOT NULL,
        numero_identificacion VARCHAR(20) NOT NULL,
        razon_social VARCHAR(300) NOT NULL,
        nombres VARCHAR(200) NULL,
        apellidos VARCHAR(200) NULL,
        nombre_comercial VARCHAR(300) NULL,
        direccion VARCHAR(500) NULL,
        ciudad VARCHAR(100) NULL,
        provincia VARCHAR(100) NULL,
        codigo_postal VARCHAR(10) NULL,
        telefono VARCHAR(20) NULL,
        correo_electronico VARCHAR(100) NULL,
        es_contribuyente_especial BIT NOT NULL CONSTRAINT DF_clients_es_contribuyente_especial DEFAULT (0),
        numero_contribuyente_especial VARCHAR(10) NULL,
        pais_codigo VARCHAR(2) NOT NULL CONSTRAINT DF_clients_pais_codigo DEFAULT ('EC'),
        es_residente_exterior BIT NOT NULL CONSTRAINT DF_clients_es_residente_exterior DEFAULT (0),
        es_consumidor_final BIT NOT NULL CONSTRAINT DF_clients_es_consumidor_final DEFAULT (0),
        es_obligado_contabilidad BIT NOT NULL CONSTRAINT DF_clients_es_obligado_contabilidad DEFAULT (0),
        contacto_nombre VARCHAR(200) NULL,
        contacto_telefono VARCHAR(20) NULL,
        contacto_correo VARCHAR(100) NULL,
        condicion_pago VARCHAR(50) NULL,
        dias_plazo INT NOT NULL CONSTRAINT DF_clients_dias_plazo DEFAULT (0),
        limite_credito DECIMAL(15,2) NOT NULL CONSTRAINT DF_clients_limite_credito DEFAULT (0),
        saldo_deudor DECIMAL(15,2) NOT NULL CONSTRAINT DF_clients_saldo_deudor DEFAULT (0),
        estado BIT NOT NULL CONSTRAINT DF_clients_estado DEFAULT (1),
        observaciones VARCHAR(500) NULL,
        fecha_creacion DATETIME NOT NULL CONSTRAINT DF_clients_fecha_creacion DEFAULT (GETDATE()),
        fecha_actualizacion DATETIME NOT NULL CONSTRAINT DF_clients_fecha_actualizacion DEFAULT (GETDATE()),
        id_usuario_creacion INT NOT NULL,
        id_usuario_actualizacion INT NULL,
        CONSTRAINT FK_clients_usuario_creacion FOREIGN KEY (id_usuario_creacion) REFERENCES dbo.tbl_usuario(id_usuario),
        CONSTRAINT FK_clients_usuario_actualizacion FOREIGN KEY (id_usuario_actualizacion) REFERENCES dbo.tbl_usuario(id_usuario)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_emisor_ruc' AND object_id = OBJECT_ID('dbo.emisor'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_emisor_ruc ON dbo.emisor (ruc);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_emisor_usuario' AND object_id = OBJECT_ID('dbo.emisor'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_emisor_usuario ON dbo.emisor (id_usuario_creacion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_emisor_ruc' AND object_id = OBJECT_ID('dbo.emisor'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_emisor_ruc ON dbo.emisor (ruc);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_emisor_estado' AND object_id = OBJECT_ID('dbo.emisor'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_emisor_estado ON dbo.emisor (estado);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_clients_usuario_identificacion' AND object_id = OBJECT_ID('dbo.clients'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_clients_usuario_identificacion ON dbo.clients (id_usuario_creacion, tipo_identificacion, numero_identificacion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_numero_identificacion' AND object_id = OBJECT_ID('dbo.clients'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_clients_numero_identificacion ON dbo.clients (numero_identificacion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_estado' AND object_id = OBJECT_ID('dbo.clients'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_clients_estado ON dbo.clients (estado);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_clients_razon_social' AND object_id = OBJECT_ID('dbo.clients'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_clients_razon_social ON dbo.clients (razon_social);
END;
GO
