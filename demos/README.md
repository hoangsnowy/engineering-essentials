# Demos — Engineering Essentials

Runnable C# console demos for the tech-sharing session. Each prints its results
so you can run it live and let the room predict the output first.

Requires the .NET SDK (any of 8 / 9 / 10 — the project targets **net8.0**).

```bash
cd demos

dotnet run          # run both chapters
dotnet run -- 1     # Chapter 1 — Enum Flags & Domain Modeling
dotnet run -- 2     # Chapter 2 — Numeric Types
```

## What each chapter shows

**Chapter 1 — `Chapter01EnumFlags.cs`**
- Plain enum (radio button) vs `[Flags]` enum (checkboxes) packed into one integer
- `1 << n` powers-of-two mechanism, named combinations (`Standard`, `Resort`)
- Bitwise operators as set algebra (union / difference / toggle / membership)
- The `HasFlag(None) == true` trap and the correct empty test
- The honest bit budget (`1 << 31` is negative — *not* the "only 31 bits" myth)
- Value Object (`Money`, equal by value) vs Entity (`ExtraService`, identity + lifecycle)

**Chapter 2 — `Chapter02NumericTypes.cs`**
- Where integer width multiplies cost (1B rows: `int` 4 GB vs `short` 2 GB)
- `0.1 + 0.2 != 0.3` and `0.1` summed ten times in `double` vs `decimal`
- Money in `double` failing to reconcile
- Range vs precision as different axes
- `MidpointRounding.ToEven` (banker's rounding) vs `AwayFromZero`

> Note: this project is deliberately self-contained (no EF Core / database) so it
> runs with `dotnet run` and nothing else. For a runnable **EF Core** version that
> queries the amenity bitmask against SQLite and prints the generated SQL, see the
> sibling project [`../demos.efcore`](../demos.efcore).
