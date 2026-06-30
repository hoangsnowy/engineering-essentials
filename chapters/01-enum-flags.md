# 1 · Enum Flags & Domain Modeling

> **TL;DR** — A plain `enum` is a *radio button*: exactly one choice. A `[Flags]` enum is a group of *checkboxes*: many choices packed into a single integer. When a concept is a **fixed, closed set of boolean options with no data of its own**, it is a *Value Object* (a domain value with no identity, see §1.7) and belongs in **one column**, not in a separate table joined many-to-many. Reach for a real entity and a join table only when each option earns its own identity, attributes, and lifecycle.

> Hit an unfamiliar term (bitmask, sargable, Value Object)? The [Glossary](glossary.md) defines each in one line.

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

The amenities of a room are not five *things* with independent lives. They are **one fact** about the room: *which subset of a known menu does this room offer?* That fact is a single value (a *Value Object* — we make this precise in §1.7). Modeling it as a relationship is like storing a person's RGB favourite-colour as three rows in a `ColorChannels` table.

This chapter is about seeing the difference, and the two tools that express it correctly: **enum flags** (the mechanism) and the **Value Object vs Entity** distinction from Domain-Driven Design (the principle behind the mechanism).

We build it in that order on purpose. First we make flags *concrete* — bits, set algebra, one column, one SQL predicate — so the principle has something to stand on. Then §1.7 names *why* it was right all along, and §1.8–1.9 turn that into a decision you can make in seconds. Keep one question in your pocket the whole way: **does each option deserve a row of its own?** Everything below is an answer to it.

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

> **See it move.** The [🏨 interactive hotel search](https://hoangsnowy.github.io/engineering-essentials/hotel-search.html) wires this up live: the room-type **radio** is a plain enum (one value), the amenity **checkboxes** are a `[Flags]` set — tick them and watch the single integer, the generated SQL, and the matching rooms update in real time.

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

A flags enum exploits a simple fact most of us look straight past: an integer is not just a quantity, it is *a row of independent switches*. Bit 0 doesn't know or care what bit 3 is doing. Read the number as a magnitude and `7` is "seven"; read the same bits as switches and `7` is "the first three are on." A flags enum is nothing more than the decision to read the bits the second way — and once you do, each member can own one switch. If every member is assigned a **distinct power of two**, then each member owns exactly one bit, and any *combination* of members is just those bits turned on together.

Each member below is written `1 << n` — the number `1` *shifted left* `n` bit positions. Picture `1` as a single lit bulb in a row of switches; `1 << 2` slides that lit bulb two slots left, leaving `0b100` = `4`. Because every shift moves the one lit bulb to a fresh, empty slot, `1 << 0` is `1`, `1 << 1` is `2`, `1 << 2` is `4`, and so on: successive powers of two, each lighting exactly one bit — and no two members ever share a slot. (Shifting is covered in the C# [bitwise and shift operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators) reference.)

```csharp
[Flags]
public enum RoomAmenities : byte   // 8 bits — plenty for a small, fixed amenity menu
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

A room that offers Wi‑Fi, breakfast and parking is stored as `1 | 2 | 4 = 7` — a single number, `0b0000_0111`. Read the bits back and you recover the exact set:

```
bit:     …  4    3    2    1    0
flag:    … Pet  Pool Park Bfast Wifi
value 7: …  0    0    1    1    1     → { Wifi, Breakfast, Parking }
```

> **Note — the backing type is part of the contract.** We chose `: byte` because the amenity menu is small and the value is persisted (§1.6). Once rows exist, **never change the backing type or renumber members** — the stored integers would silently change meaning. Pick the width once, deliberately ([Chapter 2](chapters/02-numeric-types.md)).

### Set theory, hiding in plain sight

This is not a trick; it is finite-set algebra. A flags enum defines a **universe** — the full menu of `n` things that *could* be selected (here, the five amenities) — and any stored value is just a **subset** you actually ticked. The bitwise operators *are* the set operators:

| Set operation | Meaning | Bitwise | C# |
|---|---|---|---|
| Union (A ∪ B) | combine two sets | OR | `a \| b` |
| Intersection (A ∩ B) | common elements | AND | `a & b` |
| Difference (A \ B) | remove B from A | AND NOT | `a & ~b` |
| Symmetric diff | toggle | XOR | `a ^ b` |
| Membership (x ∈ A) | does A contain x? | AND, compare | `(a & x) == x` |
| Complement (¬A) | everything not in A | NOT | `~a` |
| Empty set (∅) | nothing selected | — | `None` (0) |

With `n` independent bits there are exactly `2ⁿ` representable subsets. Five amenities → 2⁵ = 32 possible rooms-by-amenity, all addressable by a single small integer. Why `2ⁿ`? Each bit is an independent yes/no, so adding one more amenity *doubles* the rooms you can describe — every old combination, now also with-or-without the new one. One small integer, an entire menu folded inside it. That density is the whole point — but it isn't the deepest part. The deeper part is that there is *no encoding step*: the integer doesn't represent the subset the way a JSON string would; under the set-reading of the bits, it **is** the subset. Equality of values is equality of sets, for free — which is exactly why, three sections from now, this turns out to be a Value Object.

### Why powers of two, and why `None = 0`

If two members shared a bit (say both equalled `3`), you could never tell them apart — turning one on would silently turn the other on. Distinct powers of two guarantee one bit per member, so the subset is unambiguous. And `None = 0` is the empty set: the natural, queryable representation of "no amenities," and the identity element for `|` (OR-ing `None` changes nothing). Microsoft's [Enum design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum) spell out both rules: *use powers of two*, and *name the zero value `None`*.

> **Pitfall** — `[Flags]` does **not** make the arithmetic work. It only changes how `ToString()` and `Enum.Parse` format the value (`"Wifi, Breakfast"` instead of `"3"`). The combining, testing and storing all come from the bit values *you* assign. Forget to use powers of two and the attribute will happily print nonsense. The attribute is documentation and formatting; the powers of two are the mechanism. (See [`System.FlagsAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.flagsattribute).)

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

