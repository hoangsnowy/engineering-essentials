# EF Core demo — querying a `[Flags]` bitmask

Companion to **Chapter 1 §1.6**. Shows the amenity bitmask queried through EF
Core against an **in-memory SQLite** database, and — the point of the demo —
prints the SQL EF Core generates (`ToQueryString()`) right next to the rows each
query returns.

Separate from [`../demos`](../demos) on purpose: that project is dependency-free,
this one pulls in `Microsoft.EntityFrameworkCore.Sqlite`. Requires the .NET SDK
(targets **net8.0**) and a one-time NuGet restore (needs network).

```bash
cd demos.efcore
dotnet run
```

## What it prints

Four LINQ queries, each with its translated SQL:

| LINQ | Generated SQL (SQLite) |
|---|---|
| `(r.Amenities & Wifi) == Wifi` | `WHERE "r"."Amenities" & 1 = 1` |
| `(r.Amenities & need) == need` (`need = Wifi\|Pool`) | `WHERE "r"."Amenities" & @__need_0 = @__need_0` |
| `(r.Amenities & (Pool\|Parking)) != 0` | `WHERE "r"."Amenities" & 12 <> 0` |
| `r.Amenities.HasFlag(Wifi)` | `WHERE "r"."Amenities" & 1 = 1` |

Takeaways: a `[Flags]` enum maps to **one column**, the LINQ is the same
membership test as in the chapter, EF Core translates it to a bitwise `&`
predicate (including `HasFlag`), and a mask held in a variable becomes a SQL
parameter. See [`Microsoft.EntityFrameworkCore` value conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
and [`ToQueryString()`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.entityframeworkqueryableextensions.toquerystring).
