using System.Net;
using OptometriaApp.Models;

namespace OptometriaApp.Services;

public static class MedicalCertificateDocument
{
    public static string Build(MedicalCertificate certificate)
    {
        static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
        return $$"""
        <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Certificado {{certificate.Number}}</title>
        <style>body{font:16px/1.6 system-ui,sans-serif;max-width:760px;margin:40px auto;padding:20px;color:#1b3029}button{font:inherit;min-height:44px;padding:8px 18px;border:1px solid #17684e;border-radius:8px;background:#17684e;color:white}button:focus-visible{outline:3px solid #17684e;outline-offset:3px}h1{font-size:28px}p{overflow-wrap:anywhere}.statement{white-space:pre-wrap;margin:36px 0}.signature{margin-top:70px}.notice{color:#8b1820;font-weight:bold}@media print{button{display:none}body{margin:15mm;padding:0} }</style></head><body>
        <button onclick="window.print()">Imprimir</button><h1>Certificado médico</h1>
        <p>Número: {{certificate.Number}}<br>Emisión: {{certificate.CreatedAt:yyyy-MM-dd HH:mm}} UTC<br>Atención: {{certificate.ConsultationDate:yyyy-MM-dd}}</p>
        <p>Paciente: <strong>{{H(certificate.PatientName)}}</strong><br>Identificación: {{H(certificate.PatientIdentification)}}</p>
        {{(certificate.RevocationReason == null ? "" : $"<p class=\"notice\">ANULADO: {H(certificate.RevocationReason)}</p>")}}
        <p class="statement">{{H(certificate.Statement)}}</p>
        <p class="signature">Firma del profesional: __________________________<br>{{H(certificate.DoctorName)}}<br>Registro profesional: {{H(certificate.License)}}</p>
        <p>Documento para firma del profesional. No contiene una firma electrónica.</p></body></html>
        """;
    }
}
