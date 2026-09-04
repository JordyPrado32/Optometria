namespace OptometriaApp.Models;

public sealed class PurchasePayment
{
    public int Id { get; set; }
    public int LiquidationId { get; set; }
    public int UserId { get; set; }
    public Guid OperationId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Method { get; set; } = "";
    public string Reference { get; set; } = "";
    public int? ReversesId { get; set; }
}

public sealed class MedicalCertificate
{
    public int Id { get; set; }
    public int ConsultationId { get; set; }
    public int UserId { get; set; }
    public Guid Number { get; set; }
    public DateTime CreatedAt { get; set; }
    public string PatientName { get; set; } = "";
    public string PatientIdentification { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public string License { get; set; } = "";
    public DateTime ConsultationDate { get; set; }
    public string Statement { get; set; } = "";
    public string? RevocationReason { get; set; }
}
