using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hesabyar.Application.Sms;

public static class BankSmsRuleCatalog
{
    private const string DefaultResourceName = "Hesabyar.Application.Sms.bank-sms-rules.json";
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    public static IReadOnlyList<BankSmsRule> LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"Embedded bank SMS rules resource was not found: {DefaultResourceName}");
        return Load(stream);
    }

    public static IReadOnlyList<BankSmsRule> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var rules = JsonSerializer.Deserialize<List<BankSmsRule>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("The bank SMS rule catalog is empty or invalid.");

        Validate(rules);
        return rules.AsReadOnly();
    }

    internal static void Validate(IReadOnlyCollection<BankSmsRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
            throw new InvalidDataException("At least one bank SMS rule is required.");

        var bankKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null ||
                rule.SenderAliases is null ||
                rule.ExpenseKeywords is null ||
                rule.IncomeKeywords is null)
                throw new InvalidDataException("A bank SMS rule or one of its required collections is null.");

            if (string.IsNullOrWhiteSpace(rule.BankKey) || !bankKeys.Add(rule.BankKey.Trim()))
                throw new InvalidDataException("Bank SMS rule keys must be non-empty and unique.");

            if (rule.AmountMultiplier is <= 0)
                throw new InvalidDataException($"The amount multiplier for '{rule.BankKey}' must be positive.");

            if (rule.SenderAliases.Any(string.IsNullOrWhiteSpace) ||
                rule.ExpenseKeywords.Any(string.IsNullOrWhiteSpace) ||
                rule.IncomeKeywords.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"The rule '{rule.BankKey}' contains an empty alias or keyword.");

            if (!rule.SenderAliases.Any() && string.IsNullOrWhiteSpace(rule.BodyPattern))
                throw new InvalidDataException($"The rule '{rule.BankKey}' has no matching condition.");

            if (!string.IsNullOrWhiteSpace(rule.BodyPattern))
            {
                try
                {
                    _ = new Regex(rule.BodyPattern, RegexOptions.CultureInvariant, PatternTimeout);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"The body pattern for '{rule.BankKey}' is invalid.", exception);
                }
            }
        }
    }
}
