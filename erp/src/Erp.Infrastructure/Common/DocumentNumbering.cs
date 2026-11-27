using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Common;

/// <summary>
/// Hands out gap-free document numbers. Like the posters it only STAGES the counter change on the shared DbContext:
/// the number is committed by the same SaveChanges as the document it numbers, so nothing is consumed unless the
/// confirmation succeeds. The counter row carries a row-version, so concurrent confirms cannot share a number.
/// </summary>
public sealed class DocumentNumbering(AppDbContext db)
{
    public async Task<string> NextAsync(string prefix, CancellationToken ct)
    {
        var seq = db.DocumentSequences.Local.FirstOrDefault(s => s.Key == prefix)
                  ?? await db.DocumentSequences.FirstOrDefaultAsync(s => s.Key == prefix, ct);
        if (seq is null)
        {
            seq = new DocumentSequence { Key = prefix };
            db.DocumentSequences.Add(seq);
        }
        seq.LastNumber++;
        return $"{prefix}-{seq.LastNumber:D6}";
    }
}
