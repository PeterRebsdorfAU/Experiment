using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiExperiment.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Settings> Settings => Set<Settings>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<TaskResponse> TaskResponses => Set<TaskResponse>();
    public DbSet<EventLog> Events => Set<EventLog>();
    public DbSet<LotteryEntry> LotteryEntries => Set<LotteryEntry>();
    public DbSet<TextSetting> TextSettings => Set<TextSetting>();

    /// <summary>
    /// EnsureCreated opretter kun tabeller i en tom database. Tabeller tilføjet senere oprettes her,
    /// så en eksisterende database (Neon) opgraderes uden at miste data. SQL'en virker i både PostgreSQL og SQLite.
    /// </summary>
    public void UpgradeSchema()
    {
        Database.ExecuteSqlRaw(
            """CREATE TABLE IF NOT EXISTS "TextSettings" ("Key" varchar(64) NOT NULL PRIMARY KEY, "Value" text NOT NULL)""");
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>().Where(e => e.State == EntityState.Modified))
            entry.Entity.Version++;
        return base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Settings>().Property(s => s.Id).ValueGeneratedNever();
        b.Entity<Participant>().Property(p => p.Version).IsConcurrencyToken();
        b.Entity<TaskResponse>().Property(t => t.Version).IsConcurrencyToken();

        b.Entity<Participant>(e =>
        {
            e.HasIndex(p => p.Code).IsUnique();
            e.HasIndex(p => p.AssignmentIndex);
            e.Property(p => p.Code).HasMaxLength(16);
            e.Property(p => p.Group).HasMaxLength(8);
            e.Property(p => p.Step).HasMaxLength(20);
            e.Property(p => p.Gender).HasMaxLength(40);
            e.Property(p => p.AiUsageFrequency).HasMaxLength(40);
            e.HasMany(p => p.Tasks).WithOne(t => t.Participant).HasForeignKey(t => t.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskResponse>(e =>
        {
            e.HasIndex(t => new { t.ParticipantId, t.Position }).IsUnique();
            e.Property(t => t.AnswerRaw).HasMaxLength(100);
        });

        b.Entity<EventLog>(e =>
        {
            e.HasIndex(x => new { x.ParticipantId, x.ClientEventId });
            e.Property(x => x.ClientEventId).HasMaxLength(40);
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.Data).HasMaxLength(2000);
        });

        b.Entity<LotteryEntry>().Property(l => l.Email).HasMaxLength(200);

        b.Entity<TextSetting>(e =>
        {
            e.ToTable("TextSettings");
            e.HasKey(t => t.Key);
            e.Property(t => t.Key).HasMaxLength(64);
        });

        // Alle tidspunkter er UTC. SQLite glemmer "Kind" ved indlæsning, så vi sætter den igen –
        // ellers serialiseres tiderne uden "Z" og browseren tolker dem som lokal tid.
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTime)) prop.SetValueConverter(utc);
            else if (prop.ClrType == typeof(DateTime?)) prop.SetValueConverter(utcNullable);
        }
    }
}
