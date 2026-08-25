namespace ZyrexMES.Api.Modules.Quality;

public sealed record QcSubmitRequest(string SerialNumber, int StationId, string Verdict, int? NgCodeId, string? Notes);
