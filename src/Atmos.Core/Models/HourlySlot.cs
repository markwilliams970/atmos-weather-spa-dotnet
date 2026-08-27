namespace Atmos.Core.Models;

public sealed record HourlySlot(
    string TimeLabel,
    bool IsCurrent,
    int TempF,
    int TempC,
    int PrecipProb,
    string Emoji,
    bool IsDay);
