using AiExperiment.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AiExperiment.Api.Services;

/// <summary>
/// Blokrandomisering på serveren: i hver blok af 4 deltagere kommer præcis 2 i hver gruppe,
/// i tilfældig rækkefølge. Så er grupperne aldrig mere end 2 fra hinanden i størrelse.
/// Tilstanden udledes af databasen, så den overlever genstart af serveren.
/// </summary>
public sealed class Randomizer
{
    public const int BlockSize = 4;
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

        // Én tildeling ad gangen, så to samtidige deltagere ikke "tager samme plads" i blokken.
        await _gate.WaitAsync(ct);
        try
        {
            var real = db.Participants.Where(x => !x.IsTest && x.AssignmentIndex != null);
            var n = await real.CountAsync(ct);
            var inBlock = n % BlockSize;
            var current = await real.OrderByDescending(x => x.AssignmentIndex).Take(inBlock)
                .Select(x => x.Group).ToListAsync(ct);

            var remHigh = BlockSize / 2 - current.Count(g => g == Groups.High);
            var remLow = BlockSize / 2 - current.Count(g => g == Groups.Low);
            if (remHigh + remLow <= 0) { remHigh = 1; remLow = 1; } // sikkerhedsnet, bør ikke ske

            p.Group = Random.Shared.Next(remHigh + remLow) < remHigh ? Groups.High : Groups.Low;
            p.AssignmentIndex = n + 1;
            p.AssignedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
