# 2 · Choosing the Right Numeric Type

> **TL;DR** — Defaulting every number to `int` and every fraction to `double` is two bugs wearing a trenchcoat. The first wastes storage, bandwidth, and money at scale; the second silently corrupts money values. Right-size integers where the cost **multiplies** (database columns, wire payloads, large arrays), and use **`decimal` for money, `double` for measurements, `float` almost never** in business code.

> Hit an unfamiliar term (precision/scale, CLS-compliant, banker's rounding)? The [Glossary](glossary.md) defines each in one line.

---

## 2.1 The symptom

Open almost any schema and you will find it: `Age INT`, `NumberOfGuests INT`, `StarRating INT`, `FloorNumber INT` — every whole number, regardless of its real range, declared as a 4-byte integer because `int` is what the fingers type by default. A few columns over, the more dangerous one: `TotalAmount FLOAT` or `Price FLOAT` — money stored in a binary floating-point type that *cannot represent ten cents exactly*.

Neither mistake fails a unit test on Tuesday. Both cost real money — one on the cloud bill, the other in a reconciliation report that is off by a penny and nobody can explain.

A type is not just "a place to put a number." It is a contract about **range, precision, exactness, storage width, and wire size**. Choosing it deliberately is one of the cheapest forms of engineering leverage there is.

---

## 2.2 Where size actually costs (and where it doesn't)

Be precise about *why* an oversized integer is wasteful, because the intuition "smaller type = less memory" is only sometimes true.

**Where right-sizing pays, because the cost multiplies:**

- **Database columns.** Every row pays the column's width, on the table **and in every index that includes it**, **and** in every backup, replica, and *page* cached in the buffer pool (the database's in-memory cache of fixed-size on-disk blocks called **pages**). Narrow a column from `int` (4 B) to `smallint` (2 B) across **one billion rows** and you save ~2 GB at rest — then again on each replica and backup. Narrower rows also mean **more rows per page**, so the same query reads fewer pages from disk. The saving is multiplied by your entire data footprint.
- **The wire.** An API that returns a million records pays for every needless byte in latency and *egress* (outbound data leaving the cloud, a metered line item). Binary protocols make this sharper: a protobuf `varint` (variable-length integer encoding) packs a small number into one byte and a large one into five, so the *value* and *type* directly set the payload size.
- **Large arrays / columnar data.** `short[10_000_000]` is 20 MB; `int[10_000_000]` is 40 MB. The difference is real, linear, and — because it halves cache pressure — often shows up as raw speed.

**Where right-sizing a single in-memory field usually does *not* pay:**

- A lone `short` field inside a class buys little. The CLR aligns fields to word-sized addresses and adds object overhead (header + padding), so one `short` among `int`s and references is typically padded back up to a 4- or 8-byte boundary. You spent clarity and gained no RAM.

> **Rule of thumb** — Optimize the integer type at the boundaries that **multiply** the cost: persisted columns, serialized DTOs, and big arrays. For ordinary local variables and loop counters, prefer plain `int` — it is the CPU's natural word and the most readable default. Right-sizing is about *where the number lives at scale*, not about shaving bytes off every variable.

---

## 2.3 The integer ladder

| C# type | Bytes | Range | SQL Server | Booking-domain fit |
|---|---|---|---|---|
| `byte` | 1 | 0 … 255 | `tinyint` | guests, nights, star rating, floor, room count |
| `short` (`Int16`) | 2 | −32,768 … 32,767 | `smallint` | rooms-in-hotel, days-ahead, small catalog ids |
| `int` (`Int32`) | 4 | ±~2.15 billion | `int` | most surrogate keys, moderate counters |
| `long` (`Int64`) | 8 | ±~9.2 × 10¹⁸ | `bigint` | high-volume keys (bookings at global scale), epoch ms |

A few sharp edges worth knowing:

