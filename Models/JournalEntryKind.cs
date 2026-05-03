namespace gas_station.Models;

/// <summary>Classification for nine-step accounting workflows and reporting.</summary>
public enum JournalEntryKind : byte
{
    Normal = 0,
    Adjusting = 1,
    Closing = 2,
    RecurringAuto = 3,
}
