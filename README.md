# Engineering Essentials

> A practical, in-depth handbook of the software-engineering fundamentals that many developers quietly miss — the kind of things that don't break the build, but quietly cost correctness, performance, and money in production.

This is a living book. It started as an internal tech-sharing session and is being condensed, chapter by chapter, into something the wider community can use.

## Who this is for

Working developers — junior to senior — who can ship features but want the **mental models underneath** the everyday decisions: how to model a concept, which type to store it in, and *why*. Every chapter goes deeper than "how"; it explains the theory and the trade-offs so the right choice becomes obvious.

Examples are in **C# / .NET**, but the ideas (bitmasks, domain modeling, IEEE-754, storage cost) are language-agnostic.

## A single running example: hotel booking

To keep things concrete, the whole book models one familiar domain — a **hotel booking / reservation system**: `Booking`, `Room`, `Guest`, amenities, prices, date ranges, and so on. Each chapter reuses and refines this domain, so by the end you have one coherent design instead of disconnected snippets.

## Table of contents

**Part I · Modeling**

1. [Enum Flags & Domain Modeling](chapters/01-enum-flags.md) — when one field should hold *many* choices, the bitmask theory behind `[Flags]`, and the DDD distinction (Value Object vs Entity) that stops you from designing a needless many-to-many table.

**Part II · Data & Storage**

2. [Choosing the Right Numeric Type](chapters/02-numeric-types.md) — why defaulting everything to `int` wastes bandwidth, storage, and money; integer sizing; and the `decimal` vs `double` vs `float` decision that protects your money values.

**Part III · Correctness Under Load**

3. [Concurrency Control & the Double-Booking Bug](chapters/03-concurrency-control.md) — why two guests can both get a "confirmed" booking for the same room, why wrapping the code in a transaction doesn't fix it, and the pessimistic-locking vs optimistic-concurrency decision (with EF Core's `DbUpdateConcurrencyException`) that does.

New to a term? The [Glossary](glossary.md) defines the jargon (sargable, CLS-compliant, banker's rounding, lost update, concurrency token, …) in one line each.

## Roadmap

Planned / candidate chapters (suggestions welcome):

- Nullability & the billion-dollar mistake
- Strings, encodings, and culture-aware formatting
- Idempotency & exactly-once illusions
- Time, time zones, and `DateTimeOffset`
- Indexing fundamentals & query cost

## Runnable C# demos

Every chapter ships with a runnable console demo under [`demos/`](demos/) — they print their results so you can run them live in a tech-sharing session and let the room predict the output first. Requires the .NET SDK (project targets **net8.0**).

```bash
cd demos
dotnet run -- 1   # Chapter 1 — Enum Flags & Domain Modeling
dotnet run -- 2   # Chapter 2 — Numeric Types
dotnet run -- 3   # Chapter 3 — Concurrency Control
```

## Run it locally

The site is [Docsify](https://docsify.js.org) — pure static files, no build step.

```bash
# any static server works; for example:
npx serve .
# or
python3 -m http.server 3000
```

Then open the printed URL. (Docsify must be served over HTTP, not opened as a `file://` path.)

## License

Content licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/); code samples under MIT. Attribution appreciated.
