using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// EF Core demo for Chapter 1 §1.6 — "query a bitmask, one column, no join".
// Uses an in-memory SQLite database so the [Flags] predicate is translated to
// real SQL, which we print with ToQueryString() before running it.

[Flags]
internal enum RoomAmenities : byte
{
    None = 0, Wifi = 1, Breakfast = 2, Parking = 4, Pool = 8, PetFriendly = 16,
}

internal sealed class Room
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public RoomAmenities Amenities { get; set; }   // ← one column, not a join table
}

internal sealed class HotelContext(SqliteConnection conn) : DbContext
{
    public DbSet<Room> Rooms => Set<Room>();

    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite(conn);

    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<Room>().Property(r => r.Amenities).HasColumnType("tinyint"); // 1 byte
}

internal static class Program
{
    private static void Main()
    {
        // In-memory SQLite lives only while the connection is open — keep it open.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using var db = new HotelContext(conn);
        db.Database.EnsureCreated();

        db.Rooms.AddRange(
            new Room { Number = "101", Amenities = RoomAmenities.Wifi },
            new Room { Number = "102", Amenities = RoomAmenities.Wifi | RoomAmenities.Breakfast },
            new Room { Number = "103", Amenities = RoomAmenities.Wifi | RoomAmenities.Breakfast | RoomAmenities.Parking },
            new Room { Number = "104", Amenities = RoomAmenities.Wifi | RoomAmenities.Parking | RoomAmenities.Pool },
            new Room { Number = "105", Amenities = RoomAmenities.Wifi | RoomAmenities.Breakfast | RoomAmenities.Parking | RoomAmenities.Pool },
            new Room { Number = "106", Amenities = RoomAmenities.Pool },
            new Room { Number = "107", Amenities = RoomAmenities.Breakfast | RoomAmenities.Parking });
        db.SaveChanges();

        var need = RoomAmenities.Wifi | RoomAmenities.Pool;            // mask = 9

        Show("Has Wi-Fi (single bit)",
            db.Rooms.Where(r => (r.Amenities & RoomAmenities.Wifi) == RoomAmenities.Wifi));

        Show("Has BOTH Wi-Fi AND Pool (mask 9, AND)",
            db.Rooms.Where(r => (r.Amenities & need) == need));

        Show("Has ANY of Pool or Parking (OR)",
            db.Rooms.Where(r => (r.Amenities & (RoomAmenities.Pool | RoomAmenities.Parking)) != 0));

        Show("HasFlag(Wifi) — EF translates it to the same predicate",
            db.Rooms.Where(r => r.Amenities.HasFlag(RoomAmenities.Wifi)));
    }

    private static void Show(string title, IQueryable<Room> query)
    {
        Console.WriteLine();
        Console.WriteLine("══ " + title);
        Console.WriteLine("  SQL EF Core generates:");
        Console.WriteLine("    " + query.ToQueryString().Replace("\n", "\n    "));
        var rooms = query.ToList();
        Console.WriteLine("  → " + string.Join(", ", rooms.Select(r => $"{r.Number} {{{r.Amenities}}}")));
    }
}
