IF OBJECT_ID('dbo.tbl_orden_rx', 'U') IS NOT NULL
BEGIN
    UPDATE o
    SET o.numero_orden = CONCAT('RX-', RIGHT(REPLICATE('0', 6) + CAST(o.id_orden_rx AS VARCHAR(20)), 6))
    FROM dbo.tbl_orden_rx o
    WHERE o.numero_orden IS NULL
       OR LTRIM(RTRIM(o.numero_orden)) = ''
       OR o.numero_orden LIKE 'RX-20%-%';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_tbl_orden_rx_numero_orden'
      AND object_id = OBJECT_ID('dbo.tbl_orden_rx'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_tbl_orden_rx_numero_orden
        ON dbo.tbl_orden_rx(numero_orden)
        WHERE numero_orden IS NOT NULL;
END;
