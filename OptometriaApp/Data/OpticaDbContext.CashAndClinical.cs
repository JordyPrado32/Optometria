using Microsoft.EntityFrameworkCore;
using OptometriaApp.Models;

namespace OptometriaApp.Data;

public partial class OpticaDbContext
{
    public DbSet<CashClose> CashCloses => Set<CashClose>();
    public DbSet<ClinicalEditAuthorization> ClinicalEditAuthorizations => Set<ClinicalEditAuthorization>();
    public DbSet<ClinicalEditAudit> ClinicalEditAudits => Set<ClinicalEditAudit>();

    private static void ConfigureCashAndClinical(ModelBuilder model)
    {
        model.Entity<CashClose>(e =>
        {
            e.HasIndex(x => x.OperationId).IsUnique();
            e.Property(x => x.Observation).HasMaxLength(1000);
            e.Property(x => x.BankReference).HasMaxLength(200);
            foreach (var property in typeof(CashClose).GetProperties().Where(p => p.PropertyType == typeof(decimal)))
                e.Property(property.Name).HasPrecision(18, 2);
            e.HasOne<tbl_usuario>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClinicalEditAuthorization>(e =>
        {
            e.HasIndex(x => new { x.EncounterId, x.DoctorId }).IsUnique().HasFilter("[UsedAt] IS NULL");
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<tbl_historia_clinica_optometria_evento>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<tbl_usuario>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<tbl_usuario>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClinicalEditAudit>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<tbl_historia_clinica_optometria_evento>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<tbl_usuario>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClinicalEditAuthorization>().WithMany().HasForeignKey(x => x.AuthorizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
