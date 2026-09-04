SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.PurchasePayments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PurchasePayments (
        Id int IDENTITY PRIMARY KEY,
        LiquidationId int NOT NULL REFERENCES dbo.tbl_liquidacion_compra(id_liquidacion_compra),
        UserId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        OperationId uniqueidentifier NOT NULL UNIQUE,
        Amount decimal(18,2) NOT NULL CHECK (Amount <> 0),
        CreatedAt datetime2 NOT NULL,
        Method nvarchar(30) NOT NULL,
        Reference nvarchar(300) NOT NULL,
        ReversesId int NULL REFERENCES dbo.PurchasePayments(Id),
        CONSTRAINT CK_PurchasePayment_Sign CHECK ((ReversesId IS NULL AND Amount > 0) OR (ReversesId IS NOT NULL AND Amount < 0))
    );
    CREATE UNIQUE INDEX UX_PurchasePayments_Reversal ON dbo.PurchasePayments(ReversesId) WHERE ReversesId IS NOT NULL;
    CREATE INDEX IX_PurchasePayments_Liquidation ON dbo.PurchasePayments(LiquidationId);
    -- Preserve existing balances explicitly; do not fabricate individual past payments.
    INSERT dbo.PurchasePayments (LiquidationId, UserId, OperationId, Amount, CreatedAt, Method, Reference)
    SELECT id_liquidacion_compra, id_usuario_registro, NEWID(), saldo_pagado, SYSUTCDATETIME(), N'Saldo inicial', N'Saldo anterior a la introduccion del historial de abonos'
    FROM dbo.tbl_liquidacion_compra WHERE saldo_pagado > 0;
END;
IF OBJECT_ID('dbo.MedicalCertificates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MedicalCertificates (
        Id int IDENTITY PRIMARY KEY,
        ConsultationId int NOT NULL REFERENCES dbo.tbl_consulta(id_consulta),
        UserId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        Number uniqueidentifier NOT NULL UNIQUE,
        CreatedAt datetime2 NOT NULL,
        PatientName nvarchar(300) NOT NULL,
        PatientIdentification nvarchar(50) NOT NULL,
        DoctorName nvarchar(300) NOT NULL,
        License nvarchar(100) NOT NULL,
        ConsultationDate datetime2 NOT NULL,
        Statement nvarchar(2000) NOT NULL,
        RevocationReason nvarchar(300) NULL
    );
    CREATE INDEX IX_MedicalCertificates_Consultation ON dbo.MedicalCertificates(ConsultationId);
END;
COMMIT;
