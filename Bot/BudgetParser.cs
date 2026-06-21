namespace IWEHZ.Bot;

internal static class BudgetParser
{
    internal const decimal Min = 100m;
    internal const decimal Max = 10_000m;

    internal static bool TryParse(string? input, out decimal budget)
    {
        budget = 0;

        if (string.IsNullOrWhiteSpace(input) || input.Length > 20)
            return false;

        var digits = new string(input.Where(char.IsDigit).ToArray());

        if (!decimal.TryParse(digits, out budget))
            return false;

        return budget >= Min && budget <= Max;
    }
}
