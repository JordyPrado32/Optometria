using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public DbSet<PurchasePayment> PurchasePayments => Set<PurchasePayment>();
    public DbSet<MedicalCertificate> MedicalCertificates => Set<MedicalCertificate>();

    private static void ConfigureCompletionEntities(ModelBuilder model)
    {
        var payment = model.Entity<PurchasePayment>();
        payment.ToTable("PurchasePayments");
        payment.HasKey(x => x.Id);
        payment.Property(x => x.Amount).HasPrecision(18, 2);
        payment.Property(x => x.Method).HasMaxLength(30);
        payment.Property(x => x.Reference).HasMaxLength(300);
        payment.HasIndex(x => x.OperationId).IsUnique();
        payment.HasIndex(x => x.ReversesId).IsUnique().HasFilter("[ReversesId] IS NOT NULL");
        payment.HasOne<tbl_liquidacion_compra>().WithMany().HasForeignKey(x => x.LiquidationId).OnDelete(DeleteBehavior.Restrict);
        var certificate = model.Entity<MedicalCertificate>();
        certificate.ToTable("MedicalCertificates");
        certificate.HasKey(x => x.Id);
        certificate.HasIndex(x => x.Number).IsUnique();
        certificate.Property(x => x.PatientName).HasMaxLength(300);
        certificate.Property(x => x.PatientIdentification).HasMaxLength(50);
        certificate.Property(x => x.DoctorName).HasMaxLength(300);
        certificate.Property(x => x.License).HasMaxLength(100);
        certificate.Property(x => x.Statement).HasMaxLength(2000);
        certificate.Property(x => x.RevocationReason).HasMaxLength(300);
        certificate.HasOne<tbl_consulta>().WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
    }
}
