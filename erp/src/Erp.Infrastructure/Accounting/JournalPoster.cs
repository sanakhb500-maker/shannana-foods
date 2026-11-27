using Erp.Application.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Accounting;

/// <summary>One side of an automatic posting, addressed by account ROLE (not id) so the chart can be re-coded freely.</summary>
public readonly record struct PostLine(string Role, decimal Debit, decimal Credit, PartyType Party = PartyType.None, int? PartyId = null, int? AccountId = null);

/// <summary>
/// The ONLY place automatic journal entries are created. Like <see cref="Inventory.StockPoster"/> it only STAGES entries
/// on the shared scoped DbContext: the document use case commits them with its single SaveChanges, so a document and its
/// accounting entry are atomic.
/// </summary>
public sealed class JournalPoster(AppDbContext db, ICurrentUser user, DocumentNumbering numbers)
{
    private Dictionary<string, int>? _roles;
    private DateOnly? _lockDate;
    private bool _lockLoaded;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<int> AccountIdAsync(string role, CancellationToken ct)
    {
        _roles ??= await db.Accounts.AsNoTracking().Where(a => a.SystemRole != null && a.IsActive)
            .ToDictionaryAsync(a => a.SystemRole!, a => a.Id, ct);
        return _roles.TryGetValue(role, out var id)
            ? id
            : throw new BusinessRuleException($"No active account is mapped to the posting role '{role}'. Check the chart of accounts.");
    }

    /// <summary>Refuses posting into a closed period.</summary>
    public async Task EnsureOpenAsync(DateOnly date, CancellationToken ct)
    {
        if (!_lockLoaded)
        {
            _lockDate = await db.AccountingSettings.AsNoTracking().Select(s => s.LockDate).FirstOrDefaultAsync(ct);
            _lockLoaded = true;
        }
        if (_lockDate is { } lockDate && date <= lockDate)
            throw new BusinessRuleException($"The accounting period is closed up to {lockDate:yyyy-MM-dd}; nothing can be posted on or before that date.");
    }

    /// <summary>Stages a balanced, posted entry. Lines that are zero are skipped; an all-zero entry is not created.</summary>
    public async Task<JournalEntry?> PostAsync(string sourceType, int? sourceId, DateOnly date, string description, CancellationToken ct, params PostLine[] lines)
    {
        var real = lines.Where(l => l.Debit != 0 || l.Credit != 0).ToList();
        if (real.Count == 0) return null;
        if (real.Sum(l => l.Debit) != real.Sum(l => l.Credit))
            throw new InvalidOperationException($"Unbalanced automatic entry for {sourceType} {sourceId}.");

        await EnsureOpenAsync(date, ct);
        var entry = new JournalEntry
        {
            Number = await numbers.NextAsync(SequenceKeys.Journal, ct), Date = date, Description = description, SourceType = sourceType, SourceId = sourceId, Status = JournalStatus.Posted,
            PostedAt = DateTime.UtcNow, PostedBy = user.UserName,
        };
        foreach (var l in real)
            entry.Lines.Add(new JournalLine
            {
                AccountId = l.AccountId ?? await AccountIdAsync(l.Role, ct), Debit = l.Debit, Credit = l.Credit, PartyType = l.Party, PartyId = l.PartyId,
            });
        db.JournalEntries.Add(entry);
        return entry;
    }

    /// <summary>Undoes every live entry of a document by posting mirror entries today, and marks the originals Reversed.</summary>
    public async Task ReverseAsync(string sourceType, int sourceId, string description, CancellationToken ct)
    {
        var originals = await db.JournalEntries.Include(e => e.Lines)
            .Where(e => e.SourceType == sourceType && e.SourceId == sourceId && e.Status == JournalStatus.Posted && e.ReversalOfId == null)
            .ToListAsync(ct);
        if (originals.Count == 0) return;

        await EnsureOpenAsync(Today, ct);
        foreach (var o in originals)
        {
            db.JournalEntries.Add(ReversalOf(o, Today, description, user.UserName, await numbers.NextAsync(SequenceKeys.Journal, ct)));
            o.Status = JournalStatus.Reversed;
        }
    }

    internal static JournalEntry ReversalOf(JournalEntry original, DateOnly date, string description, string? by, string number)
    {
        var rev = new JournalEntry
        {
            Number = number, Date = date, Description = description, SourceType = original.SourceType, SourceId = original.SourceId, Status = JournalStatus.Posted,
            ReversalOfId = original.Id, PostedAt = DateTime.UtcNow, PostedBy = by,
        };
        foreach (var l in original.Lines)
            rev.Lines.Add(new JournalLine
            {
                AccountId = l.AccountId, Debit = l.Credit, Credit = l.Debit, Description = l.Description, PartyType = l.PartyType, PartyId = l.PartyId,
            });
        return rev;
    }
}
