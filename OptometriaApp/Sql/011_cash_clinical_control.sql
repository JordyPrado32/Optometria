SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.CashCloses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CashCloses (
        Id int IDENTITY PRIMARY KEY, OperationId uniqueidentifier NOT NULL UNIQUE,
        ClosedAt datetime2 NOT NULL, UserId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        LastSaleId int NOT NULL, LastPaymentId int NOT NULL,
        OpeningCash decimal(18,2) NOT NULL, Collected decimal(18,2) NOT NULL,
        ExpectedCash decimal(18,2) NOT NULL, CountedCash decimal(18,2) NOT NULL,
        BankWithdrawal decimal(18,2) NOT NULL, OtherWithdrawal decimal(18,2) NOT NULL,
        RetainedCash decimal(18,2) NOT NULL,
        BankReference nvarchar(200) NOT NULL, Observation nvarchar(1000) NOT NULL,
        PaymentsJson nvarchar(max) NOT NULL, SalePaymentsJson nvarchar(max) NOT NULL,
        CONSTRAINT CK_CashClose_Balance CHECK (CountedCash >= 0 AND BankWithdrawal >= 0 AND OtherWithdrawal >= 0 AND RetainedCash >= 0 AND CountedCash = BankWithdrawal + OtherWithdrawal + RetainedCash)
    );
END;
IF OBJECT_ID('dbo.ClinicalEditAuthorizations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicalEditAuthorizations (
        Id int IDENTITY PRIMARY KEY,
        EncounterId int NOT NULL REFERENCES dbo.tbl_historia_clinica_optometria_evento(id_historia_evento),
        DoctorId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        AdminId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        GrantedAt datetime2 NOT NULL, UsedAt datetime2 NULL, Reason nvarchar(1000) NOT NULL
    );
    CREATE UNIQUE INDEX UX_ClinicalAuthorization_Pending ON dbo.ClinicalEditAuthorizations(EncounterId, DoctorId) WHERE UsedAt IS NULL;
END;
IF OBJECT_ID('dbo.ClinicalEditAudits', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClinicalEditAudits (
        Id int IDENTITY PRIMARY KEY,
        EncounterId int NOT NULL REFERENCES dbo.tbl_historia_clinica_optometria_evento(id_historia_evento),
        UserId int NOT NULL REFERENCES dbo.tbl_usuario(id_usuario),
        AuthorizationId int NULL REFERENCES dbo.ClinicalEditAuthorizations(Id),
        EditedAt datetime2 NOT NULL, Reason nvarchar(1000) NOT NULL,
        BeforeJson nvarchar(max) NOT NULL, AfterJson nvarchar(max) NOT NULL
    );
END;
COMMIT;
