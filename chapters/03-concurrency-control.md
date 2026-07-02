# 3 · Concurrency Control & the Double-Booking Bug

> **TL;DR** — Two guests can both receive a "confirmed" booking for the same room, same night, because "check availability, then insert" is two separate database round-trips with a gap between them, and under load a second thread slips through that gap before the first one closes it. Wrapping the check-then-act in a transaction does not fix this on SQL Server's default `READ COMMITTED` isolation — that level stops *dirty reads*, not *lost updates*. The real fix is to make the row itself the referee: **pessimistic locking** (`SELECT … WITH (UPDLOCK, ROWLOCK)`) when conflicts are common, **optimistic concurrency** (a version/rowversion column EF Core checks on every `UPDATE`) when they are rare, and a **unique index** in the database as the fact-check neither application layer can skip.

> Hit an unfamiliar term (lost update, isolation level, concurrency token)? The [Glossary](glossary.md) defines each in one line.

---

## 3.1 The symptom

Room 214 at the Riverside Hotel has one bed and one night — the 12th of July — up for grabs. At 9:14:03 on a Tuesday morning, two guests, on two different continents, both tap "Confirm booking" within the same second. The support ticket that lands on your desk three days later reads: *"I was charged, I have a confirmation email, and when I arrived the front desk said someone else was already in my room."* You pull up the `Bookings` table and there they are — two rows, `BookingStatus.Confirmed`, same `RoomId`, same `CheckIn` date, both perfectly valid according to every constraint you wrote. Your test suite is green. It was green yesterday, and it will be green tomorrow, because the bug does not live in any single request — it lives in the *gap between two requests that happen to overlap in time*.

This is the shape of almost every concurrency bug: code that is correct when read top-to-bottom, in isolation, on one thread — and wrong the instant two threads read it at the same moment. A unit test calls your booking method once, watches it run start to finish, and declares victory. It never asks the question that matters in production: *what if another call started while this one was still in the middle of finishing?* That question has no answer in sequential code, because sequential code has no concept of "in the middle" — it either hasn't run yet or it's done. Concurrent code lives entirely in the middle, and the space between "I checked" and "I acted" is exactly where a second guest can walk in.

Put precisely: your availability check and your booking insert are not one operation, they are two, separated by a round-trip to the database and back, and the database gives no guarantee that the world hasn't changed between them. Two people reading the same "Room 214 is free" answer at the same instant is not a bug in either reader — it's a bug in treating *read* and *act* as if they were atomic when they are not. The rest of this chapter answers one question: **how do you make "check, then act" behave as one indivisible step instead of two racable ones?**

---

## 3.2 Anatomy of the bug: check-then-act, and the lost update

The pattern is called **check-then-act** (or, more generally, **read-modify-write**): read some state, decide what to do based on what you read, then write. It looks perfectly safe because *within one call* it is — the danger is entirely in what another, concurrent call can do in the gap. When two concurrent read-modify-write sequences interleave so that the second one's write silently overwrites or ignores the effect of the first, that is a **lost update**: one of the two "successful" operations quietly didn't happen the way its caller believed it did, and nothing in either individual code path reports an error.

Here is the naive C# — the code every junior dev writes first, and the code that passes every single-threaded test:

```csharp
public async Task<Booking> BookRoomAsync(int roomId, DateOnly checkIn, int guestId, byte nights)
{
    // 1) CHECK — read the current state
    bool isTaken = await _db.Bookings.AnyAsync(b =>
        b.RoomId == roomId &&
        b.CheckIn == checkIn &&
        b.Status == BookingStatus.Confirmed);

    if (isTaken)
        throw new InvalidOperationException("Room already booked for that night.");

    // 2) ACT — write, based on a fact that might now be stale
    var booking = new Booking
    {
        RoomId = roomId,
        CheckIn = checkIn,
        GuestId = guestId,
        Nights = nights,
        Status = BookingStatus.Confirmed
    };
    _db.Bookings.Add(booking);
    await _db.SaveChangesAsync();          // ← nothing here re-checks step 1's answer
    return booking;
}
```

