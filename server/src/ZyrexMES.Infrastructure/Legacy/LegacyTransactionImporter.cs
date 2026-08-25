using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZyrexMES.Domain.Entities;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Infrastructure.Legacy;

/// <summary>Requested source for the transaction import.</summary>
public sealed class LiveImportNotApprovedException : Exception
{
    public LiveImportNotApprovedException()
        : base("Live legacy import requires ConfirmLive:true in the body and MES_LEGACY_LIVE_APPROVED=true in the environment.")
    {
    }
}

public sealed record TransactionImportSummary(
    int Fetched,
    int UnitsCreated,
    int TransactionsInserted,
    int DuplicatesSkipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// Imports legacy scan transactions into core Units/UnitTransactions.
/// ZERO-DISTURBANCE: default source is the local staging table; the live
/// LegacyApi source additionally requires an explicit body flag AND an
/// environment approval, and is never exercised by tests.
/// </summary>
public sealed class LegacyTransactionImporter(AppDbContext db, ILegacyMesClient legacyClient, ILogger<LegacyTransactionImporter>? logger = null)
{
    public const string SourceStaging = "Staging";
    public const string SourceLegacyApi = "LegacyApi";
    public const string ImportUserName = "legacy-import";
    private const string CodePrefix = LegacyImportService.CodePrefix;
    private const string NotesPrefix = "LEGACY:";
    private const int BatchSize = 500;
    private const int MaxErrors = 50;
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    private readonly AppDbContext _db = db;
    private readonly ILegacyMesClient _legacyClient = legacyClient;
    private readonly ILogger<LegacyTransactionImporter>? _logger = logger;

    /// <summary>Validates window rules, then imports from the requested source.</summary>
    /// <exception cref="ArgumentException">Invalid window (future ToUtc, inverted, or &gt; 366 days).</exception>
    /// <exception cref="LiveImportNotApprovedException">Live source without double approval.</exception>
    public async Task<TransactionImportSummary> ImportAsync(DateTime fromUtc, DateTime toUtc, string source, bool confirmLive, CancellationToken ct = default)
    {
        if (toUtc > DateTime.UtcNow) throw new ArgumentException("ToUtc cannot be in the future.");
        if (fromUtc >= toUtc) throw new ArgumentException("FromUtc must be earlier than ToUtc.");
        if (toUtc - fromUtc > MaxWindow) throw new ArgumentException("Window exceeds 366 days.");

        if (source == SourceStaging)
        {
            return await ImportFromStagingAsync(fromUtc, toUtc, allowedPairs: null, ct);
        }
        if (source == SourceLegacyApi)
        {
            if (!confirmLive || !IsLiveApproved())
            {
                throw new LiveImportNotApprovedException();
            }
            return await ImportFromLegacyApiAsync(fromUtc, toUtc, ct);
        }
        throw new ArgumentException($"Unknown source '{source}' (expected '{SourceStaging}' or '{SourceLegacyApi}').");
    }

    private static bool IsLiveApproved() =>
        string.Equals(Environment.GetEnvironmentVariable("MES_LEGACY_LIVE_APPROVED"), "true", StringComparison.OrdinalIgnoreCase);

    // --- staging pipeline -----------------------------------------------------

    /// <summary>Maps staging snapshots into core rows. When <paramref name="allowedPairs"/>
    /// is non-null (live mode), only snapshots whose (SN, StationCode) pair was
    /// re-confirmed against the live API are imported.</summary>
    private async Task<TransactionImportSummary> ImportFromStagingAsync(DateTime fromUtc, DateTime toUtc, HashSet<(string Sn, string Station)>? allowedPairs, CancellationToken ct)
    {
        var errors = new List<string>();
        int unitsCreated = 0, inserted = 0, duplicates = 0;

        var user = await EnsureImportUserAsync(ct);
        var snapshots = await _db.LegacyTransactionSnapshots
            .Where(s => s.ScannedAtUtc >= fromUtc && s.ScannedAtUtc <= toUtc)
            .OrderBy(s => s.ScannedAtUtc)
            .ToListAsync(ct);
        var fetched = snapshots.Count;

        // Resolve referenced master data once (prefixed mapping, same as master import).
        var stationCodes = snapshots.Select(s => CodePrefix + s.StationCode).Distinct().ToList();
        var stationMap = await _db.Stations
            .Where(st => stationCodes.Contains(st.Code))
            .ToDictionaryAsync(st => st.Code, st => st.Id, ct);

        var productMap = await LoadProductMapAsync(snapshots, ct);

        // Existing units for the SNs in window (new units are created on demand).
        var sns = snapshots.Select(s => s.SN).Distinct().ToList();
        var unitMap = await _db.Units
            .Where(u => sns.Contains(u.SerialNumber))
            .ToDictionaryAsync(u => u.SerialNumber, u => u, ct);

        // Idempotency set: transactions already in core for these units inside the window.
        var existingUnitIds = unitMap.Values.Select(u => u.Id).ToList();
        var existingTxs = (await _db.UnitTransactions
                .Where(t => existingUnitIds.Contains(t.UnitId) && t.ScannedAtUtc >= fromUtc && t.ScannedAtUtc <= toUtc)
                .Select(t => new { t.UnitId, t.StationId, t.ScannedAtUtc })
                .ToListAsync(ct))
            .Select(t => (t.UnitId, t.StationId, t.ScannedAtUtc))
            .ToHashSet();

        foreach (var chunk in Chunk(snapshots, BatchSize))
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            foreach (var s in chunk)
            {
                if (allowedPairs is not null && !allowedPairs.Contains((s.SN, s.StationCode)))
                {
                    AddError(errors, $"SN {s.SN}: not confirmed by live API, skipped");
                    continue;
                }
                if (!stationMap.TryGetValue(CodePrefix + s.StationCode, out var stationId))
                {
                    AddError(errors, $"SN {s.SN}: station '{s.StationCode}' not mapped in core");
                    continue;
                }
                var sku = ExtractProductSku(s.RawJson);
                if (sku is null || !productMap.TryGetValue(CodePrefix + sku, out var productId))
                {
                    AddError(errors, $"SN {s.SN}: no product mapping (sku='{sku ?? "none"}' in RawJson)");
                    continue;
                }
                if (s.ResultChar != "P" && s.ResultChar != "F")
                {
                    AddError(errors, $"SN {s.SN}: invalid ResultChar '{s.ResultChar}' (expected P/F)");
                    continue;
                }

                var isNewUnit = !unitMap.TryGetValue(s.SN, out var unit);
                if (isNewUnit)
                {
                    unit = new Unit
                    {
                        SerialNumber = s.SN,
                        ProductId = productId,
                        Status = UnitStatus.Completed, // historical import: last transaction already done
                        CreatedAtUtc = s.ScannedAtUtc,
                    };
                    unitMap[s.SN] = unit;
                    _db.Units.Add(unit);
                    unitsCreated++;
                }

                // New units have no persisted transactions yet — dedupe applies to known ids only.
                if (!isNewUnit && existingTxs.Contains((unit.Id, stationId, s.ScannedAtUtc)))
                {
                    duplicates++;
                    continue;
                }

                _db.UnitTransactions.Add(new UnitTransaction
                {
                    Unit = unit, // navigation so EF resolves the FK for unsaved units too
                    StationId = stationId,
                    UserId = user.Id,
                    ScannedAtUtc = s.ScannedAtUtc,
                    Result = s.ResultChar == "P" ? QcVerdict.Pass : QcVerdict.Fail,
                    Notes = $"{NotesPrefix}{s.OperatorCode ?? "-"}",
                });
                inserted++;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        _logger?.LogInformation(
            "Legacy transaction import ({Source}) fetched={Fetched} unitsCreated={Units} inserted={Tx} duplicates={Dup} errors={Err}",
            allowedPairs is null ? SourceStaging : SourceLegacyApi, fetched, unitsCreated, inserted, duplicates, errors.Count);

        return new TransactionImportSummary(fetched, unitsCreated, inserted, duplicates, errors);
    }

    /// <summary>LIVE path (never exercised by tests): re-confirms every staged
    /// (SN, StationCode) pair against the read-only GetMesData API before importing
    /// it from staging. Pairs failing the live check are reported as errors and
    /// excluded. No write service is ever called.</summary>
    private async Task<TransactionImportSummary> ImportFromLegacyApiAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var pairs = await _db.LegacyTransactionSnapshots
            .Where(s => s.ScannedAtUtc >= fromUtc && s.ScannedAtUtc <= toUtc)
            .Select(s => new { s.SN, s.StationCode })
            .Distinct()
            .ToListAsync(ct);

        var allowed = new HashSet<(string Sn, string Station)>();
        var errors = new List<string>();
        foreach (var p in pairs)
        {
            try
            {
                var envelope = await _legacyClient.GetMesDataAsync(p.SN, p.StationCode, ct);
                if (envelope.Code == 0)
                {
                    allowed.Add((p.SN, p.StationCode));
                }
                else
                {
                    AddError(errors, $"SN {p.SN}: live GetMesData rejected (code {envelope.Code})");
                }
            }
            catch (LegacyException ex)
            {
                AddError(errors, $"SN {p.SN}: live GetMesData failed: {ex.Message}");
            }
        }

        return await ImportFromStagingAsync(fromUtc, toUtc, allowed, ct);
    }

    // --- helpers ---------------------------------------------------------------

    private async Task<Dictionary<string, int>> LoadProductMapAsync(List<LegacyTransactionSnapshot> snapshots, CancellationToken ct)
    {
        var skus = new HashSet<string>();
        foreach (var s in snapshots)
        {
            var sku = ExtractProductSku(s.RawJson);
            if (sku is not null)
            {
                skus.Add(CodePrefix + sku);
            }
        }
        return await _db.Products
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, p => p.Id, ct);
    }

    /// <summary>Reads "productSku" from the snapshot's RawJson payload (null when absent/invalid).</summary>
    private static string? ExtractProductSku(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("productSku", out var el)
                && el.ValueKind == JsonValueKind.String
                    ? el.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Fetch-or-create the system account that owns imported transactions
    /// (same pattern as test SeedUserId helpers).</summary>
    private async Task<AppUser> EnsureImportUserAsync(CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == ImportUserName, ct);
        if (user is not null)
        {
            return user;
        }
        user = new AppUser
        {
            Username = ImportUserName,
            PasswordHash = Security.PasswordHasher.Hash(Guid.NewGuid().ToString("N")), // unusable: secret never stored/logged
            FullName = "Legacy Importer (system)",
            Role = UserRole.Supervisor,
            IsActive = true,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < MaxErrors)
        {
            errors.Add(message);
        }
    }

    private static IEnumerable<List<LegacyTransactionSnapshot>> Chunk(List<LegacyTransactionSnapshot> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
