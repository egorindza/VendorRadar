using System.Formats.Asn1;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Infrastructure;
using VendorRadar.Domain;
using VendorRadar.Infrastructure;
using Xunit;

namespace VendorRadar.Tests;

public sealed class PdfTests
{
    [Fact]
    public async Task ClosureContainsAValidDetachedCmsSignature()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var signer = PdfReports.DevelopmentCertificate();
        var report = new PdfReports(signer);
        var now = new DateTime(2026, 11, 21, 13, 0, 0, DateTimeKind.Utc);
        var run = new AccessRun
        {
            Employee = new Employee { Name = "Инженер", Email = "employee@example.com", Job = "Разработка" },
            Kind = RunKind.Offboarding, CompletedBy = "IT Security Admin", CompletedAt = now,
            Tasks = [new RunTask { ServiceName = "GitHub", AssetLabel = "Deploy", Kind = AccessKind.ApiKey,
                Role = AccessRole.Owner, CompletedAt = now, CompletedBy = "IT Security Admin", Evidence = "IT-100" }]
        };
        var pdf = await report.ClosureAsync("Northstar Studio", run);
        var match = Regex.Match(Encoding.Latin1.GetString(pdf), @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        Assert.True(match.Success, "The PDF must include ByteRange.");
        var ranges = match.Groups.Values.Skip(1).Select(x => int.Parse(x.Value)).ToArray();
        Assert.Equal(pdf.Length, ranges[2] + ranges[3]);
        var signed = pdf.AsSpan(ranges[0], ranges[1]).ToArray().Concat(pdf.AsSpan(ranges[2], ranges[3]).ToArray()).ToArray();
        var signature = Convert.FromHexString(Encoding.ASCII.GetString(pdf.AsSpan(ranges[1] + 1, ranges[2] - ranges[1] - 2)));
        AsnDecoder.ReadEncodedValue(signature, AsnEncodingRules.BER, out _, out _, out var length);
        var cms = new SignedCms(new ContentInfo(signed), detached: true);
        cms.Decode(signature.AsSpan(0, length));
        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Equal(signer.Thumbprint, Assert.Single(cms.SignerInfos.Cast<SignerInfo>()).Certificate!.Thumbprint);
    }

    [Fact]
    public async Task IncompleteOffboardingCannotGenerateSignedClosure()
    {
        using var signer = PdfReports.DevelopmentCertificate();
        var pdf = new PdfReports(signer);
        await Assert.ThrowsAsync<BusinessException>(() => pdf.ClosureAsync("Northstar Studio",
            new AccessRun { Kind = RunKind.Offboarding }));
    }
}