And the SQL underneath it is exactly as naive, because it's the same two-step shape one layer down:

```sql
-- Step 1: CHECK
SELECT COUNT(*) FROM Bookings
WHERE RoomId = @roomId AND CheckIn = @checkIn AND Status = 1; -- Confirmed

-- Step 2: ACT (a separate round-trip, no relationship enforced between the two)
INSERT INTO Bookings (RoomId, CheckIn, GuestId, Nights, Status)
VALUES (@roomId, @checkIn, @guestId, @nights, 1);
```

Nothing links these two statements. The database has no memory of "I already told someone the room was free" — it answers each query independently, honestly, and with no idea that its answer is about to be relied upon. Here is the interleaving that produces two confirmed bookings for the same room and night, thread by thread:

| Time | Thread A (Guest 1) | Thread B (Guest 2) | Database state after |
|---|---|---|---|
| t0 | `SELECT COUNT(*) …` → `0` | — | Room 214 / Jul 12: 0 bookings |
| t1 | — | `SELECT COUNT(*) …` → `0` | Room 214 / Jul 12: 0 bookings |
| t2 | evaluates `isTaken = false` | — | (unchanged) |
| t3 | — | evaluates `isTaken = false` | (unchanged) |
| t4 | `INSERT` Booking #501, Confirmed | — | 1 booking |
| t5 | — | `INSERT` Booking #502, Confirmed | **2 bookings, same room, same night** |
| t6 | `SaveChanges()` → success | `SaveChanges()` → success | Both callers believe they won |

Nobody's code lied. Thread B's `SELECT` really did return `0` — at t1, that was true. The mistake was trusting a fact from t1 to still hold at t5, across a gap the database was never asked to protect. This is precisely the [enum-vs-relationship](chapters/01-enum-flags.md) lesson from Chapter 1 wearing a different costume: there, the bug was picking the wrong *shape* for a piece of data; here, it's picking the wrong *shape* for an operation — treating two steps as if they were one. A `BookingStatus` column (§1.3) can tell you a booking's state is `Confirmed`, but no plain column can tell you *whether the read that led to this write is still valid* — that requires either locking the row so no one else can read-and-act on it (§3.4), or checking at write time whether it moved out from under you (§3.5).

---

## 3.3 "Just wrap it in a transaction" doesn't save you

The reflexive fix is to wrap steps 1 and 2 in a `BEGIN TRANSACTION … COMMIT`. It feels like it should work — a transaction sounds like "one atomic unit" — and it is genuinely useful for a different problem (making sure the `INSERT` and any related writes all succeed or all fail together). But it does **not**, by itself, close the gap in §3.2, and the reason is precise, not hand-wavy: a transaction's isolation guarantees depend entirely on its **isolation level**, and SQL Server's default is `READ COMMITTED`.

`READ COMMITTED` is exactly what its name says and no more: every statement inside your transaction only ever reads data that some other transaction has *committed* — it will never show you a dirty, in-flight, uncommitted row. That's a real guarantee, and it's the right default for most work. But notice what it does **not** promise: it says nothing about whether the row you read is still there, unchanged, by the time you get around to acting on it. "Data can be changed by other transactions between individual statements within the current transaction" is the isolation level's own documented behavior — your `SELECT` at t1 and your `INSERT` at t4 are two separate statements, and `READ COMMITTED` never locks anything to keep the world frozen between them. It prevents you from reading garbage. It does not prevent you from reading *true, but soon-to-be-stale*, data. Dirty reads and lost updates are different failure modes, and a transaction boundary alone only rules out the first one.

