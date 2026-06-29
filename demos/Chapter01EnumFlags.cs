namespace EngineeringEssentials.Demos;

// ─────────────────────────────────────────────────────────────────────────────
// Chapter 1 — Enum Flags & Domain Modeling
//   Plain enum  = radio button (exactly one)
//   [Flags] enum = checkboxes (any subset, packed into one integer)
//   + Value Object vs Entity (the DDD reason flags are correct here)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>RADIO BUTTON — exactly one value at a time. byte backing = 1 column byte.</summary>
internal enum BookingStatus : byte
{
    Pending   = 0,
    Confirmed = 1,
    CheckedIn = 2,
    CheckedOut = 3,
    Cancelled = 4,
}

/// <summary>CHECKBOXES — any subset, one integer. Each member is a distinct power of two.</summary>
[Flags]
internal enum RoomAmenities : byte
{
    None        = 0,        // 0b0000_0000  — the empty set
    Wifi        = 1 << 0,   // 0b0000_0001  = 1
    Breakfast   = 1 << 1,   // 0b0000_0010  = 2
    Parking     = 1 << 2,   // 0b0000_0100  = 4
    Pool        = 1 << 3,   // 0b0000_1000  = 8
    PetFriendly = 1 << 4,   // 0b0001_0000  = 16

    // Named combinations are free — just OR-ed bits.
    Standard = Wifi | Breakfast,                  // = 3
    Resort   = Wifi | Breakfast | Parking | Pool, // = 15
}

// ── §1.9 trap: the SAME persisted bytes, reinterpreted by a "tidied" enum.
//    v1 is what got written to the database…
[Flags]
internal enum AmenitiesV1 : byte { None = 0, Wifi = 1, Breakfast = 2, Parking = 4 }
//    …v2 inserts Pool before Parking, renumbering Parking from 4 to 8.
[Flags]
internal enum AmenitiesV2 : byte { None = 0, Wifi = 1, Breakfast = 2, Pool = 4, Parking = 8 }

// ── Value Object: defined entirely by its values, no identity, compared by value.
internal readonly record struct Money(decimal Amount, string Currency);

// ── Entity: defined by identity that persists through change, compared by id.
internal sealed class ExtraService
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public Money Price { get; init; }
    public bool IsActive { get; set; }
}

internal static class Chapter01EnumFlags
{
    public static void Run()
    {
        Ui.Title("Chapter 1 — Enum Flags & Domain Modeling");

        RadioVsCheckbox();
        TheBitMechanism();
        SetAlgebra();
        HasFlagTrap();
        QueryByAmenity();
        BitBudgetTruth();
        ValueObjectVsEntity();
        RenumberingTrap();
    }

    private static void RadioVsCheckbox()
    {
        Ui.Section("Radio button (plain enum) vs Checkbox ([Flags])");

        BookingStatus status = BookingStatus.Confirmed;     // one value, never two
        Ui.Show("status", status);
        Ui.Show("(byte)status", (byte)status);

        // A subset of amenities packed into ONE number:
        var amenities = RoomAmenities.Wifi | RoomAmenities.Breakfast | RoomAmenities.Parking;
        Ui.Show("amenities.ToString()", amenities);          // "Standard, Parking" — see note
        Ui.Show("(byte)amenities", (byte)amenities);         // 1|2|4 = 7
        Ui.Note("Stored value is just 7. ToString prefers the named combo: Wifi|Breakfast == Standard,");
        Ui.Note("so it prints \"Standard, Parking\". The bits are identical — naming combos only changes formatting.");
        Ui.Note("One column, one integer — not three tables joined many-to-many.");
    }

    private static void TheBitMechanism()
    {
        Ui.Section("Why powers of two: 1 << n slides one bit left");

        for (int n = 0; n < 5; n++)
            Ui.Show($"1 << {n}", 1 << n);                    // 1, 2, 4, 8, 16

        // What goes wrong WITHOUT distinct powers of two:
        // if two members shared a bit, turning one on silently turns the other on.
        Ui.Note("Distinct power-of-two bits = each member owns exactly one bit = unambiguous subset.");
    }

    private static void SetAlgebra()
    {
        Ui.Section("Bitwise operators ARE set operators");

        var a = RoomAmenities.Wifi | RoomAmenities.Breakfast | RoomAmenities.Parking;

        a |= RoomAmenities.Pool;                  // union / add
        Ui.Show("after  |= Pool", a);

        a &= ~RoomAmenities.Breakfast;            // difference / remove
        Ui.Show("after  &= ~Breakfast", a);

        a ^= RoomAmenities.PetFriendly;           // toggle
        Ui.Show("after  ^= PetFriendly", a);

        // Membership — "has ALL of these?"
        bool isResortLike = (a & RoomAmenities.Resort) == RoomAmenities.Resort;
        Ui.Show("(a & Resort) == Resort", isResortLike);

        // "ANY of these?"
        bool hasAnyMeal = (a & RoomAmenities.Breakfast) != 0;
        Ui.Show("(a & Breakfast) != 0", hasAnyMeal);
    }

