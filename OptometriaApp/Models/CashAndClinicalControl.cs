namespace OptometriaApp.Models;

public sealed class CashClose
{
    public int Id { get; set; }
    public Guid OperationId { get; set; }
    public DateTime ClosedAt { get; set; }
    public int UserId { get; set; }
    public int LastSaleId { get; set; }
    public int LastPaymentId { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal Collected { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }
    public decimal BankWithdrawal { get; set; }
    public decimal OtherWithdrawal { get; set; }
    public decimal RetainedCash { get; set; }
    public string BankReference { get; set; } = "";
    public string Observation { get; set; } = "";
    public string PaymentsJson { get; set; } = "{}";
    public string SalePaymentsJson { get; set; } = "{}";
}

public sealed class ClinicalEditAuthorization
{
    public int Id { get; set; }
    public int EncounterId { get; set; }
    public int DoctorId { get; set; }
    public int AdminId { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class ClinicalEditAudit
{
    public int Id { get; set; }
    public int EncounterId { get; set; }
    public int UserId { get; set; }
    public int? AuthorizationId { get; set; }
    public DateTime EditedAt { get; set; }
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
