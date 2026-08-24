namespace ZyrexMES.Api.Modules.MasterData;

public record LineDto(int Id, string Code, string Name, bool IsActive);
public record CreateLineRequest(string? Code, string? Name);
public record UpdateLineRequest(string? Code, string? Name, bool IsActive);

public record StationDto(int Id, int LineId, string Code, string Name, string? ProcessType, bool IsEnabled);
public record CreateStationRequest(int LineId, string? Code, string? Name, string? ProcessType);
public record UpdateStationRequest(string? Code, string? Name, string? ProcessType, bool IsEnabled);

public record ProductDto(int Id, string Sku, string Name, string? Description, bool IsActive);
public record CreateProductRequest(string? Sku, string? Name, string? Description);

public record NgCodeDto(int Id, string Code, string Description, bool IsActive);
public record CreateNgCodeRequest(string? Code, string? Description);
