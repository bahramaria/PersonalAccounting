using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Hesabyar.Application.Sms;

public sealed class BankSmsParser
{
    private static readonly Regex AmountRegex = new(
        @"(?:مبلغ|برداشت|واریز|خرید|پرداخت|انتقال)\s*(?:از\s+حساب\s*)?[:：]?\s*([0-9][0-9,٬،.٫ \t]*)([+-]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BalanceRegex = new(
        @"(?:مانده|موجودی)\s*[:：]?\s*([0-9][0-9,٬،.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CardRegex = new(
        @"(?:کارت|حساب)\D{0,24}([0-9]{4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DateRegex = new(
        @"(?<![0-9])((?:13|14)[0-9]{2})[/\-]([0-9]{1,2})[/\-]([0-9]{1,2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PartialDateRegex = new(
        @"(?<![0-9])([0-9]{1,2})/([0-9]{1,2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TimeRegex = new(
        @"(?<![0-9])([0-9]{1,2}):([0-9]{2})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<BankSmsRule> _rules;

    public BankSmsParser(IEnumerable<BankSmsRule>? rules = null)
    {
        _rules = rules?.ToArray() ?? BankSmsRuleCatalog.LoadDefault();
        BankSmsRuleCatalog.Validate(_rules);
    }

    public ParsedBankSms Parse(string? sender, string? body)
    {
        var normalizedSender = Normalize(sender ?? string.Empty);
        var normalizedBody = Normalize(body ?? string.Empty);
        var dedupeHash = CreateDedupeHash(normalizedSender, normalizedBody);

        if (string.IsNullOrWhiteSpace(normalizedBody))
            return Unsupported(normalizedSender, normalizedBody, dedupeHash);

        if (ContainsAny(normalizedBody, "رمز پویا", "رمز:", "رمز :"))
            return Unsupported(normalizedSender, normalizedBody, dedupeHash);

        var matchedRule = _rules.FirstOrDefault(rule =>
            rule.SenderAliases.Any(alias =>
                normalizedSender.Contains(Normalize(alias), StringComparison.OrdinalIgnoreCase)) ||
            MatchesBodyPattern(normalizedBody, rule.BodyPattern));

        var amountMatch = AmountRegex.Match(normalizedBody);
        var transactionType = amountMatch.Success && amountMatch.Groups[2].Value == "-"
            ? BankSmsTransactionType.Expense
            : amountMatch.Success && amountMatch.Groups[2].Value == "+"
                ? BankSmsTransactionType.Income
                : DetectTransactionType(normalizedBody, matchedRule);
        var amountMultiplier = ResolveAmountMultiplier(normalizedBody, matchedRule);
        var amount = ScaleAmount(ExtractAmount(AmountRegex, normalizedBody), amountMultiplier);
        var balance = ScaleAmount(ExtractAmount(BalanceRegex, normalizedBody), amountMultiplier);
        var cardMatch = CardRegex.Match(normalizedBody);
        var date = ExtractDate(normalizedBody);
        var time = ExtractTime(normalizedBody);

        var hasFinancialSignal = transactionType != BankSmsTransactionType.Unknown ||
                                 ContainsAny(normalizedBody, "مانده", "موجودی") ||
                                 (normalizedBody.Contains("تراکنش", StringComparison.Ordinal) &&
                                  normalizedBody.Contains("مبلغ", StringComparison.Ordinal));

        if (!hasFinancialSignal)
            return Unsupported(normalizedSender, normalizedBody, dedupeHash);

        if (amount is null || amount <= 0 || transactionType == BankSmsTransactionType.Unknown)
            return new ParsedBankSms(
                BankSmsParseStatus.NeedsReview,
                matchedRule?.BankKey,
                transactionType,
                amount,
                balance,
                cardMatch.Success ? cardMatch.Groups[1].Value : null,
                date,
                time,
                amountMultiplier is not null,
                dedupeHash,
                normalizedSender,
                normalizedBody);

        return new ParsedBankSms(
            matchedRule is null ? BankSmsParseStatus.NeedsReview : BankSmsParseStatus.Parsed,
            matchedRule?.BankKey,
            transactionType,
            amount,
            balance,
            cardMatch.Success ? cardMatch.Groups[1].Value : null,
            date,
            time,
            amountMultiplier is not null,
            dedupeHash,
            normalizedSender,
            normalizedBody);
    }

    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4',
                '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
                '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4',
                '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9',
                'ي' => 'ی', 'ك' => 'ک', '‌' => ' ',
                _ => character
            });
        }

        return Regex.Replace(builder.ToString(), @"[\t\r\n ]+", " ").Trim();
    }

    private static bool MatchesBodyPattern(string body, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            return Regex.IsMatch(body, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static BankSmsTransactionType DetectTransactionType(string body, BankSmsRule? rule)
    {
        if (rule is not null)
        {
            if (rule.ExpenseKeywords.Any(keyword => body.Contains(Normalize(keyword), StringComparison.Ordinal)))
                return BankSmsTransactionType.Expense;
            if (rule.IncomeKeywords.Any(keyword => body.Contains(Normalize(keyword), StringComparison.Ordinal)))
                return BankSmsTransactionType.Income;
        }

        if (ContainsAny(body, "برداشت", "خرید", "پرداخت", "کسر", "انتقال وجه از"))
            return BankSmsTransactionType.Expense;
        if (ContainsAny(body, "واریز", "دریافت", "افزایش موجودی"))
            return BankSmsTransactionType.Income;
        return BankSmsTransactionType.Unknown;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));


    private static long? ResolveAmountMultiplier(string body, BankSmsRule? rule)
    {
        var hasToman = body.Contains("تومان", StringComparison.Ordinal);
        var hasRial = body.Contains("ریال", StringComparison.Ordinal);
        if (hasToman && hasRial)
            return null;
        if (hasToman)
            return 10;
        if (hasRial)
            return 1;
        return rule?.AmountMultiplier is > 0 ? rule.AmountMultiplier : null;
    }

    private static long? ScaleAmount(long? amount, long? multiplier)
    {
        if (amount is null || multiplier is null)
            return amount;
        try
        {
            return checked(amount.Value * multiplier.Value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static long? ExtractAmount(Regex regex, string body)
    {
        var match = regex.Match(body);
        if (!match.Success)
            return null;

        var value = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
        var separators = value.Where(character =>
                character is ',' or '٬' or '،' or '.' or '٫' || character == ' ')
            .Distinct()
            .ToArray();

        if (separators.Length > 1)
            return null;

        if (separators.Length == 1)
        {
            var groups = value.Split(separators[0]);
            if (groups.Length < 2 ||
                groups[0].Length is < 1 or > 3 ||
                groups.Skip(1).Any(group => group.Length != 3) ||
                groups.Any(group => group.Any(character => !char.IsAsciiDigit(character))))
                return null;

            value = string.Concat(groups);
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private static string? ExtractDate(string body)
    {
        var match = DateRegex.Match(body);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var year) &&
            int.TryParse(match.Groups[2].Value, out var month) &&
            int.TryParse(match.Groups[3].Value, out var day) &&
            month is >= 1 and <= 12 && day is >= 1 and <= 31)
            return $"{year:D4}/{month:D2}/{day:D2}";

        var partialMatch = PartialDateRegex.Match(body);
        if (!partialMatch.Success ||
            !int.TryParse(partialMatch.Groups[1].Value, out var partialMonth) ||
            !int.TryParse(partialMatch.Groups[2].Value, out var partialDay) ||
            partialMonth is < 1 or > 12 || partialDay is < 1 or > 31)
            return null;

        return $"{partialMonth:D2}/{partialDay:D2}";
    }

    private static TimeOnly? ExtractTime(string body)
    {
        var match = TimeRegex.Match(body);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var hour) ||
            !int.TryParse(match.Groups[2].Value, out var minute) ||
            hour is < 0 or > 23 || minute is < 0 or > 59)
            return null;

        return new TimeOnly(hour, minute);
    }

    private static string CreateDedupeHash(string sender, string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sender}\n{body}"));
        return Convert.ToHexString(bytes);
    }

    private static ParsedBankSms Unsupported(string sender, string body, string hash) =>
        new(BankSmsParseStatus.Unsupported, null, BankSmsTransactionType.Unknown,
            null, null, null, null, null, false, hash, sender, body);
}