- **`tinyint` is unsigned `0–255`** in SQL Server and maps cleanly to C# `byte`. There is no 1-byte *signed* SQL type, and C# `sbyte`/`uint`/`ulong` are not *CLS-compliant* — i.e. not guaranteed usable from every .NET language (the [Common Language Specification](https://learn.microsoft.com/en-us/dotnet/standard/language-independence) is the shared subset all languages support) — so for public APIs prefer the signed CLS types and pick width by range.
- **`NumberOfGuests INT` is 4 bytes to store a value that never exceeds, what, 20?** `tinyint` (1 byte) covers it with a built-in `0–255` sanity bound. The type itself documents and enforces the domain.
- **Surrogate keys: think about the ceiling.** A *surrogate key* is an artificial primary key — typically an auto-increment `Id` — that stands in for the row instead of any real-world data. `int` tops out near 2.15 billion. For `Booking.Id` in a busy global product that can be a real horizon; `bigint` is the safe default for high-volume tables, and the 4 extra bytes per row are justified there precisely because they *aren't* justified everywhere.

```csharp
public class Booking
{
    public long Id { get; set; }            // bigint — this table grows forever
    public byte  NumberOfGuests { get; set; } // 0–255 is plenty; documents the domain
    public byte  Nights { get; set; }         // a stay isn't 40,000 nights
}

public class Hotel
{
    public int  Id { get; set; }            // catalogs are small; int is generous
    public byte StarRating { get; set; }    // 1–5
}
```

