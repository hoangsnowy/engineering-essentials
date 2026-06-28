# 1 · Enum Flags & Domain Modeling

> **TL;DR** — A plain `enum` is a *radio button*: exactly one choice. A `[Flags]` enum is a group of *checkboxes*: many choices packed into a single integer. When a concept is a **fixed, closed set of boolean options with no data of its own**, it is a *Value Object* and belongs in **one column**, not in a separate table joined many-to-many. Reach for a real entity and a join table only when each option earns its own identity, attributes, and lifecycle.

---

## 1.1 The symptom

Here is a code review that happens every week, somewhere:

A developer is asked to let a hotel room advertise its amenities — Wi‑Fi, breakfast, parking, a pool, pet-friendliness. They reach for the tool they know best, the database table, and produce:

```
Rooms            Amenities            RoomAmenities (join)
-------          ----------           ----------------------
Id               Id                   RoomId
Number           Name                 AmenityId
...              ...
```

Three tables, a many-to-many relationship, a join on every read, two foreign keys to keep honest, and a migration every time the product team invents a new amenity checkbox. It works. It also encodes a misunderstanding.

The amenities of a room are not five *things* with independent lives. They are **one fact** about the room: *which subset of a known menu does this room offer?* That fact is a single value. Modeling it as a relationship is like storing a person's RGB favourite-colour as three rows in a `ColorChannels` table.

This chapter is about seeing the difference, and the two tools that express it correctly: **enum flags** (the mechanism) and the **Value Object vs Entity** distinction from Domain-Driven Design (the principle behind the mechanism).

---

## 1.2 The intuition: radio buttons vs checkboxes

Most developers already understand this perfectly — at the UI layer.

| UI control | Meaning | Backing type |
|---|---|---|
| **Radio buttons** | Pick **exactly one** of N | plain `enum` |
| **Checkboxes** | Pick **any subset** of N (zero, one, many) | `[Flags]` enum |

A booking has exactly one `BookingStatus` at a time — *Pending* or *Confirmed* or *Cancelled*, never two at once. That is a radio button, a plain enum.

A booking's notification preferences can be email **and** SMS **and** push, all at once, or none. That is a set of checkboxes — and a set of checkboxes is exactly what a flags enum stores.

The mistake in §1.1 is reaching past "a set of checkboxes" — a thing the type system models in one field — for "a network of related tables."

---

## 1.3 A plain enum is just named integers

```csharp
public enum BookingStatus
{
    Pending   = 0,
    Confirmed = 1,
    CheckedIn = 2,
    CheckedOut= 3,
    Cancelled = 4
}
```

An `enum` is syntactic sugar over an integral constant. `BookingStatus.Confirmed` *is* the number `1` with a friendly name and a compiler that stops you assigning `Confirmed` to a `RoomType`. It holds **one** value. There is no meaningful way to be `Pending` and `Cancelled` simultaneously, and the type reflects that. This is the right model for status, kind, category, role-when-singular — anything mutually exclusive.

> **Note** — The default underlying type is `int` (32 bits). You can choose a smaller one — `enum BookingStatus : byte` — and store it as a single byte. We will return to that in [Chapter 2](chapters/02-numeric-types.md); right-sizing the backing type is the same discipline applied to enums.

---

## 1.4 Flags: one integer, many bits, a whole algebra

A flags enum exploits a simple fact: an integer is a row of bits, and each bit is an independent yes/no. If every member is assigned a **distinct power of two**, then each member owns exactly one bit, and any *combination* of members is just those bits turned on together.

```csharp
[Flags]
public enum RoomAmenities
{
    None        = 0,        // 0b0000_0000  — the empty set
    Wifi        = 1 << 0,   // 0b0000_0001  = 1
    Breakfast   = 1 << 1,   // 0b0000_0010  = 2
    Parking     = 1 << 2,   // 0b0000_0100  = 4
    Pool        = 1 << 3,   // 0b0000_1000  = 8
    PetFriendly = 1 << 4,   // 0b0001_0000  = 16

    // Named combinations are free — they are just OR-ed bits:
    Standard    = Wifi | Breakfast,                 // 0b0000_0011 = 3
    Resort      = Wifi | Breakfast | Parking | Pool // 0b0000_1111 = 15
}
```