| Isolation level | Prevents dirty read? | Prevents lost update (this bug)? | Cost / mechanism |
|---|---|---|---|
| `READ UNCOMMITTED` | ✗ | ✗ | No shared locks; "least restrictive"; can read in-flight uncommitted rows |
| `READ COMMITTED` *(SQL Server default)* | ✓ | ✗ | Shared locks released as soon as each statement finishes (or row-versioned under RCSI) — the classic gap this chapter is about |
| `REPEATABLE READ` | ✓ | ✓ (via blocking) | Holds shared locks on read rows until transaction end — a second transaction can't modify what you've read, but still allows phantom rows; lower concurrency |
| `SNAPSHOT` | ✓ | ✓ (via conflict error) | Transactionally consistent snapshot at transaction start; writers don't block readers or vice versa; a conflicting concurrent write fails at commit instead of blocking |
| `SERIALIZABLE` | ✓ | ✓ | Adds range locks preventing phantom inserts too; most restrictive, highest contention cost |

So there genuinely are isolation levels that close the gap — `REPEATABLE READ` and `SERIALIZABLE` do it by holding locks (pessimistic), `SNAPSHOT` does it by detecting the conflict at commit time (optimistic) — but all three cost more concurrency than `READ COMMITTED`, and all three require the whole read-modify-write sequence to stay inside one long-lived transaction, which is often awkward across a web request. Rather than reach for a blanket isolation-level change that slows down every query in the system, the standard move is to leave isolation at `READ COMMITTED` and instead take an explicit lock (§3.4) or an explicit version check (§3.5) on exactly the rows that need it, and exactly when they need it. **Note** — a transaction always takes an exclusive lock on any row it *modifies* and holds it until commit, regardless of isolation level; the hole in §3.2 is specifically about the *read* that happens before the modification, which under `READ COMMITTED` takes no lasting lock at all.

---

## 3.4 Pessimistic concurrency: lock the row before you read it

If double-bookings are common — a popular room, a flash-sale night, dozens of guests racing for the last inventory — the straightforward fix is to stop pretending the read is safe unlocked. **Pessimistic concurrency** assumes conflict is likely, so it takes a lock at read time and holds it until the write completes, physically preventing a second transaction from reading-and-acting on the same row in the meantime.

On SQL Server, the table hint `UPDLOCK` does exactly this: "update locks are to be taken and held until the transaction completes," applied at the row level with `ROWLOCK` to avoid escalating to a page or table lock and blocking unrelated rooms:

```sql
BEGIN TRANSACTION;

-- Take an update lock on the exact row(s) we're about to reason about.
-- A second, concurrent transaction running the same query BLOCKS here
-- until this transaction commits or rolls back — it cannot get its own
-- "0 bookings" answer while we're mid-decision.
SELECT COUNT(*) FROM Bookings WITH (UPDLOCK, ROWLOCK)
WHERE RoomId = @roomId AND CheckIn = @checkIn AND Status = 1;

-- Still inside the same transaction — no other transaction could have
-- inserted a competing booking between the SELECT and this INSERT.
INSERT INTO Bookings (RoomId, CheckIn, GuestId, Nights, Status)
VALUES (@roomId, @checkIn, @guestId, @nights, 1);

COMMIT;
```

`UPDLOCK` is deliberately weaker than a full exclusive lock: it lets other transactions still *read* the row with an ordinary (non-locking) `SELECT`, but it blocks any other transaction trying to take the *same* update lock — which is exactly the guest-B thread in §3.2's timeline. `ROWLOCK` scopes that to the individual row instead of escalating to the whole page or table, so guests booking *other* rooms aren't held up by this one.

> **Note** — PostgreSQL and MySQL express the identical idea with `SELECT … FOR UPDATE` inside a transaction rather than a table hint — different syntax, same pessimistic-read-before-write pattern.

