using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

// EF Core demo for Chapter 3 §3.4 — optimistic concurrency with a real
// DbUpdateConcurrencyException, thrown by a real SQLite UPDATE that matches
// zero rows.
//
// This demo opens its OWN new in-memory SQLite connection rather than reusing
// Program.cs's `conn`. Two reasons: (1) isolation — the bitmask demo already
// left rows in that connection's `Rooms` table with the ORIGINAL (no-Version)
// schema, and EnsureCreated() cannot alter an existing table to add a column;
// a second, unrelated in-memory database keeps Chapter 1's demo model
// completely untouched. (2) realism — "two concurrent web requests" is best
// modeled as two DbContexts that don't even know about each other's
// connection object, only about a shared database.
//
// Concurrency token choice: [ConcurrencyCheck] on an app-managed `int Version`,
// NOT [Timestamp]/.IsRowVersion(). Per the EF Core docs, [Timestamp] maps to
// SQL Server's native `rowversion` type, and "some databases don't support
// these at all (e.g. SQLite)". [ConcurrencyCheck] is the provider-agnostic
// alternative — EF adds it to the WHERE clause either way, but WE are
// responsible for incrementing it on every save (see ContextA below).

internal sealed class VersionedRoom
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public bool IsBooked { get; set; }

    [ConcurrencyCheck]
    public int Version { get; set; }   // app-managed token — WE bump this, EF just checks it
}

internal sealed class VersionedHotelContext(SqliteConnection conn) : DbContext
{
    public DbSet<VersionedRoom> Rooms => Set<VersionedRoom>();

    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite(conn);

    // Fluent equivalent, shown for reference — the [ConcurrencyCheck] attribute
    // above already does this; OnModelCreating is left empty on purpose so the
    // data-annotation path is what actually runs:
    //   b.Entity<VersionedRoom>().Property(r => r.Version).IsConcurrencyToken();
}

internal static class ConcurrencyDemo
{
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 64));
        Console.WriteLine("  Chapter 3 §3.4 — EF Core Optimistic Concurrency");
        Console.WriteLine(new string('=', 64));

        // Its own isolated in-memory SQLite database — see comment above.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using (var seed = new VersionedHotelContext(conn))
        {
            seed.Database.EnsureCreated();
            seed.Rooms.Add(new VersionedRoom { Number = "101", IsBooked = false, Version = 1 });
            seed.SaveChanges();
        }
        Print("Seeded", "one VersionedRoom (Number=101, IsBooked=false, Version=1)");

        // Two DbContexts = two concurrent web requests, each with its own
        // change tracker, each loading the SAME row before either one writes.
        using var contextA = new VersionedHotelContext(conn);
        using var contextB = new VersionedHotelContext(conn);

        var roomA = contextA.Rooms.Single(r => r.Number == "101");
        var roomB = contextB.Rooms.Single(r => r.Number == "101");
        Print("Context A loaded", $"Version={roomA.Version}, IsBooked={roomA.IsBooked}");
        Print("Context B loaded", $"Version={roomB.Version}, IsBooked={roomB.IsBooked}  (same row, own tracker — this is the stale read)");

        // ── Context A commits first — succeeds. ────────────────────────────
        roomA.IsBooked = true;
        roomA.Version++;                              // app-managed bump: 1 -> 2
        contextA.SaveChanges();
        Print("Context A SaveChanges()", $"OK — UPDATE ... WHERE Id={roomA.Id} AND Version=1 matched 1 row. Row is now Version={roomA.Version}.");

        // ── Context B still holds Version=1 from its earlier read. ─────────
        // Its SaveChanges() emits  UPDATE ... WHERE Id=@id AND Version=1,
        // but the row's Version is now 2 — zero rows match.
        roomB.IsBooked = true;
        roomB.Version++;                              // B thinks it's bumping 1 -> 2, but DB is already at 2
        try
        {
            contextB.SaveChanges();
            Print("Context B SaveChanges()", "unexpectedly succeeded — this branch should not run");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Print("Context B SaveChanges() threw", ex.GetType().FullName!);
            Print("Exception message", ex.Message);
            Print("ex.Entries.Count", ex.Entries.Count.ToString());

            // The documented resolution pattern (§3.4): inspect the conflicting
            // entry, pull the CURRENT database values, and decide how to proceed.
            var entry = ex.Entries.Single();
            var databaseValues = entry.GetDatabaseValues();

            if (databaseValues is null)
            {
                Print("Resolution", "row no longer exists in the database (concurrent delete) — abort.");
            }
            else
            {
                var dbVersion = (int)databaseValues["Version"]!;
                var dbIsBooked = (bool)databaseValues["IsBooked"]!;
                Print("Database values now", $"Version={dbVersion}, IsBooked={dbIsBooked}");

                if (dbIsBooked)
                {
                    // Someone else already booked it — this is a genuine business
                    // conflict, not just a stale token. Reload and give up cleanly,
                    // the same way §3.6's in-memory optimistic demo aborts.
                    entry.OriginalValues.SetValues(databaseValues); // refresh token so a retry wouldn't immediately re-conflict
                    Print("Resolution", "room is already booked by the other request — report \"someone else already updated this\", do not retry.");
                }
                else
                {
                    Print("Resolution", "row changed but is still free — refresh OriginalValues and retry SaveChanges().");
                }
            }
        }

        using (var check = new VersionedHotelContext(conn))
        {
            var final = check.Rooms.Single(r => r.Number == "101");
            Print("Final row in database", $"Version={final.Version}, IsBooked={final.IsBooked}");
        }
    }

    private static void Print(string label, string value)
    {
        Console.WriteLine();
        Console.WriteLine("  " + label);
        Console.WriteLine("    → " + value);
    }
}