A room that offers Wi‑Fi, breakfast and parking is stored as `1 | 2 | 4 = 7` — a single number, `0b0000_0111`. Read the bits back and you recover the exact set.

### Set theory, hiding in plain sight

This is not a trick; it is finite-set algebra. A flags enum defines a **universe** of `n` possible elements, and any value is a **subset** of that universe. The bitwise operators *are* the set operators:

| Set operation | Meaning | Bitwise | C# |
|---|---|---|---|
| Union (A ∪ B) | combine two sets | OR | `a \| b` |
| Intersection (A ∩ B) | common elements | AND | `a & b` |
| Difference (A \ B) | remove B from A | AND NOT | `a & ~b` |
| Symmetric diff | toggle | XOR | `a ^ b` |
| Membership (x ∈ A) | does A contain x? | AND, compare | `(a & x) == x` |
| Complement (¬A) | everything not in A | NOT | `~a` |
| Empty set (∅) | nothing selected | — | `None` (0) |

With `n` independent bits there are exactly `2ⁿ` representable subsets. Five amenities → 2⁵ = 32 possible rooms-by-amenity, all addressable by a single small integer. That density is the whole point.

### Why powers of two, and why `None = 0`

If two members shared a bit (say both equalled `3`), you could never tell them apart — turning one on would silently turn the other on. Distinct powers of two guarantee one bit per member, so the subset is unambiguous. And `None = 0` is the empty set: the natural, queryable representation of "no amenities," and the identity element for `|` (OR-ing `None` changes nothing).

> **Pitfall** — `[Flags]` does **not** make the arithmetic work. It only changes how `ToString()` and `Enum.Parse` format the value (`"Wifi, Breakfast"` instead of `"3"`). The combining, testing and storing all come from the bit values *you* assign. Forget to use powers of two and the attribute will happily print nonsense. The attribute is documentation and formatting; the powers of two are the mechanism.

---

## 1.5 Working with flags in C#

```csharp
// Build a set
var amenities = RoomAmenities.Wifi | RoomAmenities.Breakfast | RoomAmenities.Parking;

// Add an element (idempotent union)
amenities |= RoomAmenities.Pool;

// Remove an element (set difference)
amenities &= ~RoomAmenities.Breakfast;

// Toggle
amenities ^= RoomAmenities.PetFriendly;

// Test membership — "does it have ALL of these?"
bool hasWifi      = (amenities & RoomAmenities.Wifi) == RoomAmenities.Wifi;
bool isResortLike = (amenities & RoomAmenities.Resort) == RoomAmenities.Resort;

// Test "ANY of these?"
bool hasAnyMeal   = (amenities & RoomAmenities.Breakfast) != 0;

// Readable equivalent (see caveat below)
bool hasPool      = amenities.HasFlag(RoomAmenities.Pool);
```

> **`HasFlag` caveats.** `value.HasFlag(flag)` is exactly `(value & flag) == flag` — readable, and correct for "has all bits of `flag`." Two things to remember: (1) `HasFlag(None)` is **always `true`**, because every set contains the empty set; never test for "no amenities" with `HasFlag` — use `value == RoomAmenities.None`. (2) On legacy .NET Framework it boxed and was measurably slower in hot loops; on modern .NET it is fine. In a tight inner loop, the explicit `&` is still the safe choice.

### Counting bits before they bite

`int` has 32 bits, but the 32nd is the sign bit, so a plain `int`-backed flags enum comfortably holds **31** independent flags; need more and you should switch the backing type to `uint` or `long` (64 flags). If you find yourself approaching that ceiling, treat it as a design smell — see §1.9.

---

## 1.6 Persistence: one column, not three tables

Because the value is a single integer, it stores as a single column. In EF Core an enum maps to its underlying numeric type automatically; a `[Flags]` enum is no different:

