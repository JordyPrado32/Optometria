IF NOT EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE LOWER(nombre) = 'bodeguero')
BEGIN
    INSERT INTO dbo.tbl_rol(nombre, descripcion)
    VALUES ('Bodeguero', 'Gestion operativa de inventario, kardex y reposicion');
END;

IF OBJECT_ID('dbo.tbl_sesion', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.tbl_sesion', 'fecha_inicio') IS NULL
        ALTER TABLE dbo.tbl_sesion ADD fecha_inicio DATETIME NULL;

    IF COL_LENGTH('dbo.tbl_sesion', 'fecha_fin') IS NULL
        ALTER TABLE dbo.tbl_sesion ADD fecha_fin DATETIME NULL;
END;
