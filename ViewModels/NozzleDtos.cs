namespace backend.ViewModels;

public record NozzleForPumpRowDto(int Id, string Name, int DippingId);

public record NozzleStationRowDto(
    int Id,
    int PumpId,
    string PumpNumber,
    string Name,
    int StationId,
    int BusinessId,
    int DippingId);