```csharp
public class Room
{
    public int Id { get; set; }
    public string Number { get; set; } = default!;
    public RoomAmenities Amenities { get; set; }   // ← one column
}

// Right-size the column to match the backing type (Chapter 2):
modelBuilder.Entity<Room>()
    .Property(r => r.Amenities)
    .HasColumnType("tinyint");   // 1 byte: fine for up to 8 amenities
```

The resulting table is just `Rooms(Id, Number, Amenities)`. No `Amenities` table, no `RoomAmenities` join, no extra indexes, no cascade rules. Reads need no join. The set travels as 1–4 bytes on the wire and at rest.

### Querying a bitmask in SQL

```sql
-- Rooms that have BOTH Wi-Fi(1) and Breakfast(2):  mask = 3
SELECT * FROM Rooms WHERE (Amenities & 3) = 3;

-- Rooms that have ANY of Pool(8) or Parking(4):    mask = 12
SELECT * FROM Rooms WHERE (Amenities & 12) <> 0;
```

> **The honest caveat.** A bitwise predicate is **non-sargable**: the database cannot use an ordinary B-tree index on `Amenities` to satisfy `(Amenities & 3) = 3`, so it scans. For a few thousand rooms this is irrelevant. If you operate at a scale where you constantly filter millions of rows by a *single* amenity, that recurring relational query is itself a signal that the amenity may want to be a first-class, indexable thing — which is the §1.8 decision, not a reason to fear flags.

---

## 1.7 The principle: Value Object vs Entity

Flags are the mechanism. The reason they are *correct* here comes from Domain-Driven Design, and it is the single most useful modeling distinction most teams are missing.

**Entity** — something defined by a continuous **identity** that persists through change. A `Booking` is the same booking even after its dates, status, and guest all change; `Booking #4471` yesterday is `Booking #4471` today. Entities have a lifecycle (created, modified, archived) and are compared **by id**.

**Value Object** — something defined entirely by its **values**, with no identity of its own. `Money(120.00, "USD")` is interchangeable with any other `Money(120.00, "USD")`; a `DateRange(Jun 3 → Jun 7)` is just those two dates. Value Objects are **immutable**, compared **by value**, and — crucially for our purposes — **persisted as part of the entity that owns them**, not as independent rows.

Now place "the amenities of a room" on that spectrum. Does a particular *Wi‑Fi-ness* have an identity that we track over time? No. Is the set defined entirely by which options are on? Yes. Is it interchangeable — is this room's `{Wifi, Pool}` the very same value as that room's `{Wifi, Pool}`? Yes. **It is a Value Object.** And Value Objects live *inside* their owner — here, as one column on `Room`.

The many-to-many table from §1.1 mis-promotes a Value Object to an Entity. It hands "amenity" an identity (`Amenities.Id`), a table, a lifecycle, and relationships it does not need, and it pays for that mistake on every read, every write, and every migration.

### The First Normal Form objection — answered honestly

A careful reader will protest: *"Packing a set into one integer violates 1NF — a column must hold a single atomic value, not a collection."*

This deserves a real answer, not a hand-wave. First Normal Form is about the atomicity of the **domain value**, not about whether that value can be *decomposed* by clever math. A bitmask is a single scalar drawn from one domain — exactly like a Unix file mode `0755`, an RGBA colour packed into one `int`, or an IP address stored as a 32-bit number. None of those store a *relation* in the cell; they store one value that happens to have internal structure. You are not putting a table inside a column.

So 1NF is the wrong court to argue this in. The real question — the engineering question — is:

> **Will you ever need to treat each element as a first-class row** that you join to, constrain with foreign keys, attach attributes to, or aggregate over relationally?

If **no**, it is a Value Object; one packed value is correct and 1NF is satisfied. If **yes**, then the element really is an Entity, and you should normalize it into its own table with a genuine relationship. Normalization is a tool for managing *relationships between entities* — and a Value Object, by definition, is not one.

---

## 1.8 The decision, on one screen

