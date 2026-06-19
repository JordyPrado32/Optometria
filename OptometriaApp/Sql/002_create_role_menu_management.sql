IF OBJECT_ID('dbo.tbl_menu_app', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_menu_app
    (
        id_menu INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        nombre VARCHAR(150) NOT NULL,
        ruta VARCHAR(200) NOT NULL,
        icono VARCHAR(100) NULL,
        orden INT NOT NULL CONSTRAINT DF_tbl_menu_app_orden DEFAULT (0),
        activo BIT NOT NULL CONSTRAINT DF_tbl_menu_app_activo DEFAULT (1),
        fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_menu_app_fecha_creacion DEFAULT (GETDATE()),
        CONSTRAINT UQ_tbl_menu_app_ruta UNIQUE (ruta)
    );
END

IF OBJECT_ID('dbo.tbl_rol_menu_permiso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_rol_menu_permiso
    (
        id_rol_menu_permiso INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        id_rol INT NOT NULL,
        id_menu INT NOT NULL,
        puede_ver BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_ver DEFAULT (0),
        puede_crear BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_crear DEFAULT (0),
        puede_editar BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_editar DEFAULT (0),
        puede_eliminar BIT NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_eliminar DEFAULT (0),
        fecha_creacion DATETIME NOT NULL CONSTRAINT DF_tbl_rol_menu_permiso_fecha_creacion DEFAULT (GETDATE()),
        CONSTRAINT UQ_tbl_rol_menu_permiso_rol_menu UNIQUE (id_rol, id_menu),
        CONSTRAINT FK_tbl_rol_menu_permiso_tbl_rol FOREIGN KEY (id_rol) REFERENCES dbo.tbl_rol(id_rol) ON DELETE CASCADE,
        CONSTRAINT FK_tbl_rol_menu_permiso_tbl_menu_app FOREIGN KEY (id_menu) REFERENCES dbo.tbl_menu_app(id_menu) ON DELETE CASCADE
    );
END

MERGE dbo.tbl_menu_app AS target
USING
(
    VALUES
        ('Dashboard', '/dashboard', 'dashboard', 1, 1),
        ('Pacientes', '/patients', 'patients', 2, 1),
        ('Roles', '/roles', 'roles', 3, 1),
        ('Menus', '/menus', 'menu', 4, 1),
        ('Registrar usuario', '/register', 'user-plus', 5, 1),
        ('Seguridad', '/setup-2fa', 'shield', 6, 1)
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

IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 1)
BEGIN
    MERGE dbo.tbl_rol_menu_permiso AS target
    USING
    (
        SELECT 1 AS id_rol, id_menu, 1 AS puede_ver, 1 AS puede_crear, 1 AS puede_editar, 1 AS puede_eliminar
        FROM dbo.tbl_menu_app
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
END

IF EXISTS (SELECT 1 FROM dbo.tbl_rol WHERE id_rol = 2)
BEGIN
    MERGE dbo.tbl_rol_menu_permiso AS target
    USING
    (
        SELECT 2 AS id_rol, id_menu,
            CASE WHEN ruta IN ('/dashboard', '/setup-2fa') THEN 1 ELSE 0 END AS puede_ver,
            0 AS puede_crear,
            0 AS puede_editar,
            0 AS puede_eliminar
        FROM dbo.tbl_menu_app
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
END