> **`HasFlag` caveats.** `value.HasFlag(flag)` is exactly `(value & flag) == flag` — readable, and correct for "has all bits of `flag`." Two things to remember: (1) `HasFlag(None)` is **always `true`**, because every set contains the empty set; never test for "no amenities" with `HasFlag` — use `value == RoomAmenities.None`. (2) On legacy .NET Framework it *boxed* (wrapped the enum value in a short-lived heap object, adding an allocation) and was measurably slower in hot loops; on modern .NET it is fine. In a tight inner loop, the explicit `&` is still the safe choice. (See [`Enum.HasFlag`](https://learn.microsoft.com/en-us/dotnet/api/system.enum.hasflag).)

Flags values are also effectively **immutable** the way `int` is. `amenities |= Pool` reads like it edits a set in place, but nothing in memory is altered: it computes a *brand-new* number and points the variable at it — exactly like `i = i + 1` doesn't change the old `i`, it replaces it. (Contrast a `List<T>`, where `.Add()` really does mutate the one list everyone shares.) This isn't an accident of the type; it's forced by the meaning. You cannot *edit* the number `7`, and "the set {Wifi, Breakfast}" is a fact, not an object with a slot you reach into — to get a different set you state a different one. That inability to be mutated in place is the first quiet sign you're holding a *Value Object* (§1.7), not an entity.

Hold onto that immutability — it is also what lets the next section store the whole set as a single, unremarkable number.

### Counting bits before they bite

An `int` has 32 bits, so an `int`-backed flags enum holds **32** independent flags — all of them usable. The only wrinkle: the 32nd flag is `1 << 31`, which flips the sign bit, so the *stored value goes negative*. That surprises people who inspect the raw number or filter it in plain SQL, but the bit logic still works perfectly. If you want every stored value to stay positive, stop at 31, or switch the backing type to `uint` or `long` (64 flags). Our `byte`-backed `RoomAmenities` tops out at 8 flags — more than enough for an amenity menu. Microsoft's [enum guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum) say the same: reach past 32 flags and the concept is probably a catalog, not a flag set — see §1.9.

> **Try it yourself.** The repo ships a runnable console demo — [`demos/Chapter01EnumFlags.cs`](https://github.com/hoangsnowy/engineering-essentials/blob/main/demos/Chapter01EnumFlags.cs). From the `demos/` folder run `dotnet run -- 1`. Before you do, **predict the output**:
> 1. What integer is `(int)(RoomAmenities.Wifi | RoomAmenities.Parking | RoomAmenities.PetFriendly)`?  *(answer: 1 + 4 + 16 = 21)*
> 2. For a `{ Wifi, Breakfast }` room, is `(amenities & Resort) == Resort` true or false, and why?  *(false — `Resort` also needs Parking and Pool)*
> 3. What does `amenities.HasFlag(RoomAmenities.None)` return?  *(true — the empty-set trap)*

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
    .HasColumnType("tinyint");   // 1 byte: matches the `: byte` enum, fits up to 8 amenities
```

The resulting table is just `Rooms(Id, Number, Amenities)`. No `Amenities` table, no `RoomAmenities` join, no extra indexes, no cascade rules. Reads need no join. The set travels as a single byte on the wire and at rest.

> **Why this maps automatically — and when it won't.** A flags enum persists with no extra config because it is *already a single scalar* (one integer). A multi-field Value Object — like `Money` (amount + currency, §1.7 and [Chapter 2](chapters/02-numeric-types.md)) — is also stored inside its owner, but because it spans more than one column EF Core needs you to say so explicitly with [owned entity types (`OwnsOne`)](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) or a [value converter](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions). Same DDD idea, different EF Core plumbing.

### Querying a bitmask in SQL

The classic question is *"give me every room with Wi-Fi."* A Wi-Fi room can be stored as **many different integers** — `1` ({Wifi}), `3` ({Wifi, Breakfast}), `21` ({Wifi, Parking, PetFriendly}) — because the *other* amenities set *other* bits. So how does one predicate catch them all, and how does it ignore the bits you didn't ask about?

Mask away everything except the bit you care about. Think of the mask as a stencil: a `1` is a hole that lets that bit show through, a `0` is solid and blocks it. `Amenities & 1` has a hole only over bit 0 (Wi-Fi) and blocks every other bit, because AND-ing any bit with `0` yields `0`. The result is `1` exactly when Wi-Fi is on — whatever the rest of the bits are:

| Room | Amenities (set) | Stored | `& 1` | Has Wi-Fi? |
|---|---|---|---|---|
| 101 | {Wifi} | `1` = `0b00001` | `1` | ✓ |
| 102 | {Wifi, Breakfast} | `3` = `0b00011` | `1` | ✓ |
| 103 | {Breakfast, Parking} | `6` = `0b00110` | `0` | ✗ |
| 104 | {Wifi, Parking, PetFriendly} | `21` = `0b10101` | `1` | ✓ |
| 105 | {Pool} | `8` = `0b01000` | `0` | ✗ |

```sql
-- Every room with Wi-Fi (bit 0), regardless of any other amenity:
SELECT * FROM Rooms WHERE (Amenities & 1) = 1;     -- rows 101, 102, 104

-- BOTH Wi-Fi(1) AND Pool(8):    mask = 9,  result must EQUAL the mask
SELECT * FROM Rooms WHERE (Amenities & 9) = 9;

-- ANY of Pool(8) or Parking(4): mask = 12, result just non-zero
SELECT * FROM Rooms WHERE (Amenities & 12) <> 0;
```

The pattern generalizes: **AND with a mask projects the value onto only the bits in that mask**, so the bits you didn't ask about literally cannot affect the answer. That projection is the whole idea behind a mask — it's how you ask a precise question of a value that carries more than you care about right now. "Does this room have Wi-Fi?" should not depend on whether it also has a pool, and `& 1` is exactly the act of putting your hand over every bit but the one in the question. `(Amenities & mask) = mask` means "has **all** of mask"; `(Amenities & mask) <> 0` means "has **any** of mask". That is exactly the *Membership* row of the §1.4 table, applied to one or several bits at once. The `&` is SQL Server's [bitwise AND operator](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/bitwise-operators-transact-sql) — the same set-membership test as in C#.

In EF Core you write the same test in LINQ and it translates to that SQL `&` predicate:

```csharp
// WHERE (Amenities & 1) = 1
var withWifi = db.Rooms
    .Where(r => (r.Amenities & RoomAmenities.Wifi) == RoomAmenities.Wifi)
    .ToList();

// EF Core also translates HasFlag to the same bitwise predicate:
var withWifiToo = db.Rooms
    .Where(r => r.Amenities.HasFlag(RoomAmenities.Wifi))
    .ToList();
```

> **The honest caveat.** A bitwise predicate is **non-sargable**: the database can't use an ordinary B-tree index on `Amenities` to jump straight to matching rows, so it reads every row and tests the bits (a full *scan*). ("Sargable" = *Search-ARGument-able*, i.e. index-usable; a B-tree is the ordinary sorted index that powers `=`/`<`/`>` lookups.) For a few thousand rooms this is irrelevant. If you operate at a scale where you constantly filter millions of rows by a *single* amenity, that recurring relational query is itself a signal that the amenity may want to be a first-class, indexable thing — which is the §1.8 decision, not a reason to fear flags.

---

## 1.7 The principle: Value Object vs Entity

Flags are the mechanism. The reason they are *correct* here comes from Domain-Driven Design, and it is the single most useful modeling distinction most teams are missing.

**Entity** — something defined by a continuous **identity** that persists through change. A `Booking` is the same booking even after its dates, status, and guest all change; `Booking #4471` yesterday is `Booking #4471` today. Entities have a lifecycle (created, modified, archived) and are compared **by id**.

**Value Object** — something defined entirely by its **values**, with no identity of its own. `Money(120.00, "USD")` is interchangeable with any other `Money(120.00, "USD")`; a `DateRange(Jun 3 → Jun 7)` is just those two dates. Value Objects are **immutable** (their values never change after construction — to "change" one you build a new one) and compared **by value**. In the common case they are **persisted as part of the entity that owns them** rather than as independent rows — though that is a persistence choice, not part of the definition. Microsoft's DDD guide, [Implementing value objects](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/implement-value-objects), works through both the equality semantics and the EF Core mapping.

Here is the one question that collapses the whole distinction into something you can apply at a glance: *if I replaced this with an identical copy, would anyone notice?* Swap a $120 bill for another $120 bill and nothing is lost — `Money` is a Value Object. Swap `Booking #4471` for a freshly-made booking with the same dates and guest, and you've lost the actual reservation a real person is holding — a `Booking` is an Entity. Identity is precisely *what survives copying, and what copying destroys*.

Now place "the amenities of a room" on that spectrum. Does a particular *Wi‑Fi-ness* have an identity that we track over time? No. Is the set defined entirely by which options are on? Yes. Is it interchangeable — is this room's `{Wifi, Pool}` the very same value as that room's `{Wifi, Pool}`? Yes. **It is a Value Object.** And so it lives *inside* its owner — here, as one column on `Room`.

The many-to-many table from §1.1 mis-promotes a Value Object to an Entity. It hands "amenity" an identity (`Amenities.Id`), a table, a lifecycle, and relationships it does not need, and it pays for that mistake on every read, every write, and every migration.

That is the whole chapter in one sentence: **the code review in §1.1 wasn't wrong about SQL — it was wrong about what kind of thing an amenity is.** The mechanism was never the hard part; the classification is. Which is exactly why the loudest objection to flags turns out to aim at the wrong target.

### The First Normal Form objection — answered honestly

A careful reader will protest: *"Packing a set into one integer violates 1NF — a column must hold a single atomic value, not a collection."*

This deserves a real answer, not a hand-wave. First Normal Form is about the atomicity of the **domain value**, not about whether that value can be *decomposed* by clever math. Atomicity was never about a value being structureless — internal structure is universal. A date has a year, month, and day inside it; a string has characters; a `decimal` has a sign and digits. The test 1NF actually cares about is different: *could you put a comma in this cell* — a list of separate items the database should treat as rows? A bitmask fails that test exactly as cleanly as `0755`, an RGBA colour packed into one `int`, or an IP address stored as a 32-bit number do: each is one value drawn from one domain, not a list. You are not putting a table inside a column.

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

// CHECKBOXES, BUT each option has its own identity, lifecycle, and data
//   → these are ENTITIES, and the relationship is a real many-to-many
public class ExtraService                 // Airport pickup, spa, late checkout...
{
    public int Id { get; set; }                     // its own identity
    public string Name { get; set; } = default!;
    public Money Price { get; set; } = default!;    // an attribute it carries
    public bool IsActive { get; set; }              // its own lifecycle (admin toggles it)
}

public class BookingExtraService          // the justified join table
{
    public int BookingId { get; set; }
    public int ExtraServiceId { get; set; }
    public int Quantity { get; set; }      // relationship even carries its own data
}
```

Notice the booking domain needs **both** shapes, and the choice is not about taste — it is about whether each option carries identity and a lifecycle. Amenities are a Value Object (flags). Extra services are Entities (table + M:N) because they have their own identity, can be toggled on by an admin tomorrow, and you must report revenue per service. Same UI checkboxes; different domain truth; different storage.

> **Rule of thumb** — *Checkboxes whose options never need a row of their own → flags. Checkboxes whose options are things in their own right → a table.* Ask "does this option have a price, a description users see, an admin who edits it, or a report that counts it?" One yes, and it is an Entity.

---

## 1.9 When **not** to use flags

Flags are a precision tool, not a hammer. Each rule below comes with the concrete failure you hit if you ignore it, and the model that fixes it.

### 1.9.1 The set is open — options born at runtime

Bits are assigned by a *programmer* at *compile time*. The moment options are created by users or admins while the app runs, flags break:

```csharp
[Flags] public enum ExtraServices : long
{ None = 0, AirportPickup = 1, Spa = 2, LateCheckout = 4 }   // ← frozen at build time
```

Marketing adds "EV charging" on Tuesday. There is no bit for it, and you cannot mint one without a code change, a redeploy, and a migration — and an admin screen that *creates* services is outright impossible. That is an **Entity catalog**, a table whose rows are born at runtime:

```csharp
public class ExtraService
{
    public int Id { get; set; }                  // an admin INSERTs new rows, no deploy
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; }
}
```

> *If a non-developer can add an option, it is a row, not a bit.*

### 1.9.2 Options carry their own data

A flag is one bit: it can say *on/off* and nothing else. The instant an option needs a price, a localized label, an icon, an effective date, or an audit trail, the bit has nowhere to put it:

```csharp
// Flags can record THAT breakfast is offered — never its price, label, or icon.
[Flags] public enum RoomAmenities : byte { None = 0, Wifi = 1, Breakfast = 2 /* …how much? */ }
```

If the product wants "breakfast +€15, shown with this icon, in the guest's language," that data hangs off an Entity:

```csharp
public class Amenity
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public Money? Surcharge { get; set; }        // its own attribute
    public string IconUrl { get; set; } = default!;
}
```

A plain Wi-Fi/parking checkbox with *no* such data stays a flag — that is exactly the §1.8 line.

### 1.9.3 You exceed the bit budget

`int` gives 32 flag bits, `long` gives 64 (§1.5). A SaaS feature-toggle set that grows to 200 entries has no integer wide enough — and you'd hit the bit-31 sign flip on the way. Needing hundreds of options is itself the signal that the concept is a **catalog**, not a flag set: store it as rows, or as a dedicated set type. Bolting "two `long`s side by side" together is a homemade bitset begging to be a table.

### 1.9.4 You need relational integrity or per-option analytics

The killer is the report. A bitmask packs the answer so tightly that the question can't get back in. Finance asks: *"revenue per extra service this quarter."* With services packed into a bitmask there is no `ServiceId` to group by — you'd unpack every bit of every row in application code. With the join table from §1.8 it's one query:

```sql
-- Trivial with rows; near-impossible on a packed bitmask:
SELECT   s.Name, SUM(bx.Quantity) AS Sold
FROM     BookingExtraService bx
JOIN     ExtraService s ON s.Id = bx.ExtraServiceId
GROUP BY s.Name;
```

Foreign keys, `GROUP BY option`, per-option indexed lookups — these are relational operations, and relations want rows. A bitmask deliberately discards the per-option identity those queries need.

### 1.9.5 Auditing individual toggles matters

A bitmask stores only the *current* set — no actor, no timestamp, no history. It cannot answer *"who enabled the pool on 12 May, and when?"* That is an event stream, its own Entity:

```csharp
public class RoomAmenityChange
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public RoomAmenities Amenity { get; set; }     // which bit changed
    public bool Enabled { get; set; }              // turned on or off
    public string ChangedBy { get; set; } = default!;
    public DateTimeOffset ChangedAt { get; set; }
}
```

The flags column can still hold the *current* state; the history needs its own rows.

Notice the shape of all five cases: each one is an option quietly *acquiring an identity* — a name an admin types, a price, a width, a row to join, a moment in time. The day an option earns one of those, it has stopped being a value and become a thing. Flags model values; the moment you are modeling things, reach for rows.

### Two mechanical traps (even when flags ARE right)

- **Never renumber persisted flags.** Reorder the enum so `Parking` shifts from `4` to `8` and every `4` already in the database silently becomes whatever now owns bit `2`. Stored integers are a contract: *appending* new powers of two is safe, *renumbering* existing ones is data corruption with no error and no warning.
- **Reserve `0` for `None` forever.** `0` is the empty set and the default of a fresh column; give a real amenity the value `0` and "no amenities" becomes indistinguishable from "has amenity X."

---

## 1.10 Key takeaways

1. A plain `enum` models **one-of-N** (radio). A `[Flags]` enum models **any-subset-of-N** (checkboxes) inside a single integer.
2. Flags work because each member is a **distinct power of two**, making the value a **subset of a finite universe**, with bitwise operators acting as set operators. `[Flags]` only prettifies `ToString()`; the powers of two do the real work.
3. A fixed set of attribute-less options is a **Value Object** — store it **inline, in one right-sized column**, not as an Entity behind a many-to-many table.
4. The "1NF violation" objection is a category error: a bitmask is one atomic scalar. The real test is whether each option needs to be a **first-class row** you join, constrain, attach data to, or report on.
5. Promote to an **Entity + join table** the instant an option earns its own identity, attributes, lifecycle, or relational queries — as `ExtraService` does and `RoomAmenities` does not.

---

## 1.11 References (Microsoft Learn)

- [Enumeration types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum)
- [`System.FlagsAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.flagsattribute) · [`Enum.HasFlag`](https://learn.microsoft.com/en-us/dotnet/api/system.enum.hasflag)
- [Bitwise and shift operators (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators)
- [Enum design — Framework design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum)
- [Implementing value objects — .NET DDD guide](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/implement-value-objects)
- [EF Core — Owned entity types](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) · [Value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [Bitwise operators (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/bitwise-operators-transact-sql) · [int, bigint, smallint, and tinyint (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/data-types/int-bigint-smallint-and-tinyint-transact-sql)

> Next: the same right-sizing instinct, applied to numbers — why defaulting every column to `int` quietly wastes bandwidth, storage, and money, and how to choose between `decimal`, `double`, and `float`. → [Chapter 2](chapters/02-numeric-types.md)