(See [Integral numeric types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types) and SQL Server [int, bigint, smallint, and tinyint](https://learn.microsoft.com/en-us/sql/t-sql/data-types/int-bigint-smallint-and-tinyint-transact-sql).)

---

## 2.4 Floating point vs decimal — three types, two different jobs

For fractional numbers, .NET gives three choices. The split that matters is **base-2 vs base-10**, because it decides *exactness*.

| Type | Bytes | Base | Sig. digits | Range (approx) | Speed |
|---|---|---|---|---|---|
| `float` (`Single`) | 4 | 2 (IEEE-754) | ~7 | ±3.4 × 10³⁸ | fastest |
| `double` | 8 | 2 (IEEE-754) | ~15–17 | ±1.7 × 10³⁰⁸ | fast (hardware FPU) |
| `decimal` | 16 | **10** | 28–29 | ±7.9 × 10²⁸ | ~10× slower (software) |

`float` and `double` are **binary** floating point: fast, enormous range, and **approximate** for most decimal fractions. `decimal` is **base-10** floating point: it represents decimal fractions *exactly*, at the cost of speed and range. They are tools for different jobs, not interchangeable sizes of the same tool. (The FPU above is the CPU's hardware *floating-point unit*; `decimal` has no such hardware and runs in software.)

### Why `double` can't hold ten cents

```csharp
Console.WriteLine(0.1 + 0.2);          // 0.30000000000000004
Console.WriteLine(0.1 + 0.2 == 0.3);   // False
```

This is not a .NET bug; it is arithmetic. In base-2, `0.1` is the repeating fraction `0.0001100110011…` — it has no finite binary representation, exactly as `1/3 = 0.333…` has no finite decimal one. The 64 bits of a `double` store the **nearest representable value**, a hair off. Do arithmetic and those hairs accumulate:

```csharp
double sum = 0.0;
for (int i = 0; i < 10; i++) sum += 0.1;   // ten ten-cent items
Console.WriteLine(sum);          // 0.9999999999999999
Console.WriteLine(sum == 1.0);   // False  ← your invoice is now "wrong"
```

`decimal` stores `0.1` *as* one-tenth, in base-10, exactly:

```csharp
decimal sum = 0m;
for (int i = 0; i < 10; i++) sum += 0.1m;
Console.WriteLine(sum);          // 1.0
Console.WriteLine(sum == 1.0m);  // True
```

> **Warning** — **Never store or compute money in `float`/`double`.** The errors are tiny per operation and catastrophic in aggregate: prices that don't sum to the total, tax that's a cent off, balances that fail to reconcile, and rounding that quietly favors one side. This is a *correctness* defect, not a precision preference.

### Range vs precision — they are different axes

`double` has a colossal **range** (~10³⁰⁸) but only ~15–17 significant **digits**. `decimal` has a modest range (~10²⁸) but ~28–29 exact digits within it. So the question is never "which is bigger" — it's *which axis your value needs*. Money needs exact digits in a human-sized range → `decimal`. A physics simulation needs vast range and tolerates relative error → `double`. (See [Floating-point numeric types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types), [`System.Decimal`](https://learn.microsoft.com/en-us/dotnet/api/system.decimal), [`System.Double`](https://learn.microsoft.com/en-us/dotnet/api/system.double).)

---

## 2.5 The money type, end to end

Money is the canonical `decimal` case, and it ties back to [Chapter 1](chapters/01-enum-flags.md): an amount and its currency form a **Value Object**.

```csharp
// readonly record struct: an immutable value type with built-in value equality —
// exactly the shape of a Value Object (no identity, compared by its values).
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money operator +(Money a, Money b) =>
        a.Currency == b.Currency
            ? new Money(a.Amount + b.Amount, a.Currency)
            : throw new InvalidOperationException("Cannot add different currencies.");

    public Money RoundToCents() =>
        this with { Amount = Math.Round(Amount, 2, MidpointRounding.ToEven) };
}

public class Booking
{
    public long  Id { get; set; }
    public Money Total { get; set; }   // owned Value Object → two columns
}
```

`MidpointRounding.ToEven` above is **banker's rounding** — a `.5` rounds to the nearest *even* digit (2.5 → 2, 3.5 → 4), which avoids the upward bias of always rounding `.5` up. It is the .NET default; choose it deliberately for money. (See [`MidpointRounding`](https://learn.microsoft.com/en-us/dotnet/api/system.midpointrounding).)

Map it to an exact SQL column — and right-size the precision/scale, just as you would an integer's width:

```csharp
modelBuilder.Entity<Booking>().OwnsOne(b => b.Total, t =>
{
    t.Property(p => p.Amount)
     .HasColumnType("decimal(19,4)");          // 19 total digits, 4 after the point
    t.Property(p => p.Currency).HasMaxLength(3); // ISO-4217: "USD"
});
```

> **`decimal(p, s)` decoded.** `p` (**precision**) is the total number of significant digits; `s` (**scale**) is how many sit after the decimal point. `decimal(19,4)` holds up to 15 digits before the point and 4 after — generous for money in any currency. Pick scale from the currency (most use 2–4 places); pick precision from the largest total you could ever sum. (`ISO-4217` is the international standard for 3-letter currency codes — `USD`, `EUR`, `VND`.)

> **Mapping a struct Value Object — a caveat.** `OwnsOne` works well for value objects, but a `readonly record struct` can hit friction with EF Core's change tracking and nullability in some versions. If you meet it, the pragmatic options are a `record class`, or a [value converter](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions) that serializes the value into one column. See [Owned entity types](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) for the full picture.

**Why `decimal(19,4)` and not the `money` type?** SQL Server's [`money`](https://learn.microsoft.com/en-us/sql/t-sql/data-types/money-and-smallmoney-transact-sql) works, but it fixes scale at 4, has historically surprised people with intermediate-rounding quirks, and is less portable across databases — Microsoft's own docs warn to *"avoid using this data type if your money values are used in calculations… use the `decimal` data type"*. An explicit [`decimal(p,s)`](https://learn.microsoft.com/en-us/sql/t-sql/data-types/decimal-and-numeric-transact-sql) states your intent, travels to other engines, and gives you control over precision and scale. Many teams standardize on `decimal(19,4)` (or `decimal(18,2)`) for exactly those reasons. Use what your shop standardizes on — just make it an explicit base-10 type, never `float`.

---

## 2.6 So when *is* `double` the right answer?

`decimal` is for **counted, exact** quantities (money, anything you reconcile). `double` is for **measured, continuous** quantities where a tiny relative error is physically meaningless and range/speed matter:

- **Geo-coordinates** — a hotel's latitude/longitude. `double` is standard; the ~15 digits dwarf GPS accuracy. (In SQL you might store `decimal(9,6)` for tidy fixed precision, or a `geography` type for spatial queries.)
- **Statistics & science** — an average review score, a distance, a load factor, an ML feature. These are estimates; binary floating point is the right, hardware-accelerated tool.
- **Anything performance-critical and tolerant** — large numeric arrays, graphics, signal processing.

`float` (32-bit) is rarer still in line-of-business code. Its ~7 significant digits are too few for most quantities; reach for it only when memory or bandwidth dominates **and** low precision is acceptable — huge numeric arrays, GPU/ML tensors, 3-D vertex data. For a single field, `double` is the saner default.

> **Performance footnote** — `double` runs on the CPU's floating-point unit; `decimal` is implemented in software and is roughly an order of magnitude slower per operation. This matters in tight numeric loops, not in the once-per-request arithmetic of a booking total. **Correctness first** — choose `decimal` for money even though it's slower; choose `double` for a simulation even though it's approximate. Speed breaks the tie only *within* the correct category.

> **Try it yourself.** The repo ships a runnable console demo — [`demos/Chapter02NumericTypes.cs`](https://github.com/hoangsnowy/engineering-essentials/blob/main/demos/Chapter02NumericTypes.cs). From the `demos/` folder run `dotnet run -- 2`. Predict first:
> 1. What does `0.1 + 0.2 == 0.3` print, and what does `0.1m + 0.2m == 0.3m` print?  *(False, then True)*
> 2. Summing `0.1` ten times in `double` — what do you get?  *(0.9999999999999999)*
> 3. `Math.Round(2.5m)` and `Math.Round(3.5m)` under the default rounding?  *(2 and 4 — banker's rounding)*

---

## 2.7 The cheat sheet

| The value is… | Use | SQL | Why |
|---|---|---|---|
| Money / price / tax / balance | `decimal` | `decimal(19,4)` | exact base-10; reconciles to the cent |
| Guests, nights, star rating, floor | `byte` | `tinyint` | range ≤ 255; documents + bounds the domain |
| Rooms per hotel, small catalogs | `short` | `smallint` | range ≤ 32k; half the width of `int` |
| Most surrogate keys, counters | `int` | `int` | natural word; generous range |
| Keys on huge tables, epoch ms | `long` | `bigint` | won't exhaust at scale |
| Latitude / longitude | `double` | `decimal(9,6)` / `geography` | measured; precision far exceeds GPS |
| Average rating, stats, science | `double` | `float(53)` | continuous estimate; fast |
| Ratio in 0–1 you display | `decimal` | `decimal(5,4)` | exact when shown/compared to humans (store percentages as a 0–1 ratio, or use `decimal(7,4)` for 0–100) |
| Big numeric arrays, ML tensors | `float` | `real` | memory/bandwidth dominate; precision tolerant |

---

## 2.8 Key takeaways

1. **A type is a contract** about range, exactness, storage width, and wire size — pick it on purpose, not by reflex.
2. **Right-size integers where the cost multiplies** — database columns, serialized payloads, and large arrays — and let plain `int` be the default for ordinary in-memory locals.
3. **`int` everywhere is waste**; `NumberOfGuests` is a `byte`, a high-volume key is a `long`. The type also *documents and bounds* the domain.
4. **`double` is binary and approximate; `decimal` is base-10 and exact.** `0.1 + 0.2 != 0.3` is arithmetic, not a bug.
5. **Money is always `decimal`** (a Value Object of amount + currency, stored as `decimal(p,s)`); **`double` is for measured, continuous quantities**; **`float` is a rare memory-vs-precision trade.** Choose for correctness first; let speed break ties only within the correct category.

---

## 2.9 References (Microsoft Learn)

- [Integral numeric types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types)
- [Floating-point numeric types (C# reference)](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types)
- [`System.Decimal`](https://learn.microsoft.com/en-us/dotnet/api/system.decimal) · [`System.Double`](https://learn.microsoft.com/en-us/dotnet/api/system.double) · [`System.Single`](https://learn.microsoft.com/en-us/dotnet/api/system.single) · [`MidpointRounding`](https://learn.microsoft.com/en-us/dotnet/api/system.midpointrounding)
- [Language independence and the CLS](https://learn.microsoft.com/en-us/dotnet/standard/language-independence)
- SQL Server: [int, bigint, smallint, and tinyint](https://learn.microsoft.com/en-us/sql/t-sql/data-types/int-bigint-smallint-and-tinyint-transact-sql) · [decimal and numeric](https://learn.microsoft.com/en-us/sql/t-sql/data-types/decimal-and-numeric-transact-sql) · [money and smallmoney](https://learn.microsoft.com/en-us/sql/t-sql/data-types/money-and-smallmoney-transact-sql)

> Back to the [Introduction](/) · Previous: [Chapter 1 — Enum Flags & Domain Modeling](chapters/01-enum-flags.md)