```
Is the option set ...
  • closed and known at compile time?        ─┐
  • made of plain on/off members?             │   YES to all
  • free of per-option data (price, text,     ├──────────────►  FLAGS  (Value Object, one column)
    inventory, audit) ?                        │
  • never joined/aggregated per option         │
    at scale?                                  ─┘

  • open / admin-editable at runtime?         ─┐
  • each option has its own attributes?        │   YES to any
  • options have their own lifecycle?          ├──────────────►  ENTITY + many-to-many table
  • you must FK, constrain, or report          │
    on options relationally?                   ─┘
```

### Worked example: the same booking system, modeled correctly

```csharp
// RADIO BUTTON  → plain enum, one value, one (tiny) column
public enum BookingStatus : byte { Pending, Confirmed, CheckedIn, CheckedOut, Cancelled }

// CHECKBOXES, no own data → FLAGS (Value Object), one column
[Flags] public enum NotificationChannels : byte
{ None = 0, Email = 1, Sms = 2, Push = 4, WhatsApp = 8 }

[Flags] public enum RoomAmenities : byte
{ None = 0, Wifi = 1, Breakfast = 2, Parking = 4, Pool = 8, PetFriendly = 16 }

// CHECKBOXES, BUT each option has price + inventory + is admin-editable
//   → these are ENTITIES, and the relationship is a real many-to-many
public class ExtraService                 // Airport pickup, spa, late checkout...
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public Money Price { get; set; } = default!;   // own attribute → it's an Entity
    public bool IsActive { get; set; }              // own lifecycle
}

public class BookingExtraService          // the justified join table
{
    public int BookingId { get; set; }
    public int ExtraServiceId { get; set; }
    public int Quantity { get; set; }      // relationship even carries its own data
}
```

Notice the booking domain needs **both** shapes, and the choice is not about taste — it is about whether each option carries identity and data. Amenities are a Value Object (flags). Extra services are Entities (table + M:N) because they have a price, can be toggled on by an admin tomorrow, and you must report revenue per service. Same UI checkboxes; different domain truth; different storage.

> **Rule of thumb** — *Checkboxes whose options never need a row of their own → flags. Checkboxes whose options are things in their own right → a table.* Ask "does this option have a price, a description users see, an admin who edits it, or a report that counts it?" One yes, and it is an Entity.

---

## 1.9 When **not** to use flags

Flags are a precision tool, not a hammer. Prefer the relational model when:

- **The set is open.** Options are added by users/admins at runtime. Bits are assigned by a programmer at compile time; you cannot ship a deploy every time marketing invents a feature.
- **Options carry data.** The moment an "option" needs a price, a localized label, an icon, an effective date, or an audit trail, it is an Entity.
- **You exceed the bit budget.** Past ~31 flags (`int`) or 64 (`long`) you are out of bits; needing that many is itself a sign the concept is a catalog, not a flag set.
- **You need relational integrity or per-option analytics.** Foreign keys, `GROUP BY option`, indexed lookups per option — these want rows.
- **Auditing individual toggles matters.** "Who enabled the pool on 12 May?" is an entity/event question, not a bitmask.

And a couple of mechanical warnings: never **renumber existing flags** once values are persisted (the stored integers would silently change meaning), and reserve `0` for `None` forever.

---

## 1.10 Key takeaways

1. A plain `enum` models **one-of-N** (radio). A `[Flags]` enum models **any-subset-of-N** (checkboxes) inside a single integer.
2. Flags work because each member is a **distinct power of two**, making the value a **subset of a finite universe**, with bitwise operators acting as set operators. `[Flags]` only prettifies `ToString()`; the powers of two do the real work.
3. A fixed set of attribute-less options is a **Value Object** — store it **inline, in one right-sized column**, not as an Entity behind a many-to-many table.
4. The "1NF violation" objection is a category error: a bitmask is one atomic scalar. The real test is whether each option needs to be a **first-class row** you join, constrain, attach data to, or report on.
5. Promote to an **Entity + join table** the instant an option earns its own identity, attributes, lifecycle, or relational queries — as `ExtraService` does and `RoomAmenities` does not.

> Next: the same right-sizing instinct, applied to numbers — why defaulting every column to `int` quietly wastes bandwidth, storage, and money, and how to choose between `decimal`, `double`, and `float`. → [Chapter 2](chapters/02-numeric-types.md)
