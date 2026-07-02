# EF Core demos — bitmask queries + optimistic concurrency

Companion to **Chapter 1 §1.6** and **Chapter 3 §3.4**. Part 1 shows the amenity
bitmask queried through EF Core against an **in-memory SQLite** database, and —
the point of that demo — prints the SQL EF Core generates (`ToQueryString()`)
right next to the rows each query returns. Part 2 forces a real optimistic-
concurrency conflict between two `DbContext` instances and shows the genuine
`DbUpdateConcurrencyException` EF Core throws, caught and resolved end to end.

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

## Part 2 — `ConcurrencyDemo.cs` (Chapter 3 §3.4)

Runs automatically after Part 1, on its own isolated in-memory SQLite
database. Seeds one `VersionedRoom`, loads it into **two separate
`DbContext`** instances (simulating two concurrent requests), commits the
first, then commits the second — which is still holding the stale `Version`
it read before the first commit. The second `SaveChanges()` throws a real
`Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`; the demo catches
it, inspects `ex.Entries`, reloads the current database values via
`GetDatabaseValues()`, and prints the resolution.

The token is `[ConcurrencyCheck] public int Version` — **not**
`[Timestamp]`/`.IsRowVersion()`, because that maps to SQL Server's native
`rowversion` type, which SQLite doesn't support. `[ConcurrencyCheck]` is the
provider-agnostic alternative: the app bumps `Version` itself on every save,
and EF Core folds it into the `UPDATE`'s `WHERE` clause regardless of
provider. See [EF Core — Handling concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
