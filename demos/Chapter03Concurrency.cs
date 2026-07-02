namespace EngineeringEssentials.Demos;

// ─────────────────────────────────────────────────────────────────────────────
// Chapter 3 — Concurrency Control (lost updates, pessimistic vs optimistic)
//   Two requests read-then-write the same row without coordination -> one
//   update silently vanishes. Fix it by either blocking the second reader
//   (pessimistic) or detecting the stale write and retrying (optimistic).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A minimal booking record — enough to reproduce "two guests, one room, one night".</summary>
internal sealed class Booking
{
    public long Id { get; init; }
    public int RoomId { get; init; }
    public DateOnly CheckIn { get; init; }
    public string Guest { get; init; } = "";
}

/// <summary>A room row with an application-managed concurrency token (§3.4's in-memory analog of [ConcurrencyCheck]).</summary>
internal sealed class VersionedRoom
{
    public int Id { get; init; }
    public DateOnly CheckIn { get; init; }
    public bool IsBooked { get; set; }
    public int Version { get; set; }          // bumped on every successful write
}

internal static class Chapter03Concurrency
{
    public static void Run()
    {
        Ui.Title("Chapter 3 — Concurrency Control");

        LostUpdateDeterministic();
        PessimisticFix();
        OptimisticFix();
    }

    // ── (a) THE BUG — reproduced deterministically every run ──────────────────
    //
    // Two Tasks both run "check availability, then insert" for the SAME room+date.
    // A Task.Delay is planted BETWEEN the read and the write on purpose, so the
    // OS scheduler can never race this away — both tasks are guaranteed to pass
    // the availability check before either one writes. This is not a "sometimes"
    // bug; it reproduces on every single run, which is the point: the naive
    // check-then-act pattern is broken by construction, not by bad luck.
    //
    // NOTE: storage is a ConcurrentBag, not a plain List<T>. That is deliberate
    // and NOT the bug being demonstrated — List<T>.Add is not thread-safe, and
    // letting two threads call it at the exact same instant introduces a SECOND,
    // unrelated race (a corrupted/lost element) that would muddy the one race
    // this demo exists to show. ConcurrentBag makes the "table" itself safe to
    // write from two threads so the only race left is the one in TryBookNaive's
    // check-then-act logic — the read-modify-write gap, exactly like two web
    // requests hitting the same ASP.NET Core endpoint concurrently.
    private static void LostUpdateDeterministic()
    {
        Ui.Section("3.1/3.2 — The lost update, forced to happen every time");

        var table = new System.Collections.Concurrent.ConcurrentBag<Booking>(); // stands in for the `Bookings` table
        long nextId = 1;
        var roomId = 101;
        var checkIn = new DateOnly(2026, 8, 1);

        async Task<bool> TryBookNaive(string guest)
        {
            // 1) CHECK — "is anyone already in room 101 on 2026-08-01?"
            bool alreadyBooked = table.Any(b => b.RoomId == roomId && b.CheckIn == checkIn);

            // Artificial gap: in real life this is network latency, GC, a busy
            // thread pool, or just two web requests arriving milliseconds apart.
            // Widening the gap here only makes the race EASIER to hit — the bug
            // itself doesn't need it, but the delay guarantees we hit it in a demo.
            await Task.Delay(50);

            if (alreadyBooked) return false;       // "someone beat me to it" — never true here

            // 2) ACT — both tasks reach this line believing they're first.
            table.Add(new Booking { Id = Interlocked.Increment(ref nextId), RoomId = roomId, CheckIn = checkIn, Guest = guest });
            return true;
        }

        var t1 = TryBookNaive("Alice");
        var t2 = TryBookNaive("Bob");
        Task.WaitAll(t1, t2);

        int survivors = table.Count(b => b.RoomId == roomId && b.CheckIn == checkIn);
        Ui.Show("Alice's TryBook result", t1.Result);
        Ui.Show("Bob's TryBook result", t2.Result);
        Ui.Show("bookings for room 101 / 2026-08-01", survivors);
        Ui.Note("Both reads happened before either write — the check was true (\"free\") for both callers.");
        Ui.Note("Expected 1 booking, got " + survivors + ". This is the lost-update anomaly — no exception, no log line, just a double-booked room.");
    }

