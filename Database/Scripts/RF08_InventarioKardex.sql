IF COL_LENGTH('dbo.tbl_producto', 'stock_actual') IS NOT NULL
BEGIN
    UPDATE p
    SET p.stock_actual = ISNULL(p.stock_actual, 0)
    FROM dbo.tbl_producto p
    WHERE p.stock_actual IS NULL;
END;

IF OBJECT_ID('dbo.tbl_movimiento_inventario', 'U') IS NOT NULL
AND OBJECT_ID('dbo.tbl_kardex', 'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.tbl_movimiento_inventario
    (
        id_producto,
        id_usuario,
        tipo_movimiento,
        cantidad,
        stock_anterior,
        stock_resultante,
        fecha_movimiento,
        observaciones,
        tipo_documento_referencia,
        metodo_valuacion,
        costo_unitario
    )
    SELECT
        p.id_producto,
        ISNULL(p.id_usuario_actualizacion, 1),
        'Entrada',
        p.stock_actual,
        0,
        p.stock_actual,
        ISNULL(p.fecha_creacion, GETDATE()),
        'Stock inicial migrado',
        'StockInicial',
        'Promedio',
        p.precio_costo
    FROM dbo.tbl_producto p
    WHERE ISNULL(p.stock_actual, 0) > 0
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.tbl_movimiento_inventario m
          WHERE m.id_producto = p.id_producto
            AND m.tipo_documento_referencia = 'StockInicial');
END;
