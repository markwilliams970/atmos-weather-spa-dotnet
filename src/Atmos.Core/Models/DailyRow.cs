namespace Atmos.Core.Models;

public sealed record DailyRow(
    string DayName,
    string DateLabel,
    int HighF,
    int HighC,
    int LowF,
    int LowC,
    string Emoji,
    string Condition,
    double PrecipMm,
    double PrecipIn,
    int PrecipProbMax,
    double UvMax,
    int WindMaxMph,
    int WindMaxKmh);