    // ── (b) PESSIMISTIC FIX — lock the whole check-then-act ───────────────────
    //
    // In-memory analog of  SELECT ... WITH (UPDLOCK, ROWLOCK)  followed by the
    // UPDATE inside the same transaction: the second caller cannot even START
    // its read until the first caller's read+write has fully committed.
    private static void PessimisticFix()
    {
        Ui.Section("3.5 — Pessimistic fix: lock the critical section");

        var table = new List<Booking>();
        long nextId = 1;
        var roomId = 101;
        var checkIn = new DateOnly(2026, 8, 1);
        var gate = new SemaphoreSlim(1, 1);        // in-memory stand-in for UPDLOCK+ROWLOCK

        async Task<bool> TryBookLocked(string guest)
        {
            await gate.WaitAsync();                // blocks the second caller entirely
            try
            {
                bool alreadyBooked = table.Any(b => b.RoomId == roomId && b.CheckIn == checkIn);
                await Task.Delay(50);               // same artificial gap — now harmless, no one else can enter

                if (alreadyBooked) return false;

                table.Add(new Booking { Id = Interlocked.Increment(ref nextId), RoomId = roomId, CheckIn = checkIn, Guest = guest });
                return true;
            }
            finally
            {
                gate.Release();                     // hands the lock to whoever is waiting next
            }
        }

        var t1 = TryBookLocked("Alice");
        var t2 = TryBookLocked("Bob");
        Task.WaitAll(t1, t2);

        int survivors = table.Count(b => b.RoomId == roomId && b.CheckIn == checkIn);
        Ui.Show("Alice's TryBook result", t1.Result);
        Ui.Show("Bob's TryBook result", t2.Result);
        Ui.Show("bookings for room 101 / 2026-08-01", survivors);
        Ui.Note("The gate serializes the whole read+write span — the second caller's read now happens AFTER the first caller's write.");
        Ui.Note("Correct, but the lock holds for the full duration of the delay — every other request for this room queues behind it.");
    }

    // ── (c) OPTIMISTIC FIX — compare-and-swap with a bounded retry loop ───────
    //
    // In-memory analog of EF Core's [ConcurrencyCheck] token: no lock is taken
    // up front. Instead, the writer includes the Version it read in its commit
    // attempt; if the row's Version has moved on, the commit is rejected — the
    // in-memory equivalent of  UPDATE ... WHERE Id=@id AND Version=@expected
    // affecting zero rows, which is exactly what makes EF Core throw
    // DbUpdateConcurrencyException in the real database (§3.4).
    private static void OptimisticFix()
    {
        Ui.Section("3.6 — Optimistic fix: versioned compare-and-swap + retry");

        var room = new VersionedRoom { Id = 101, CheckIn = new DateOnly(2026, 8, 1), IsBooked = false, Version = 1 };

        // CommitBooking mimics SaveChanges(): it only succeeds if the Version it
        // read still matches the Version currently in the "table" at write time.
        bool CommitBooking(VersionedRoom liveRow, int expectedVersion)
        {
            if (liveRow.Version != expectedVersion) return false;   // 0 rows matched -> DbUpdateConcurrencyException
            liveRow.IsBooked = true;
            liveRow.Version++;                                       // bump token, same as a fresh rowversion/Guid
            return true;
        }

        (bool booked, int attempts) TryBookOptimistic(string guest, int maxRetries)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // 1) READ — snapshot the row and its version (Original values, per §3.4).
                bool wasFree = !room.IsBooked;
                int readVersion = room.Version;

                // Same artificial gap as (a)/(b): simulates the other request
                // sneaking in and committing between this read and this write.
                Thread.Sleep(guest == "Alice" ? 10 : 50);

                if (!wasFree)
                {
                    Ui.Note($"{guest} attempt {attempt}: read room as already booked — abort, no write attempted.");
                    return (false, attempt);
                }

                // 2) WRITE — compare-and-swap against the version read in step 1.
                bool committed = CommitBooking(room, readVersion);
                if (committed)
                {
                    Ui.Note($"{guest} attempt {attempt}: committed with Version {readVersion} -> {room.Version}.");
                    return (true, attempt);
                }

                // 3) CONFLICT — the in-memory DbUpdateConcurrencyException moment.
                //    Reload database values and loop (the documented retry pattern:
                //    catch, inspect ex.Entries, refresh OriginalValues, retry).
                Ui.Note($"{guest} attempt {attempt}: Version mismatch (expected {readVersion}, room is now {room.Version}) — reload and retry.");
            }
            return (false, maxRetries);
        }

        // Run both "requests" concurrently; Alice's shorter delay makes her win
        // deterministically, so the demo prints the same outcome every time.
        var t1 = Task.Run(() => TryBookOptimistic("Alice", maxRetries: 3));
        var t2 = Task.Run(() => TryBookOptimistic("Bob", maxRetries: 3));
        Task.WaitAll(t1, t2);

        Ui.Show("Alice (booked, attempts)", t1.Result);
        Ui.Show("Bob (booked, attempts)", t2.Result);
        Ui.Show("final room.IsBooked", room.IsBooked);
        Ui.Show("final room.Version", room.Version);
        Ui.Note("No lock was ever held during the delay — the loser detects the conflict at commit time and aborts cleanly instead of corrupting data.");
    }
}
