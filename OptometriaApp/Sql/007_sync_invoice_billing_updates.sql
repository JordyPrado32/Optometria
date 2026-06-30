USE bd_optica_modelo_estrella;
GO

SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* =========================================================
       1. TABLAS BASE DE FACTURACION / COBRO
       ========================================================= */

    IF OBJECT_ID('dbo.tbl_cta_cobrar', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tbl_cta_cobrar
        (
            id_cta_cobrar INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            id_cliente INT NOT NULL,
            id_venta INT NULL,
            id_comprobante INT NULL,
            monto_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_cta_cobrar_monto_total DEFAULT (0),
            saldo DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_cta_cobrar_saldo DEFAULT (0),
            fecha_emision DATETIME2 NOT NULL CONSTRAINT DF_tbl_cta_cobrar_fecha_emision DEFAULT (SYSUTCDATETIME()),
            fecha_vencimiento DATE NULL,
            estado VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_cta_cobrar_estado DEFAULT ('Pendiente'),
            fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_cta_cobrar_fecha_creacion DEFAULT (SYSUTCDATETIME()),
            usuario_creacion VARCHAR(100) NULL
        );
    END;

    IF OBJECT_ID('dbo.tbl_nota_credito', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.tbl_nota_credito
        (
            id_nota_credito INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            id_comprobante_relacionado INT NULL,
            id_cta_cobrar INT NULL,
            numero_nota VARCHAR(50) NOT NULL,
            fecha_emision DATETIME2 NOT NULL CONSTRAINT DF_tbl_nota_credito_fecha_emision DEFAULT (SYSUTCDATETIME()),
            monto_total DECIMAL(18,2) NOT NULL CONSTRAINT DF_tbl_nota_credito_monto_total DEFAULT (0),
            motivo VARCHAR(255) NULL,
            estado VARCHAR(20) NOT NULL CONSTRAINT DF_tbl_nota_credito_estado DEFAULT ('Emitida'),
            usuario_creacion VARCHAR(100) NULL,
            fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_tbl_nota_credito_fecha_creacion DEFAULT (SYSUTCDATETIME())
        );
    END;

    /* =========================================================
       2. AJUSTES A tbl_comprobante
       ========================================================= */

    IF COL_LENGTH('dbo.tbl_comprobante', 'tipo_comprobante') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD tipo_comprobante VARCHAR(50) NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'numero_comprobante') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD numero_comprobante VARCHAR(100) NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'secuencial') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD secuencial BIGINT NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'numero_autorizacion') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD numero_autorizacion VARCHAR(100) NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'fecha_autorizacion') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD fecha_autorizacion DATETIME2 NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'estado_comprobante') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD estado_comprobante VARCHAR(20) NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'id_emisor') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD id_emisor INT NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'ruta_pdf') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD ruta_pdf VARCHAR(255) NULL;

    IF COL_LENGTH('dbo.tbl_comprobante', 'fecha_emision') IS NULL
        ALTER TABLE dbo.tbl_comprobante ADD fecha_emision DATETIME NULL CONSTRAINT DF_tbl_comprobante_fecha_emision DEFAULT (GETDATE());

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_tbl_comprobante_numero_comprobante' AND object_id = OBJECT_ID('dbo.tbl_comprobante'))
        CREATE UNIQUE NONCLUSTERED INDEX UQ_tbl_comprobante_numero_comprobante ON dbo.tbl_comprobante(numero_comprobante) WHERE numero_comprobante IS NOT NULL;

    /* =========================================================
       3. AJUSTES A tbl_abono
       ========================================================= */

    IF COL_LENGTH('dbo.tbl_abono', 'id_cta_cobrar') IS NULL
        ALTER TABLE dbo.tbl_abono ADD id_cta_cobrar INT NULL;

    IF COL_LENGTH('dbo.tbl_abono', 'monto_abono') IS NULL
    BEGIN
        ALTER TABLE dbo.tbl_abono ADD monto_abono DECIMAL(18,2) NULL;

        IF COL_LENGTH('dbo.tbl_abono', 'monto') IS NOT NULL
        BEGIN
            EXEC('UPDATE dbo.tbl_abono SET monto_abono = ISNULL(monto, 0) WHERE monto_abono IS NULL;');
        END;
    END;

    IF COL_LENGTH('dbo.tbl_abono', 'metodo_pago_id') IS NULL
    BEGIN
        ALTER TABLE dbo.tbl_abono ADD metodo_pago_id INT NULL;

        IF COL_LENGTH('dbo.tbl_abono', 'id_metodo_pago') IS NOT NULL
        BEGIN
            EXEC('UPDATE dbo.tbl_abono SET metodo_pago_id = id_metodo_pago WHERE metodo_pago_id IS NULL;');
        END;
    END;

    IF COL_LENGTH('dbo.tbl_abono', 'referencia_pago') IS NULL
        ALTER TABLE dbo.tbl_abono ADD referencia_pago VARCHAR(100) NULL;

    IF COL_LENGTH('dbo.tbl_abono', 'usuario_registro') IS NULL
    BEGIN
        ALTER TABLE dbo.tbl_abono ADD usuario_registro VARCHAR(100) NULL;

        IF COL_LENGTH('dbo.tbl_abono', 'id_usuario') IS NOT NULL
        BEGIN
            UPDATE a
            SET usuario_registro = COALESCE(u.usuario, CONCAT('USER_', a.id_usuario))
            FROM dbo.tbl_abono a
            LEFT JOIN dbo.tbl_usuario u ON u.id_usuario = a.id_usuario
            WHERE a.usuario_registro IS NULL;
        END;
    END;

    IF COL_LENGTH('dbo.tbl_abono', 'fecha_registro') IS NULL
        ALTER TABLE dbo.tbl_abono ADD fecha_registro DATETIME2 NULL CONSTRAINT DF_tbl_abono_fecha_registro DEFAULT (SYSUTCDATETIME());

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tbl_abono') AND name = 'id_cta_cobrar')
    BEGIN
        UPDATE dbo.tbl_abono
        SET id_cta_cobrar = NULL
        WHERE id_cta_cobrar = 0;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_abono_cta' AND object_id = OBJECT_ID('dbo.tbl_abono'))
        CREATE NONCLUSTERED INDEX IX_abono_cta ON dbo.tbl_abono(id_cta_cobrar) WHERE id_cta_cobrar IS NOT NULL;

    /* =========================================================
       4. AJUSTES A tbl_venta
       ========================================================= */

    IF COL_LENGTH('dbo.tbl_venta', 'id_cliente_facturacion') IS NULL
        ALTER TABLE dbo.tbl_venta ADD id_cliente_facturacion INT NULL;

    IF COL_LENGTH('dbo.tbl_venta', 'porcentaje_impuesto') IS NULL
        ALTER TABLE dbo.tbl_venta ADD porcentaje_impuesto DECIMAL(5,2) NULL CONSTRAINT DF_tbl_venta_porcentaje_impuesto DEFAULT (0);

    IF COL_LENGTH('dbo.tbl_venta', 'forma_pago') IS NULL
        ALTER TABLE dbo.tbl_venta ADD forma_pago VARCHAR(50) NULL CONSTRAINT DF_tbl_venta_forma_pago DEFAULT ('Efectivo');

    IF COL_LENGTH('dbo.tbl_venta', 'dias_credito') IS NULL
        ALTER TABLE dbo.tbl_venta ADD dias_credito INT NULL CONSTRAINT DF_tbl_venta_dias_credito DEFAULT (0);

    IF COL_LENGTH('dbo.tbl_venta', 'observaciones_factura') IS NULL
        ALTER TABLE dbo.tbl_venta ADD observaciones_factura VARCHAR(MAX) NULL;

    UPDATE dbo.tbl_venta
    SET porcentaje_impuesto = ISNULL(porcentaje_impuesto, 0),
        forma_pago = ISNULL(forma_pago, 'Efectivo'),
        dias_credito = ISNULL(dias_credito, 0)
    WHERE porcentaje_impuesto IS NULL
       OR forma_pago IS NULL
       OR dias_credito IS NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_venta_paciente_estado' AND object_id = OBJECT_ID('dbo.tbl_venta'))
        CREATE NONCLUSTERED INDEX IX_tbl_venta_paciente_estado ON dbo.tbl_venta(id_paciente, estado);

    /* =========================================================
       5. AJUSTES A tbl_detalle_venta
       ========================================================= */

    IF COL_LENGTH('dbo.tbl_detalle_venta', 'origen_tipo') IS NULL
        ALTER TABLE dbo.tbl_detalle_venta ADD origen_tipo VARCHAR(40) NULL;

    IF COL_LENGTH('dbo.tbl_detalle_venta', 'origen_id') IS NULL
        ALTER TABLE dbo.tbl_detalle_venta ADD origen_id INT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tbl_detalle_venta_origen' AND object_id = OBJECT_ID('dbo.tbl_detalle_venta'))
        CREATE NONCLUSTERED INDEX IX_tbl_detalle_venta_origen ON dbo.tbl_detalle_venta(origen_tipo, origen_id)
        WHERE origen_tipo IS NOT NULL AND origen_id IS NOT NULL;

    /* =========================================================
       6. FK / RELACIONES
       ========================================================= */

    IF OBJECT_ID('dbo.clients', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_venta_clients_facturacion')
            ALTER TABLE dbo.tbl_venta
            ADD CONSTRAINT FK_tbl_venta_clients_facturacion
            FOREIGN KEY (id_cliente_facturacion) REFERENCES dbo.clients(cliente_id);
    END;

    IF OBJECT_ID('dbo.tbl_cta_cobrar', 'U') IS NOT NULL
    BEGIN
        IF OBJECT_ID('dbo.clients', 'U') IS NOT NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_cta_cobrar_clients')
                ALTER TABLE dbo.tbl_cta_cobrar
                ADD CONSTRAINT FK_tbl_cta_cobrar_clients
                FOREIGN KEY (id_cliente) REFERENCES dbo.clients(cliente_id);
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_cta_cobrar_tbl_venta')
            ALTER TABLE dbo.tbl_cta_cobrar
            ADD CONSTRAINT FK_tbl_cta_cobrar_tbl_venta
            FOREIGN KEY (id_venta) REFERENCES dbo.tbl_venta(id_venta);

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_cta_cobrar_tbl_comprobante')
            ALTER TABLE dbo.tbl_cta_cobrar
            ADD CONSTRAINT FK_tbl_cta_cobrar_tbl_comprobante
            FOREIGN KEY (id_comprobante) REFERENCES dbo.tbl_comprobante(id_comprobante);
    END;

    IF OBJECT_ID('dbo.tbl_nota_credito', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_nota_credito_tbl_comprobante')
            ALTER TABLE dbo.tbl_nota_credito
            ADD CONSTRAINT FK_tbl_nota_credito_tbl_comprobante
            FOREIGN KEY (id_comprobante_relacionado) REFERENCES dbo.tbl_comprobante(id_comprobante);

        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_nota_credito_tbl_cta_cobrar')
            ALTER TABLE dbo.tbl_nota_credito
            ADD CONSTRAINT FK_tbl_nota_credito_tbl_cta_cobrar
            FOREIGN KEY (id_cta_cobrar) REFERENCES dbo.tbl_cta_cobrar(id_cta_cobrar);
    END;

    IF OBJECT_ID('dbo.tbl_abono', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_abono_tbl_cta_cobrar')
            ALTER TABLE dbo.tbl_abono
            ADD CONSTRAINT FK_tbl_abono_tbl_cta_cobrar
            FOREIGN KEY (id_cta_cobrar) REFERENCES dbo.tbl_cta_cobrar(id_cta_cobrar);

        IF OBJECT_ID('dbo.tbl_metodo_pago', 'U') IS NOT NULL
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_abono_tbl_metodo_pago')
                ALTER TABLE dbo.tbl_abono
                ADD CONSTRAINT FK_tbl_abono_tbl_metodo_pago
                FOREIGN KEY (metodo_pago_id) REFERENCES dbo.tbl_metodo_pago(id_metodo_pago);
        END;
    END;

    /* =========================================================
       7. INDICES DE COBRO / NOTA
       ========================================================= */

    IF OBJECT_ID('dbo.tbl_cta_cobrar', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cta_cobrar_cliente_estado' AND object_id = OBJECT_ID('dbo.tbl_cta_cobrar'))
            CREATE NONCLUSTERED INDEX IX_cta_cobrar_cliente_estado ON dbo.tbl_cta_cobrar(id_cliente, estado);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cta_cobrar_vencimiento' AND object_id = OBJECT_ID('dbo.tbl_cta_cobrar'))
            CREATE NONCLUSTERED INDEX IX_cta_cobrar_vencimiento ON dbo.tbl_cta_cobrar(fecha_vencimiento)
            WHERE estado = 'Pendiente';
    END;

    IF OBJECT_ID('dbo.tbl_nota_credito', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_nota_relacion' AND object_id = OBJECT_ID('dbo.tbl_nota_credito'))
            CREATE NONCLUSTERED INDEX IX_nota_relacion ON dbo.tbl_nota_credito(id_comprobante_relacionado, id_cta_cobrar);
    END;

    /* =========================================================
       8. METODOS DE PAGO Y PRODUCTOS DE SERVICIO
       ========================================================= */

    IF OBJECT_ID('dbo.tbl_metodo_pago', 'U') IS NOT NULL
    BEGIN
        MERGE dbo.tbl_metodo_pago AS target
        USING
        (
            VALUES
                ('Efectivo'),
                ('Tarjeta'),
                ('Transferencia'),
                ('Credito')
        ) AS source(nombre)
        ON target.nombre = source.nombre
        WHEN NOT MATCHED THEN
            INSERT (nombre) VALUES (source.nombre);
    END;

    IF OBJECT_ID('dbo.tbl_producto', 'U') IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_producto WHERE codigo_producto = 'SRV-CITA-COMPLETADA')
        BEGIN
            INSERT INTO dbo.tbl_producto
            (
                codigo_producto,
                nombre_producto,
                tipo_item,
                descripcion,
                precio_venta,
                stock_actual,
                activo,
                naturaleza_item,
                fecha_creacion,
                usuario_creacion
            )
            VALUES
            (
                'SRV-CITA-COMPLETADA',
                'Consulta optometrica',
                'Servicio',
                'Servicio autogenerado al completar una cita.',
                0,
                0,
                1,
                'Servicio',
                GETDATE(),
                'SYSTEM_BILLING'
            );
        END
        ELSE
        BEGIN
            UPDATE dbo.tbl_producto
            SET nombre_producto = 'Consulta optometrica',
                tipo_item = 'Servicio',
                descripcion = 'Servicio autogenerado al completar una cita.',
                naturaleza_item = 'Servicio',
                activo = 1
            WHERE codigo_producto = 'SRV-CITA-COMPLETADA';
        END;

        IF NOT EXISTS (SELECT 1 FROM dbo.tbl_producto WHERE codigo_producto = 'SRV-EXAMEN-OPTICO')
        BEGIN
            INSERT INTO dbo.tbl_producto
            (
                codigo_producto,
                nombre_producto,
                tipo_item,
                descripcion,
                precio_venta,
                stock_actual,
                activo,
                naturaleza_item,
                fecha_creacion,
                usuario_creacion
            )
            VALUES
            (
                'SRV-EXAMEN-OPTICO',
                'Examen optometrico',
                'Servicio',
                'Servicio autogenerado desde consulta o examen.',
                0,
                0,
                1,
                'Servicio',
                GETDATE(),
                'SYSTEM_BILLING'
            );
        END
        ELSE
        BEGIN
            UPDATE dbo.tbl_producto
            SET nombre_producto = 'Examen optometrico',
                tipo_item = 'Servicio',
                descripcion = 'Servicio autogenerado desde consulta o examen.',
                naturaleza_item = 'Servicio',
                activo = 1
            WHERE codigo_producto = 'SRV-EXAMEN-OPTICO';
        END;
    END;

    /* =========================================================
       9. MENU / PERMISOS DEL MODULO FACTURAS
       ========================================================= */

    IF OBJECT_ID('dbo.tbl_menu_app', 'U') IS NOT NULL
    BEGIN
        MERGE dbo.tbl_menu_app AS target
        USING
        (
            VALUES ('Facturas', '/invoices', 'invoice', 21, 1)
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
                SELECT
                    1 AS id_rol,
                    id_menu,
                    CAST(1 AS BIT) AS puede_ver,
                    CAST(1 AS BIT) AS puede_crear,
                    CAST(1 AS BIT) AS puede_editar,
                    CAST(1 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app
                WHERE ruta = '/invoices'
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
                SELECT
                    2 AS id_rol,
                    id_menu,
                    CAST(1 AS BIT) AS puede_ver,
                    CAST(1 AS BIT) AS puede_crear,
                    CAST(1 AS BIT) AS puede_editar,
                    CAST(0 AS BIT) AS puede_eliminar
                FROM dbo.tbl_menu_app
                WHERE ruta = '/invoices'
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
