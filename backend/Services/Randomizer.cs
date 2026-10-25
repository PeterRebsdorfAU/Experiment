using AiExperiment.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AiExperiment.Api.Services;

/// <summary>
/// Balanceret fordeling på serveren: en ny deltager kommer i den gruppe, der har færrest,
/// og ved lige mange trækkes der lod. Grupperne er derfor aldrig mere end 1 fra hinanden
/// (svarer til blokrandomisering med blokke af 2). Tallene læses fra databasen hver gang,
/// så det passer også efter genstart, og efter at admin har slettet deltagere.
/// </summary>
public sealed class Randomizer
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task AssignAsync(AppDb db, Participant p, CancellationToken ct)
    {
        if (p.IsTest)
        {
            // Test-deltagere (admin-forhåndsvisning) har allerede en gruppe og tæller ikke med.
            p.Group ??= Random.Shared.Next(2) == 0 ? Groups.High : Groups.Low;
            p.AssignedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Én tildeling ad gangen, så to samtidige deltagere ikke begge ser "lige mange" og havner samme sted.
        await _gate.WaitAsync(ct);
        try
        {
            var real = db.Participants.Where(x => !x.IsTest && x.Group != null);
            var high = await real.CountAsync(x => x.Group == Groups.High, ct);
            var low = await real.CountAsync(x => x.Group == Groups.Low, ct);

            p.Group = high < low ? Groups.High
                : low < high ? Groups.Low
                : Random.Shared.Next(2) == 0 ? Groups.High : Groups.Low;
            p.AssignmentIndex = (await real.MaxAsync(x => x.AssignmentIndex, ct) ?? 0) + 1;
            p.AssignedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