The trade-off is exactly what "pessimistic" implies: correctness bought with **serialization**. Every other request trying to book Room 214 / Jul 12 now queues up behind the lock holder instead of racing it, which is precisely the goal — but it also means a slow or stalled transaction holding the lock stalls everyone behind it, and locking rows in different orders across code paths can produce a **deadlock** (two transactions each waiting on a lock the other holds), which SQL Server detects and resolves by killing one transaction outright. Pessimistic locking is the right default when contention is frequent and correctness under load matters more than raw throughput — a flash sale on ten rooms, a limited-inventory drop — because in that regime, most requests were going to have to wait for the winner anyway; the lock just makes that waiting orderly instead of racing to a data-corrupting finish.

---

## 3.5 Optimistic concurrency: assume conflicts are rare, check at write time

Most bookings never contend. Two guests wanting the exact same room on the exact same night, at the same moment, is the rare case — for the overwhelming majority of bookings, nobody else is even looking at that row. Taking a lock on every read to defend against a collision that almost never happens is like putting a security guard on every doorway in a mostly-empty building. **Optimistic concurrency** flips the assumption: don't lock anything at read time; instead, stamp every row with a version marker, and when you go to write, tell the database "only apply this update if the version is still what I last saw." If someone else got there first, the version has moved, your write matches zero rows, and you find out immediately — no lock ever held, no one blocked while nothing is actually contending.

EF Core implements this with a **concurrency token**: a mapped property that gets folded into the `WHERE` clause of every generated `UPDATE` and `DELETE`. Two flavors exist, and the choice between them is really a choice about who generates the new value on every write:

| Approach | Attribute / Fluent API | Underlying type | Who assigns the new value | Works on |
|---|---|---|---|---|
| Application-managed | `[ConcurrencyCheck]` / `.IsConcurrencyToken()` | Any type (commonly `Guid`) | **You** — reassign it yourself before every save | Any provider, including SQLite |
| Database-generated | `[Timestamp]` / `.IsRowVersion()` | SQL Server `rowversion` (8-byte `binary`) | **The database** — auto-updates on every row write | SQL Server's `rowversion` specifically — other providers may expose their own auto-updating token with different underlying types and mapping rules |

> **Pitfall — `rowversion` is not a clock.** Despite the name, and despite `[Timestamp]`'s own Microsoft docs describing it as "the data type of the column as a row version," it "does not represent an actual time." It's an 8-byte, per-database counter that increments on every insert or update of *any* row that has a rowversion column — a monotonically increasing binary number, nothing more. Never read it as a wall-clock value, and don't be misled by the legacy T-SQL synonym `timestamp`, which Microsoft explicitly deprecated in favor of `rowversion` for exactly this reason.

`[ConcurrencyCheck]` is the portable choice — it "allows using optimistic concurrency on databases — like SQLite — where no native automatically-updating type exists," at the cost of you remembering to bump the value yourself. `[Timestamp]`/`.IsRowVersion()` is less code (the database maintains it for you) but ties you to SQL Server's `rowversion` type, which "some databases don't support at all."

A version column only guards an `UPDATE` — EF Core skips a table entirely if nothing on the tracked entity actually changed, token or not. So the token can't sit on `Room` itself (booking a room doesn't change anything about the room), it has to sit on the thing that genuinely flips state when a booking is confirmed: a per-night availability row. Real inventory systems do exactly this — a hotel pre-allocates one row per room per night rather than trying to derive "is it free" by scanning `Bookings` — and it gives the concurrency token something real to guard:

```csharp
public class RoomAvailability
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public DateOnly CheckIn { get; set; }
    public bool IsBooked { get; set; }

    [Timestamp]                          // SQL Server rowversion — database-generated
    public byte[] Version { get; set; } = default!;
}

public class Booking
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int GuestId { get; set; }
    public byte NumberOfGuests { get; set; }
    public byte Nights { get; set; }
    public DateOnly CheckIn { get; set; }
    public BookingStatus Status { get; set; }
    public Money Total { get; set; } = default!;
}
```

