using Erp.Domain.Entities;

namespace Erp.Application.DriverApp;

/// <param name="Reason">Required when the result is Failed (shop closed, refused, no answer, ...).</param>
public sealed record StopResultInput(StopStatus Status, string? Reason, string? Notes, double? Latitude, double? Longitude);

public sealed record CollectionInput(decimal Amount, PaymentMethod Method, string? Notes);

public sealed record CollectionResult(int ReceiptId, decimal Amount, decimal TotalCollected);

public sealed record DriverTripRow(int Id, string? Number, DateOnly Date, TripStatus Status, string VehiclePlate, int StopCount, int DoneCount);

public sealed record DriverStopLine(int ItemId, string ItemCode, string ItemNameAr, string ItemNameEn, decimal Quantity, decimal UnitPrice, decimal Total);

public sealed record DriverStop(int Id, int Sequence, StopStatus Status, string? Notes, DateTime? ActionAt, int DeliveryNoteId, string? DeliveryNumber,
    int CustomerId, string CustomerNameAr, string CustomerNameEn, string? Phone, string? Address,
    int CurrencyId, string CurrencyCode, decimal TotalAmount, decimal Collected, IReadOnlyList<int> ProofIds, IReadOnlyList<DriverStopLine> Lines);

public sealed record DriverTrip(int Id, string? Number, DateOnly Date, TripStatus Status, string VehiclePlate, string? Notes,
    decimal CapacityKg, decimal TotalWeightKg, bool OverCapacity, IReadOnlyList<DriverStop> Stops);

public sealed record ProofInfo(int Id, ProofKind Kind, long SizeBytes, DateTime CreatedAt);

public sealed record ProofFile(string Path, string ContentType);

/// <summary>What the delivery driver's mobile app can do. Every call is scoped to the Driver record linked to the signed-in account.</summary>
public interface IDriverService
{
    Task<IReadOnlyList<DriverTripRow>> MyTripsAsync(CancellationToken ct);
    Task<DriverTrip> GetTripAsync(int tripId, CancellationToken ct);
    Task<DriverTrip> StartTripAsync(int tripId, CancellationToken ct);
    Task<DriverTrip> CompleteTripAsync(int tripId, CancellationToken ct);
    Task<DriverTrip> SetResultAsync(int tripId, int stopId, StopResultInput input, CancellationToken ct);
    Task<CollectionResult> CollectAsync(int stopId, CollectionInput input, CancellationToken ct);
    Task<ProofInfo> AddProofAsync(int stopId, ProofKind kind, byte[] data, CancellationToken ct);
    /// <param name="driverOnly">True for the app (must be the caller's own stop); false for ERP staff, who are authorised by policy instead.</param>
    Task<ProofFile> GetProofAsync(int proofId, bool driverOnly, CancellationToken ct);
}
