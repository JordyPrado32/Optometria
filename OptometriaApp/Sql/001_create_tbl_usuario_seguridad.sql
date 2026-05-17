IF OBJECT_ID('dbo.tbl_usuario_seguridad', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_usuario_seguridad
    (
        id_usuario INT NOT NULL PRIMARY KEY,
        two_factor_enabled BIT NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_two_factor_enabled DEFAULT (0),
        authenticator_secret VARCHAR(128) NULL,
        recovery_password_hash VARCHAR(255) NULL,
        recovery_password_expires_at DATETIME NULL,
        must_change_password BIT NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_must_change_password DEFAULT (0),
        created_at DATETIME NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_created_at DEFAULT (GETDATE()),
        updated_at DATETIME NOT NULL CONSTRAINT DF_tbl_usuario_seguridad_updated_at DEFAULT (GETDATE()),
        CONSTRAINT FK_tbl_usuario_seguridad_tbl_usuario
            FOREIGN KEY (id_usuario) REFERENCES dbo.tbl_usuario(id_usuario)
            ON DELETE CASCADE
    );
END