Confirming a booking now goes through a normal `SaveChangesAsync()` — no manual locking — but flipping `IsBooked` is a genuine change, so EF Core silently adds the row's `Version` to the `WHERE` clause of the `UPDATE` it was already going to emit, exactly the way §2's `Money` mapping (§2.5) taught EF Core to translate a C# shape into SQL on your behalf. Wrap the whole thing in a bounded retry loop — this is the actual 4-step resolution the docs describe (catch → inspect `ex.Entries` → refresh from `GetDatabaseValuesAsync()` → retry), not just the first three steps:

```csharp
public async Task<Booking> ConfirmBookingAsync(int roomId, DateOnly checkIn, int guestId, byte nights, int maxAttempts = 3)
{
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        var availability = await _db.RoomAvailability
            .SingleAsync(a => a.RoomId == roomId && a.CheckIn == checkIn);   // Version comes along for free

        if (availability.IsBooked)
            throw new InvalidOperationException("Room already booked for that night.");

        availability.IsBooked = true;      // a real change → EF marks this row Modified
        var booking = new Booking
        {
            RoomId = roomId, CheckIn = checkIn, GuestId = guestId,
            Nights = nights, Status = BookingStatus.Confirmed
        };
        _db.Bookings.Add(booking);

        try
        {
            await _db.SaveChangesAsync();
            // Generated UPDATE (conceptually):
            //   UPDATE RoomAvailability SET IsBooked = 1 WHERE Id = @id AND Version = @originalVersion;
            // Zero rows affected → someone else's write already moved Version →
            // EF Core throws DbUpdateConcurrencyException.
            return booking;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.Single();
            var databaseValues = await entry.GetDatabaseValuesAsync();
            if (databaseValues is null)
                throw new InvalidOperationException("The availability row was deleted by another process.");

            var current = (RoomAvailability)databaseValues.ToObject();
            if (current.IsBooked)
                throw new InvalidOperationException(
                    "Someone else just booked this room for that night — please try another date.");

            // Still free — just contended. Refresh the token, undo the half-added
            // Booking, and loop back to retry with a fresh read.
            entry.OriginalValues.SetValues(databaseValues);
            _db.Entry(booking).State = EntityState.Detached;
        }
    }

    throw new InvalidOperationException("Could not confirm the booking after several attempts — please try again.");
}
```

The mechanism, precisely: EF Core tracks three separate value sets per entity — **current values** (what your code is trying to write), **original values** (what was read at query time), and **database values** (what's actually in the row right now). A concurrency token means the generated `UPDATE`'s `WHERE` clause pins on the *original* value; if the database's *current* stored value has since moved, the `WHERE` matches zero rows, `SaveChanges()` reports zero affected, and EF Core surfaces that as `DbUpdateConcurrencyException` rather than silently doing nothing. This is the write-time mirror of the read-time lock in §3.4: instead of blocking the second guest at read time, you let both reads succeed and let the *write* referee the race — whoever's `UPDATE` lands first wins, and the loser's `catch` block is the signal to reload, check whether the room is genuinely gone, and either retry or tell the guest — precisely closing the §3.2 gap without ever holding a lock.

---

## 3.6 Belt and suspenders: a unique index as the last line of defense

Optimistic concurrency (§3.5) closes the race for the row it's watching — but it only fires if every code path that writes a booking remembers to check the token. A background import job, a future developer who forgets the `try/catch`, a raw SQL script run by hand during an incident — any of these can `INSERT` straight past your C# logic and recreate the exact bug from §3.2, because nothing at the *database* level says two confirmed bookings for the same room and night are mutually exclusive. Application-layer concurrency control is only as strong as the discipline of every caller; a database constraint has no callers to forget.

The fix costs one line and needs no application code at all: a **unique index** on the columns that must never repeat.

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => new { b.RoomId, b.CheckIn })
    .IsUnique()
    .HasFilter($"[Status] = {(int)BookingStatus.Confirmed}");
    // Filtered unique index: only CONFIRMED bookings must be unique per
    // (RoomId, CheckIn) — a Cancelled booking shouldn't block a rebook.
