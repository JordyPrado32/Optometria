IF COL_LENGTH('dbo.emisor', 'direccion_establecimiento') IS NULL
    ALTER TABLE dbo.emisor ADD direccion_establecimiento VARCHAR(500) NULL;
IF COL_LENGTH('dbo.emisor', 'obligado_contabilidad') IS NULL
    ALTER TABLE dbo.emisor ADD obligado_contabilidad BIT NOT NULL CONSTRAINT DF_emisor_obligado_contabilidad DEFAULT (0);
IF COL_LENGTH('dbo.emisor', 'ambiente_codigo') IS NULL
    ALTER TABLE dbo.emisor ADD ambiente_codigo VARCHAR(1) NOT NULL CONSTRAINT DF_emisor_ambiente_codigo DEFAULT ('1');
IF COL_LENGTH('dbo.emisor', 'tipo_emision_codigo') IS NULL
    ALTER TABLE dbo.emisor ADD tipo_emision_codigo VARCHAR(1) NOT NULL CONSTRAINT DF_emisor_tipo_emision_codigo DEFAULT ('1');
IF COL_LENGTH('dbo.emisor', 'certificado_digital_ruta') IS NULL
    ALTER TABLE dbo.emisor ADD certificado_digital_ruta VARCHAR(500) NULL;
IF COL_LENGTH('dbo.emisor', 'certificado_digital_clave') IS NULL
    ALTER TABLE dbo.emisor ADD certificado_digital_clave VARCHAR(1000) NULL;
IF COL_LENGTH('dbo.emisor', 'certificado_digital_clave') IS NOT NULL
    ALTER TABLE dbo.emisor ALTER COLUMN certificado_digital_clave VARCHAR(1000) NULL;
IF COL_LENGTH('dbo.emisor', 'regimen_rimpe') IS NULL
    ALTER TABLE dbo.emisor ADD regimen_rimpe VARCHAR(50) NULL;

IF COL_LENGTH('dbo.tbl_comprobante', 'clave_acceso') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD clave_acceso VARCHAR(49) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'codigo_numerico') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD codigo_numerico VARCHAR(8) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'ambiente_sri') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD ambiente_sri VARCHAR(1) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'tipo_emision_sri') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD tipo_emision_sri VARCHAR(1) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'version_xml') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD version_xml VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'ruta_xml') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD ruta_xml VARCHAR(500) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'xml_no_firmado') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD xml_no_firmado VARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'xml_firmado') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD xml_firmado VARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'hash_xml') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD hash_xml VARCHAR(128) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'fecha_firma') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD fecha_firma DATETIME2 NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'estado_sri') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD estado_sri VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_comprobante', 'mensajes_sri') IS NULL
    ALTER TABLE dbo.tbl_comprobante ADD mensajes_sri VARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.tbl_nota_credito', 'secuencial') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD secuencial BIGINT NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'clave_acceso') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD clave_acceso VARCHAR(49) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'codigo_numerico') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD codigo_numerico VARCHAR(8) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'ambiente_sri') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD ambiente_sri VARCHAR(1) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'tipo_emision_sri') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD tipo_emision_sri VARCHAR(1) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'version_xml') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD version_xml VARCHAR(10) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'ruta_xml') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD ruta_xml VARCHAR(500) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'xml_no_firmado') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD xml_no_firmado VARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'xml_firmado') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD xml_firmado VARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'hash_xml') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD hash_xml VARCHAR(128) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'fecha_firma') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD fecha_firma DATETIME2 NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'estado_sri') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD estado_sri VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_nota_credito', 'mensajes_sri') IS NULL
    ALTER TABLE dbo.tbl_nota_credito ADD mensajes_sri VARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'metodo_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD metodo_entrega VARCHAR(30) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'tarifa_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD tarifa_entrega DECIMAL(10,2) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'direccion_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD direccion_entrega VARCHAR(500) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'referencia_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD referencia_entrega VARCHAR(255) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'telefono_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD telefono_entrega VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'nombre_receptor') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD nombre_receptor VARCHAR(200) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'fecha_listo_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD fecha_listo_entrega DATETIME2 NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'fecha_entregado') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD fecha_entregado DATETIME2 NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'id_comprobante_entrega') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD id_comprobante_entrega INT NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'numero_guia_remision') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD numero_guia_remision VARCHAR(50) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'repartidor_nombre') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD repartidor_nombre VARCHAR(200) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'repartidor_telefono') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD repartidor_telefono VARCHAR(20) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'estado_tracking') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD estado_tracking VARCHAR(30) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'latitud_actual') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD latitud_actual DECIMAL(10,6) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'longitud_actual') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD longitud_actual DECIMAL(10,6) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'url_mapa_seguimiento') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD url_mapa_seguimiento VARCHAR(500) NULL;
IF COL_LENGTH('dbo.tbl_envio_laboratorio', 'observaciones_logistica') IS NULL
    ALTER TABLE dbo.tbl_envio_laboratorio ADD observaciones_logistica VARCHAR(MAX) NULL;
