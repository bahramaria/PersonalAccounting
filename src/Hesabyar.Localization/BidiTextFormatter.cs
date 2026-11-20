namespace Hesabyar.Localization;

public static class BidiTextFormatter
{
    private const char LeftToRightIsolate = '\u2066';
    private const char PopDirectionalIsolate = '\u2069';

    public static string IsolateLeftToRight(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(LeftToRightIsolate, value, PopDirectionalIsolate);
    }
}