```

```sql
CREATE UNIQUE INDEX UX_Bookings_RoomId_CheckIn
    ON Bookings (RoomId, CheckIn)
    WHERE Status = 1;  -- Confirmed only
```

Now, even in the exact interleaving from §3.2's table, Thread B's `INSERT` at t5 fails outright with a unique-constraint violation — the database itself refuses the second row, no matter which application layer, script, or bug produced the statement. This does not replace §3.4 or §3.5: a raw constraint violation is a blunt, late failure (an ugly SQL exception, not a friendly "someone else booked this" message, and it happens *after* you've already done the work of building the `Booking` object), so pessimistic locking or optimistic tokens should still be your primary, user-facing defense. The index is the backstop for when they're bypassed — cheap insurance against exactly the bug this chapter opened with, and, unlike the C# in §3.5, it cannot be forgotten in a new code path because it isn't attached to any one code path at all.

---

## 3.7 Optimistic vs pessimistic: which to reach for

| Factor | Favors **pessimistic** (`UPDLOCK`) | Favors **optimistic** (version token) |
|---|---|---|
| Contention level | High — many writers target the same rows | Low — collisions are rare |
| Read/write mix | Write-heavy on hot rows | Read-heavy, occasional write |
| UX tolerance for conflict | Low — better to make guest B wait than fail | Higher — a "someone beat you to it, try again" message is acceptable |
| Deadlock risk | Real — multiple locks taken in different orders across code paths | None — no locks held, nothing to deadlock on |
| Throughput under low contention | Needlessly serializes traffic that was never going to collide | Full throughput — cost paid only on an actual conflict |
| Long-running transaction span | Lock is held for the whole read-modify-write — costly if that span is long (e.g. a web request round-trip) | No lock held at all during that span |
| Implementation cost | A table hint / `SELECT … FOR UPDATE` in the query | A schema column + retry-on-exception logic |

> **Rule of thumb** — Start optimistic. Most systems, most of the time, have far more reads than genuine collisions, and optimistic concurrency costs nothing until a conflict actually happens. Reach for pessimistic locking only when you can name the hot, contended resource in advance — a flash-sale room, a single shared counter, a queue's head row — where you *know* most requests will queue up regardless, and an orderly lock beats a stampede of retries. Keep the unique index (§3.6) either way; it's not a third option, it's the floor under both.

---

## 3.8 Try it yourself

The repo ships two runnable demos that reproduce the race from §3.2 and the fixes from §3.4–§3.6.

`cd demos` then `dotnet run -- 3` runs [`demos/Chapter03Concurrency.cs`](https://github.com/hoangsnowy/engineering-essentials/blob/main/demos/Chapter03Concurrency.cs), which simulates the Thread A / Thread B interleaving from §3.2's table directly in-process. Predict first:

1. Running the naive check-then-act booking method with two concurrent tasks racing for the same room and night — how many bookings end up in the store, 1 or 2? *(2 — both tasks read "not booked" before either writes; the demo forces this with an artificial delay between the check and the insert, so it reproduces on every run.)*
2. After swapping in the `UPDLOCK`-style guard (a `SemaphoreSlim` gate around the whole check-then-act), how many bookings are there for the same race? *(1 — the second caller's read is now forced to happen after the first caller's write, so it correctly sees the room as taken.)*
3. What exception type, if any, does the losing caller observe once the pessimistic fix is applied? *(None — `TryBookLocked` simply returns `false`. The demo is dependency-free, so it models a rejected write as a boolean, not a thrown exception; §3.5's real EF Core mechanism is what actually throws.)*

`cd demos.efcore` then `dotnet run` runs the extended [`demos.efcore/`](https://github.com/hoangsnowy/engineering-essentials/tree/main/demos.efcore) project against a real EF Core context, and now also deliberately forces a version conflict so you can watch a genuine `DbUpdateConcurrencyException` get thrown and caught end to end. Predict first:

4. When the demo updates the same `Room` row through two different `DbContext` instances without reloading in between, what exception does the second `SaveChanges()` throw? *(`Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`, with the message "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)…".)*
5. After the catch block calls `GetDatabaseValues()` and inspects the row, does the demo retry `SaveChanges()`? *(No — the reloaded values show `IsBooked = true`, meaning the room is genuinely gone, not just a stale token, so the demo reports the conflict and stops instead of retrying. A retry only makes sense when the reload shows the row is still available.)*

---

## 3.9 Cheat sheet

| Situation | Reach for | Why |
|---|---|---|
| Availability check immediately followed by an insert/update | Never trust the read alone — lock (§3.4) or version-check (§3.5) | Check-then-act across two round-trips is a lost-update race |
| Hot, heavily contended row (flash sale, limited inventory) | Pessimistic — `SELECT … WITH (UPDLOCK, ROWLOCK)` / `FOR UPDATE` | Most requests were queuing anyway; an orderly lock beats a stampede |
| Low-contention, high read/write ratio | Optimistic — `[Timestamp]`/`.IsRowVersion()` or `[ConcurrencyCheck]` | Pay the cost only on an actual conflict, none on the common path |
| Cross-database portability needed (e.g. SQLite in tests) | `[ConcurrencyCheck]` / `.IsConcurrencyToken()` | Application-managed token works on any provider |
| SQL Server only, minimal code | `[Timestamp]` / `.IsRowVersion()` | Database auto-maintains the 8-byte `rowversion` counter |
| Defense against bugs/scripts that skip application logic entirely | A unique index (filtered, if needed) | The database enforces it regardless of which code path writes |
| "Just wrap it in a transaction" | Not sufficient alone under default `READ COMMITTED` | Prevents dirty reads, not lost updates — see §3.3's table |

---

## 3.10 Key takeaways

1. **Check-then-act is two operations, not one** — the gap between the `SELECT` that checks availability and the `INSERT`/`UPDATE` that acts on it is exactly where a concurrent request produces a lost update, and no unit test run on a single thread will ever see it.
2. **A transaction alone does not close that gap.** SQL Server's default `READ COMMITTED` isolation prevents dirty reads, not lost updates — closing the gap requires either a lock, a version check, or a stricter isolation level (`REPEATABLE READ`/`SNAPSHOT`/`SERIALIZABLE`), each traded for less concurrency.
3. **Pessimistic locking (`UPDLOCK`/`ROWLOCK`, or `FOR UPDATE` on Postgres/MySQL) makes the row itself the referee at read time** — correct under heavy contention, at the cost of serialized throughput and deadlock risk.
4. **Optimistic concurrency checks at write time instead** — a version/rowversion column folded into the `UPDATE`'s `WHERE` clause; zero rows affected means someone else won, and EF Core reports that as `DbUpdateConcurrencyException` for you to catch, reload, and retry or surface to the user.
5. **`rowversion` is a binary counter, not a timestamp** — despite the name, it carries no wall-clock meaning, only "has this row changed since I last read it."
6. **A unique index is the floor under both strategies** — it is the one defense that cannot be skipped by a forgotten `try/catch` or a raw script, so keep it even after adding application-level concurrency control.

---

## 3.11 References (Microsoft Learn)

- [EF Core — Handling concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [`DbUpdateConcurrencyException`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbupdateconcurrencyexception)
- [`ConcurrencyCheckAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.concurrencycheckattribute)
- [`TimestampAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations.timestampattribute)
- SQL Server: [`rowversion` (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql) · [`SET TRANSACTION ISOLATION LEVEL`](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql) · [Table hints (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table)

> Back to the [Introduction](/) · Previous: [Chapter 2 — Choosing the Right Numeric Type](chapters/02-numeric-types.md)