    private static void HasFlagTrap()
    {
        Ui.Section("HasFlag — and the None trap");

        var a = RoomAmenities.Wifi | RoomAmenities.Pool;
        Ui.Show("a.HasFlag(Pool)", a.HasFlag(RoomAmenities.Pool));      // true

        // TRAP: every set "contains" the empty set, so HasFlag(None) is ALWAYS true.
        Ui.Show("a.HasFlag(None)  <-- trap", a.HasFlag(RoomAmenities.None)); // true!
        Ui.Note("Never test \"no amenities\" with HasFlag(None). Use: a == RoomAmenities.None");
        Ui.Show("a == None (correct empty test)", a == RoomAmenities.None); // false
    }

    private static void QueryByAmenity()
    {
        Ui.Section("Query \"rooms with Wi-Fi\" — other bits don't matter");

        // Same room stores DIFFERENT integers depending on its other amenities.
        var rooms = new (int No, RoomAmenities A)[]
        {
            (101, RoomAmenities.Wifi),                                                   // 1
            (102, RoomAmenities.Wifi | RoomAmenities.Breakfast),                         // 3
            (103, RoomAmenities.Breakfast | RoomAmenities.Parking),                      // 6
            (104, RoomAmenities.Wifi | RoomAmenities.Parking | RoomAmenities.PetFriendly), // 21
            (105, RoomAmenities.Pool),                                                   // 8
        };

        foreach (var r in rooms)
        {
            // The membership test: mask out every bit except Wi-Fi(1).
            bool hasWifi = (r.A & RoomAmenities.Wifi) == RoomAmenities.Wifi;
            Ui.Show($"Room {r.No}  stored={(byte)r.A,2}  (A & Wifi)==Wifi", hasWifi);
        }

        // This is exactly what EF Core turns into  WHERE (Amenities & 1) = 1
        var withWifi = rooms
            .Where(r => (r.A & RoomAmenities.Wifi) == RoomAmenities.Wifi)
            .Select(r => r.No);
        Ui.Show("rooms with Wi-Fi", string.Join(", ", withWifi));
        Ui.Note("(A & 1) zeroes every other bit, so it's 1 iff Wi-Fi is on — the rest can't change it.");
    }

    private static void BitBudgetTruth()
    {
        Ui.Section("Bit budget — the honest version (not the '31 bits' myth)");

        // An int has 32 usable flag bits. Bit 31 works fine — it just makes the
        // stored value NEGATIVE, which surprises you in raw inspection / plain SQL.
        int bit31 = 1 << 31;
        Ui.Show("1 << 31  (32nd bit)", bit31);             // -2147483648
        Ui.Note("32 flags fit in int; stop at 31 only if you want every value to stay positive.");
        Ui.Note("Need more than 32? Switch backing type to long (64) — or it's a design smell (catalog, not flags).");
    }

    private static void ValueObjectVsEntity()
    {
        Ui.Section("The principle — Value Object vs Entity");

        // Value Object: equal when their VALUES are equal. No identity.
        var p1 = new Money(120.00m, "USD");
        var p2 = new Money(120.00m, "USD");
        Ui.Show("Money(120,USD) == Money(120,USD)", p1 == p2);   // true → store INLINE (one column set)

        // Entity: identity persists through change; two different rows are NOT equal
        // just because their data matches.
        var s1 = new ExtraService { Id = 1, Name = "Airport pickup", Price = new(30m, "USD"), IsActive = true };
        var s2 = new ExtraService { Id = 2, Name = "Airport pickup", Price = new(30m, "USD"), IsActive = true };
        Ui.Show("ExtraService#1 == ExtraService#2", ReferenceEquals(s1, s2)); // false → its own row + lifecycle

        Ui.Note("Amenities = Value Object  -> flags, one column.");
        Ui.Note("ExtraService = Entity (price, admin-editable, reported on) -> table + many-to-many.");
        Ui.Note("Test: does the option have a price, a label users see, an admin who edits it, a report that counts it?");
    }

    private static void RenumberingTrap()
    {
        Ui.Section("§1.9 trap — renumbering persisted flags corrupts data silently");

        // A room saved long ago as Parking. On disk it is just the byte 4.
        const byte stored = 4;

        Ui.Show("stored byte", stored);
        Ui.Show("read as AmenitiesV1 (original)", (AmenitiesV1)stored);   // Parking
        Ui.Show("read as AmenitiesV2 (renumbered)", (AmenitiesV2)stored); // Pool  ← same bytes!
        Ui.Note("Inserting Pool=4 shifted Parking to 8. Every old '4' now reads as Pool. No error, no warning.");
        Ui.Note("Rule: APPEND new powers of two; never RENUMBER existing members once rows exist.");
    }
}
