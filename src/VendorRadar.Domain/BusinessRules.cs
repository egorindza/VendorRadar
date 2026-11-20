namespace VendorRadar.Domain;

public static class BusinessRules
{
    public static bool IsIdle(DateTime grantedAt, DateTime? lastUsedAt, DateTime utcNow) =>
        (lastUsedAt ?? grantedAt) <= utcNow.AddDays(-30);

    public static PurchaseState Decide(PurchaseState state, bool approved) => (state, approved) switch
    {
        (PurchaseState.DepartmentReview, true) => PurchaseState.FinanceReview,
        (PurchaseState.FinanceReview, true) => PurchaseState.Approved,
        (PurchaseState.DepartmentReview or PurchaseState.FinanceReview, false) => PurchaseState.Rejected,
        _ => throw new BusinessException("Заявка уже закрыта.")
    };

    public static string NormalizeDomain(string domain)
    {
        var input = domain.Trim().TrimEnd('.');
        if (Uri.CheckHostName(input) != UriHostNameType.Dns || !input.Contains('.'))
            throw new BusinessException("Введите домен в формате example.com.");

        string ascii;
        try { ascii = new System.Globalization.IdnMapping().GetAscii(input).ToLowerInvariant(); }
        catch (ArgumentException) { throw new BusinessException("Некорректное доменное имя."); }
        return ascii.StartsWith("www.", StringComparison.Ordinal) ? ascii[4..] : ascii;
    }

    public static IEnumerable<DateOnly> Payments(Contract contract, DateOnly from, DateOnly through)
    {
        if (!contract.Active || from > through) yield break;
        if (!contract.AutoRenew)
        {
            if (contract.NextPayment >= from && contract.NextPayment <= through) yield return contract.NextPayment;
            yield break;
        }
        var interval = (int)contract.Cycle;
        var elapsedMonths = (from.Year - contract.NextPayment.Year) * 12 + from.Month - contract.NextPayment.Month;
        var lastMonth = (through.Year - contract.NextPayment.Year) * 12 + through.Month - contract.NextPayment.Month;
        for (var offset = Math.Max(0, elapsedMonths / interval) * interval; offset <= lastMonth; offset += interval)
        {
            var date = contract.NextPayment.AddMonths(offset);
            if (date > through) yield break;
            if (date >= from) yield return date;
        }
    }
}

public sealed class BusinessException(string message) : Exception(message);
public sealed class MissingRecordException : Exception;
public sealed class OperationDeniedException : Exception;
