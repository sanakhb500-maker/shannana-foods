using Erp.Application.Common;
using Erp.Application.DriverApp;
using Erp.Application.Sales;
using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Infrastructure.DriverApp;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Where proof-of-delivery files are kept (outside wwwroot, never served directly).</summary>
    public string ProofsPath { get; set; } = "App_Data/proofs";
}

public class DriverService(AppDbContext db, ICurrentUser user, ISalesService sales, IOptions<StorageOptions> storage) : IDriverService
{
    public const int MaxProofBytes = 3 * 1024 * 1024;
    private const int MaxProofsPerStop = 5;

    // ---------- scoping: everything is limited to the caller's own trips ----------
    private async Task<int> MyDriverIdAsync(CancellationToken ct)
    {
        const string none = "No driver is linked to this account.";
        var uid = user.UserId ?? throw new NotFoundException(none);
        return await db.Drivers.AsNoTracking().Where(d => d.UserId == uid && d.IsActive).Select(d => (int?)d.Id).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException(none);
    }

    private async Task<DistributionTrip> MyTripAsync(int tripId, CancellationToken ct)
    {
        var me = await MyDriverIdAsync(ct);
        return await db.DistributionTrips.Include(t => t.Stops).FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == me, ct)
               ?? throw new NotFoundException("Trip not found.");
    }

    private async Task<(DistributionTrip Trip, TripStop Stop)> MyStopAsync(int stopId, CancellationToken ct)
    {
        var me = await MyDriverIdAsync(ct);
        var tripId = await (from s in db.TripStops.AsNoTracking()
                            join t in db.DistributionTrips.AsNoTracking() on s.TripId equals t.Id
                            where s.Id == stopId && t.DriverId == me
                            select (int?)t.Id).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Stop not found.");
        var trip = await MyTripAsync(tripId, ct);
        return (trip, trip.Stops.First(s => s.Id == stopId));
    }

    // ---------- trips ----------
    public async Task<IReadOnlyList<DriverTripRow>> MyTripsAsync(CancellationToken ct)
    {
        var me = await MyDriverIdAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var rows = await (from t in db.DistributionTrips.AsNoTracking()
                          join v in db.Vehicles.AsNoTracking() on t.VehicleId equals v.Id
                          where t.DriverId == me && t.Status != TripStatus.Cancelled
                                && (t.Status == TripStatus.Planned || t.Status == TripStatus.InTransit || t.Date >= since)
                          select new { t.Id, t.Number, t.Date, t.Status, v.PlateNumber, Stops = t.Stops.Count, Done = t.Stops.Count(s => s.Status != StopStatus.Pending) })
            .ToListAsync(ct);
        // what needs doing first: in transit, then planned, then finished; newest first inside each group
        return rows.OrderBy(r => r.Status == TripStatus.InTransit ? 0 : r.Status == TripStatus.Planned ? 1 : 2).ThenByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .Select(r => new DriverTripRow(r.Id, r.Number, r.Date, r.Status, r.PlateNumber, r.Stops, r.Done)).ToList();
    }

    public async Task<DriverTrip> GetTripAsync(int tripId, CancellationToken ct)
    {
        var trip = await MyTripAsync(tripId, ct);
        var detail = await sales.GetTripAsync(trip.Id, ct); // weight, capacity, collected money and proofs per stop
        var plate = await db.Vehicles.AsNoTracking().Where(v => v.Id == trip.VehicleId).Select(v => v.PlateNumber).FirstAsync(ct);

        var deliveryIds = detail.Stops.Select(s => s.DeliveryNoteId).ToList();
        var lines = await (from l in db.DeliveryNoteLines.AsNoTracking()
                           join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                           where deliveryIds.Contains(l.DeliveryNoteId)
                           orderby l.Id
                           select new { l.DeliveryNoteId, l.ItemId, i.Code, i.NameAr, i.NameEn, l.Quantity, l.UnitPrice }).ToListAsync(ct);
        var customerIds = detail.Stops.Select(s => s.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var currencies = await db.Currencies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        var stops = detail.Stops.Select(s =>
        {
            var c = customers[s.CustomerId];
            var stopLines = lines.Where(l => l.DeliveryNoteId == s.DeliveryNoteId)
                .Select(l => new DriverStopLine(l.ItemId, l.Code, l.NameAr, l.NameEn, l.Quantity, l.UnitPrice, Math.Round(l.Quantity * l.UnitPrice, 4))).ToList();
            return new DriverStop(s.Id, s.Sequence, s.Status, s.Notes, s.ActionAt, s.DeliveryNoteId, s.DeliveryNumber, c.Id, c.NameAr, c.NameEn, c.Phone, c.Address,
                s.CurrencyId, currencies.GetValueOrDefault(s.CurrencyId, ""), s.TotalAmount, s.Collected, s.ProofIds ?? [], stopLines);
        }).ToList();

        return new DriverTrip(trip.Id, trip.Number, trip.Date, trip.Status, plate, trip.Notes, detail.CapacityKg, detail.TotalWeightKg, detail.OverCapacity, stops);
    }

    public async Task<DriverTrip> StartTripAsync(int tripId, CancellationToken ct)
    {
        await MyTripAsync(tripId, ct);
        await sales.StartTripAsync(tripId, ct);
        return await GetTripAsync(tripId, ct);
    }

    public async Task<DriverTrip> CompleteTripAsync(int tripId, CancellationToken ct)
    {
        await MyTripAsync(tripId, ct);
        await sales.CompleteTripAsync(tripId, ct);
        return await GetTripAsync(tripId, ct);
    }

    public async Task<DriverTrip> SetResultAsync(int tripId, int stopId, StopResultInput input, CancellationToken ct)
    {
        var trip = await MyTripAsync(tripId, ct);
        if (trip.Stops.All(s => s.Id != stopId)) throw new NotFoundException("Stop not found.");
        if (input.Status is not (StopStatus.Delivered or StopStatus.Failed)) throw new BusinessRuleException("Only Delivered or Failed can be recorded.");
        var reason = input.Reason?.Trim();
        if (input.Status == StopStatus.Failed && string.IsNullOrEmpty(reason)) throw new BusinessRuleException("A reason is required when delivery fails.");

        var note = string.Join(" - ", new[] { reason, input.Notes?.Trim() }.Where(x => !string.IsNullOrEmpty(x)));
        if (note.Length > 300) note = note[..300];
        await sales.SetStopStatusAsync(tripId, stopId, new StopStatusInput(input.Status, note.Length == 0 ? null : note), ct);

        var stop = await db.TripStops.FirstAsync(s => s.Id == stopId, ct);
        stop.ActionAt = DateTime.UtcNow;
        stop.Latitude = input.Latitude is >= -90 and <= 90 ? input.Latitude : null;
        stop.Longitude = input.Longitude is >= -180 and <= 180 ? input.Longitude : null;
        await db.SaveChangesAsync(ct);
        return await GetTripAsync(tripId, ct);
    }

    // ---------- money collected at the stop (a draft receipt the accountant confirms) ----------
    public async Task<CollectionResult> CollectAsync(int stopId, CollectionInput input, CancellationToken ct)
    {
        var (trip, stop) = await MyStopAsync(stopId, ct);
        if (trip.Status != TripStatus.InTransit) throw new BusinessRuleException("Stop results can only be recorded while the trip is in transit.");
        if (input.Amount <= 0) throw new BusinessRuleException("The amount must be greater than zero.");

        var d = await db.DeliveryNotes.AsNoTracking().FirstAsync(x => x.Id == stop.DeliveryNoteId, ct);
        var receipt = await sales.CreateReceiptAsync(new CustomerReceiptInput(DateOnly.FromDateTime(DateTime.UtcNow), d.CustomerId, d.CurrencyId, null,
            input.Amount, input.Method, trip.Number, input.Notes?.Trim(), null), ct);
        receipt.TripStopId = stop.Id;
        await db.SaveChangesAsync(ct);

        var total = await db.CustomerReceipts.AsNoTracking().Where(r => r.TripStopId == stop.Id && r.Status != DocumentStatus.Cancelled).SumAsync(r => r.Amount, ct);
        return new CollectionResult(receipt.Id, receipt.Amount, total);
    }

    // ---------- proof of delivery ----------
    /// <summary>Recognises PNG and JPEG by their first bytes (the declared content type is not trusted).</summary>
    public static (string ContentType, string Ext)? Sniff(byte[] data)
    {
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ("image/png", ".png");
        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ("image/jpeg", ".jpg");
        return null;
    }

    public async Task<ProofInfo> AddProofAsync(int stopId, ProofKind kind, byte[] data, CancellationToken ct)
    {
        var (_, stop) = await MyStopAsync(stopId, ct);
        if (!Enum.IsDefined(kind)) throw new BusinessRuleException("Choose Signature or Photo.");
        var type = data.Length is > 0 and <= MaxProofBytes ? Sniff(data) : null;
        if (type is null) throw new BusinessRuleException("Only PNG or JPEG images up to 3 MB are accepted.");
        if (await db.StopProofs.CountAsync(p => p.TripStopId == stop.Id, ct) >= MaxProofsPerStop) throw new BusinessRuleException("A stop can hold at most 5 proof files.");

        var dir = storage.Value.ProofsPath;
        Directory.CreateDirectory(dir);
        var name = Guid.NewGuid().ToString("N") + type.Value.Ext;
        await File.WriteAllBytesAsync(Path.Combine(dir, name), data, ct);

        var proof = new StopProof { TripStopId = stop.Id, Kind = kind, FileName = name, ContentType = type.Value.ContentType, SizeBytes = data.Length };
        db.StopProofs.Add(proof);
        await db.SaveChangesAsync(ct);
        return new ProofInfo(proof.Id, proof.Kind, proof.SizeBytes, proof.CreatedAt);
    }

    public async Task<ProofFile> GetProofAsync(int proofId, bool driverOnly, CancellationToken ct)
    {
        var proof = await db.StopProofs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proofId, ct) ?? throw new NotFoundException("Proof not found.");
        if (driverOnly) await MyStopAsync(proof.TripStopId, ct); // 404 unless it is the caller's own stop
        var path = Path.Combine(storage.Value.ProofsPath, Path.GetFileName(proof.FileName)); // GetFileName: never trust a stored name as a path
        if (!File.Exists(path)) throw new NotFoundException("Proof not found.");
        return new ProofFile(path, proof.ContentType);
    }
}
