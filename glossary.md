# Glossary

Plain-language definitions of the jargon used across the book. Terms a junior dev
might bounce off, in one line each. External links are all Microsoft Learn.

## Bits & flags

- **Bit** — a single binary digit, `0` or `1`. An integer is just a row of bits.
- **Bitmask** — one integer whose individual bits each mean yes/no for a different option, so a *set* of options fits in a single number.
- **Power of two** — `1, 2, 4, 8, 16, …`; each lights exactly one distinct bit, which is why flag enum members use them.
- **`1 << n` (left shift)** — slides the bit `1` left by `n` positions, producing the n-th power of two (`1 << 3 == 8`). See [bitwise and shift operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators).
- **`[Flags]` enum** — an enum whose values combine as a bitmask; the attribute only changes formatting (`ToString`/`Parse`). See [`FlagsAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.flagsattribute) and [enum design](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/enum).
- **Boxing** — wrapping a value type (like an enum) in a short-lived heap object; an allocation that used to make `HasFlag` slow on old .NET. See [`Enum.HasFlag`](https://learn.microsoft.com/en-us/dotnet/api/system.enum.hasflag).

## Domain modeling (DDD)

- **Entity** — an object defined by a continuous **identity** that survives change (`Booking #4471`); compared by id.
- **Value Object** — an object defined entirely by its **values**, no identity, immutable, compared by value (`Money(120, "USD")`). See [implementing value objects](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/implement-value-objects).
- **Immutable** — its values never change after construction; to "change" it you build a new one.
- **`readonly record struct`** — a C# immutable value type with built-in value equality — a natural fit for a Value Object.
- **1NF (First Normal Form)** — the rule that a column holds one atomic domain value. A bitmask satisfies it: one scalar, even though math can decompose it.
- **Owned entity type** — EF Core's way to store a multi-field Value Object inside its owner's table (`OwnsOne`). See [owned entity types](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) and [value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions).

## Databases & storage

- **Surrogate key** — an artificial primary key (e.g. an auto-increment `Id`) standing in for the row instead of real-world data.
- **Sargable** — *Search-ARGument-able*: a predicate the database can satisfy with an index. A bitwise filter is **non-sargable**, so it scans.
- **B-tree index** — the ordinary sorted index that powers `=`, `<`, `>` lookups.
- **Page / buffer pool** — the database stores data in fixed-size blocks called *pages*; the **buffer pool** is its in-memory cache of them. Narrower rows = more rows per page = fewer reads.
- **`decimal(p, s)`** — fixed-point type: `p` = total significant digits (precision), `s` = digits after the point (scale). See [decimal and numeric](https://learn.microsoft.com/en-us/sql/t-sql/data-types/decimal-and-numeric-transact-sql).
- **`tinyint` / `smallint` / `int` / `bigint`** — SQL Server integers of 1 / 2 / 4 / 8 bytes. See [int, bigint, smallint, and tinyint](https://learn.microsoft.com/en-us/sql/t-sql/data-types/int-bigint-smallint-and-tinyint-transact-sql).

## Numbers & wire

- **IEEE-754** — the binary floating-point standard behind `float`/`double`; why `0.1 + 0.2 != 0.3`. See [floating-point numeric types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types).
- **FPU (floating-point unit)** — the CPU hardware that runs `float`/`double` fast; `decimal` has no such hardware and runs ~10× slower in software.
- **Precision vs range** — *range* = how big a number can be; *precision* = how many exact digits. `double` has huge range, ~15–17 digits; `decimal` modest range, ~28–29 exact digits.
- **Banker's rounding (`MidpointRounding.ToEven`)** — rounds a `.5` to the nearest **even** digit (2.5→2, 3.5→4) to avoid upward bias; the .NET default. See [`MidpointRounding`](https://learn.microsoft.com/en-us/dotnet/api/system.midpointrounding).
- **CLS-compliant** — usable from any .NET language (the shared Common Language Specification subset); `sbyte`/`uint`/`ulong` are **not**. See [language independence](https://learn.microsoft.com/en-us/dotnet/standard/language-independence).
- **ISO-4217** — the international standard for 3-letter currency codes (`USD`, `EUR`, `VND`).
- **`varint`** — a variable-length integer encoding (e.g. protobuf): small numbers take 1 byte, large ones up to 5.
- **Egress** — outbound data leaving a cloud network; a metered, billed line item.

> Back to the [Introduction](/).
