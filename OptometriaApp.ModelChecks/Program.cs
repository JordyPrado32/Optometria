using Microsoft.EntityFrameworkCore;
using OptometriaApp.Data;
using OptometriaApp.Models;
using OptometriaApp.Services;

// Checks the actual application model without connecting to a database.
using var db = new OpticaDbContext(new DbContextOptionsBuilder<OpticaDbContext>()
    .UseSqlServer("Server=localhost;Database=ModelChecks;Integrated Security=true").Options);
var payment = db.Model.FindEntityType(typeof(PurchasePayment))!;
var certificate = db.Model.FindEntityType(typeof(MedicalCertificate))!;
Check(payment.GetTableName() == "PurchasePayments", "Payment table mapping");
Check(certificate.GetTableName() == "MedicalCertificates", "Certificate table mapping");
Check(db.Model.FindEntityType(typeof(tbl_consulta))!.GetTableName() == "tbl_consulta", "Certificate foreign key table");
Check(payment.FindProperty(nameof(PurchasePayment.Amount))!.GetPrecision() == 18, "Payment precision");
Check(payment.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == nameof(PurchasePayment.OperationId)), "Payment idempotency index");
Check(payment.GetIndexes().Any(x => x.IsUnique && x.GetFilter() == "[ReversesId] IS NOT NULL"), "One reversal per payment");
Check(payment.GetForeignKeys().All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "Payment history cannot cascade-delete");
Check(certificate.FindProperty(nameof(MedicalCertificate.Statement))!.GetMaxLength() == 2000, "Certificate text limit");
Check(certificate.GetForeignKeys().All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "Certificate history cannot cascade-delete");
Check(db.Model.FindEntityType(typeof(tbl_nota_credito))!.FindProperty("saldo_disponible")!.GetPrecision() == 15, "Credit balance precision matches SQL");
Check(typeof(ClinicCompletionService).Assembly.GetManifestResourceNames().Contains("OptometriaApp.Sql.010_clinic_completion.sql"), "Migration embedded in deployment");
Check(SessionValidationService.CredentialVersion("old") != SessionValidationService.CredentialVersion("new"), "Password changes invalidate credential version");
Console.WriteLine("12 application model checks passed. No database connection used.");

if (args.Contains("--serve"))
{
    var html = MedicalCertificateDocument.Build(new MedicalCertificate
    {
        Id = 1, Number = Guid.Parse("11111111-2222-3333-4444-555555555555"), CreatedAt = new DateTime(2026, 9, 3, 15, 30, 0),
        ConsultationDate = new DateTime(2026, 9, 3), PatientName = "Paciente de prueba", PatientIdentification = "DATOS FICTICIOS",
        DoctorName = "Profesional de prueba", License = "DEMO",
        Statement = "DOCUMENTO DE PRUEBA SIN VALIDEZ CLÍNICA.\nEsta muestra permite comprobar legibilidad, márgenes e impresión del formato. No representa una atención real."
    });
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    var app = builder.Build();
    app.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8"));
    await app.RunAsync("http://127.0.0.1:8765");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
