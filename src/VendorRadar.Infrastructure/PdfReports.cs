using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VendorRadar.Application;
using VendorRadar.Domain;

namespace VendorRadar.Infrastructure;

public sealed class PdfReports(X509Certificate2 certificate)
{
    public async Task<byte[]> ClosureAsync(string organization, AccessRun run)
    {
        if (run.Kind != RunKind.Offboarding || run.CompletedAt is null || run.Tasks.Any(x => x.CompletedAt is null))
            throw new BusinessException("Сначала подтвердите отзыв всех доступов.");

        var bytes = Report(organization, "Акт закрытия доступов", content =>
        {
            content.Item().Text(run.Employee.Name).Bold().FontSize(17);
            content.Item().Text($"{run.Employee.Job} · {run.Employee.Email}");
            content.Item().Text($"Дата закрытия UTC: {run.CompletedAt:yyyy-MM-dd HH:mm} · Подтвердил: {run.CompletedBy}");
            content.Item().Text($"Номер операции: {run.Id}");
            if (run.Tasks.Count == 0) content.Item().Text("На момент проверки учтённых доступов не найдено.");
            foreach (var task in run.Tasks.OrderBy(x => x.ServiceName).ThenBy(x => x.AssetLabel))
            {
                content.Item().BorderBottom(1).BorderColor("#DDE7E2").PaddingVertical(8).Column(row =>
                {
                    row.Item().Text($"{task.ServiceName} · {task.Role} · {task.Kind} {task.AssetLabel}").Bold();
                    row.Item().Text($"{task.CompletedAt:yyyy-MM-dd HH:mm} UTC · {task.CompletedBy} · {task.Evidence}");
                });
            }
            content.Item().Text($"Сертификат: {certificate.Subject} · {certificate.Thumbprint}").FontSize(9);
        });
        using var input = new MemoryStream(bytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var signer = new PdfSharpDefaultSigner(certificate, PdfMessageDigestType.SHA256);
        DigitalSignatureHandler.ForDocument(document, signer, new DigitalSignatureOptions
        {
            Reason = "VendorRadar access closure",
            AppName = "VendorRadar"
        });
        using var output = new MemoryStream();
        await document.SaveAsync(output, false);
        return output.ToArray();
    }

    public byte[] Finance(string organization, AnalyticsSnapshot snapshot, DateOnly date) =>
        Report(organization, "Финансовая сводка", content =>
        {
            content.Item().Text($"На {date:dd.MM.yyyy} · базовая валюта USD");
            content.Item().Text($"MRR: {snapshot.Mrr:N2} USD   ARR: {snapshot.Arr:N2} USD").FontSize(17).Bold();
            content.Item().Text($"Простаивают: {snapshot.Idle} · Свободны: {snapshot.Free} · Без курса: {snapshot.Unpriced}");
            content.Item().PaddingTop(12).Text("Расходы по отделам").Bold();
            foreach (var line in snapshot.Departments)
                content.Item().Text($"{line.Department} · {line.Contracts} · {line.MonthlyUsd:N2} USD / мес.");
            content.Item().PaddingTop(12).Text("Действующие контракты").Bold();
            foreach (var line in snapshot.Spend)
            {
                var value = line.MonthlyUsd is decimal monthly ? $"{monthly:N2} USD / мес." : "Нет курса";
                content.Item().Text($"{line.Name} · {line.Department} · {value}");
            }
        });

    public static X509Certificate2 DevelopmentCertificate()
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=VendorRadar Development", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static byte[] Report(string organization, string title, Action<ColumnDescriptor> fill) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(x => x.FontSize(11).FontColor("#173329"));
            page.Header().BorderBottom(1).BorderColor("#DDE7E2").PaddingBottom(16).Column(header =>
            {
                header.Item().Text($"VENDORRADAR   /   {organization}").FontColor("#247259").SemiBold();
                header.Item().PaddingTop(10).Text(title).FontSize(24).Bold();
            });
            page.Content().PaddingVertical(18).Column(content =>
            {
                content.Spacing(7);
                fill(content);
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("VendorRadar   ·   ");
                text.CurrentPageNumber();
            });
        })).GeneratePdf();
}
