using VendorRadar.Domain;
using VendorRadar.Infrastructure;
using Xunit;

namespace VendorRadar.Tests;

public sealed class RulesTests
{
    [Fact]
    public void RecurrencePreservesTheDayAfterShortMonths()
    {
        var plan = new Contract { NextPayment = new(2026, 1, 31), Cycle = BillingCycle.Monthly, AutoRenew = true };
        var dates = BusinessRules.Payments(plan, new(2026, 2, 1), new(2026, 4, 30)).ToArray();
        Assert.Equal(new DateOnly[] { new(2026, 2, 28), new(2026, 3, 31), new(2026, 4, 30) }, dates);
    }

    [Fact]
    public void RadarIncludesPastNoticeForUpcomingChargeAndHonorsAutoRenewDisabled()
    {
        var today = new DateOnly(2026, 11, 21);
        var annual = new Contract
        {
            Name = "Design", NextPayment = today.AddDays(13), Cycle = BillingCycle.Annual,
            NoticeDays = 14, AutoRenew = true
        };
        var paidOnce = new Contract
        {
            Name = "Chat", NextPayment = today.AddDays(2), Cycle = BillingCycle.Monthly,
            NoticeDays = 7, AutoRenew = false
        };
        var alerts = ContractRadar.UpcomingFor([annual, paidOnce], today, 90).ToArray();
        Assert.Contains(alerts, x => x.ContractId == annual.Id && x.Kind == RadarKind.Notice && x.Date == today.AddDays(-1));
        var single = Assert.Single(alerts, x => x.ContractId == paidOnce.Id);
        Assert.Equal(RadarKind.Payment, single.Kind);
    }

    [Fact]
    public void NoticeMayPrecedeCalendarHorizonEvenIfChargeDoesNot()
    {
        var today = new DateOnly(2026, 11, 21);
        var plan = new Contract { NextPayment = today.AddDays(54), Cycle = BillingCycle.Annual, NoticeDays = 30, AutoRenew = true };
        var notice = Assert.Single(ContractRadar.UpcomingFor([plan], today, 30));
        Assert.Equal(RadarKind.Notice, notice.Kind);
        Assert.Equal(today.AddDays(24), notice.Date);
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(30, null, true)]
    [InlineData(90, 29, false)]
    [InlineData(90, 30, true)]
    public void IdleSeatsUseLastUsageOrGrantDate(int grantedDaysAgo, int? usedDaysAgo, bool idle)
    {
        var now = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(idle, BusinessRules.IsIdle(now.AddDays(-grantedDaysAgo), usedDaysAgo is int days ? now.AddDays(-days) : null, now));
    }

    [Theory]
    [InlineData(PurchaseState.DepartmentReview, true, PurchaseState.FinanceReview)]
    [InlineData(PurchaseState.FinanceReview, true, PurchaseState.Approved)]
    [InlineData(PurchaseState.FinanceReview, false, PurchaseState.Rejected)]
    [InlineData(PurchaseState.DepartmentReview, false, PurchaseState.Rejected)]
    public void ProcurementRequiresTwoOrderedDecisions(PurchaseState before, bool approved, PurchaseState after) =>
        Assert.Equal(after, BusinessRules.Decide(before, approved));

    [Fact]
    public void ProcurementCannotReopenClosedRequests() =>
        Assert.Throws<BusinessException>(() => BusinessRules.Decide(PurchaseState.Approved, true));

    [Theory]
    [InlineData(" WWW.FIGMA.COM. ", "figma.com")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    public void DomainsAreCanonical(string input, string expected) =>
        Assert.Equal(expected, BusinessRules.NormalizeDomain(input));

    [Fact]
    public void DomainsDoNotAcceptAUrl() =>
        Assert.Throws<BusinessException>(() => BusinessRules.NormalizeDomain("https://figma.com/pricing"));
}
