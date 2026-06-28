namespace EngineeringEssentials.Demos;

// ─────────────────────────────────────────────────────────────────────────────
// Chapter 2 — Choosing the Right Numeric Type
//   int everywhere wastes storage / bandwidth / money.
//   double is binary & approximate; decimal is base-10 & exact. Money = decimal.
// ─────────────────────────────────────────────────────────────────────────────

internal static class Chapter02NumericTypes
{
    public static void Run()
    {
        Ui.Title("Chapter 2 — Choosing the Right Numeric Type");

        IntegerWidthCosts();
        TheTenCentsProblem();
        DecimalIsExact();
        MoneyInDoubleIsABug();
        RangeVsPrecision();
        BankersRounding();
    }

    private static void IntegerWidthCosts()
    {
        Ui.Section("Where width multiplies: columns, wire, big arrays");

        Ui.Show("sizeof(byte)", sizeof(byte));    // 1
        Ui.Show("sizeof(short)", sizeof(short));  // 2
        Ui.Show("sizeof(int)", sizeof(int));      // 4
        Ui.Show("sizeof(long)", sizeof(long));    // 8

        const int rows = 1_000_000_000;           // one billion rows
        long asInt   = (long)rows * sizeof(int);   // 4 GB
        long asShort = (long)rows * sizeof(short);  // 2 GB
        Ui.Show("1B rows as int  (bytes)", asInt);
        Ui.Show("1B rows as short (bytes)", asShort);
        Ui.Note($"Narrowing int->short on 1B rows saves ~{(asInt - asShort) / (1024 * 1024 * 1024)} GB — at rest, on every index, replica, backup.");
        Ui.Note("NumberOfGuests never exceeds ~20 -> byte (tinyint). The type documents AND bounds the domain.");
    }

    private static void TheTenCentsProblem()
    {
        Ui.Section("Why double can't hold ten cents (this is arithmetic, not a bug)");

        Ui.Show("0.1 + 0.2", 0.1 + 0.2);            // 0.30000000000000004
        Ui.Show("0.1 + 0.2 == 0.3", 0.1 + 0.2 == 0.3); // False

        double sum = 0.0;
        for (int i = 0; i < 10; i++) sum += 0.1;     // ten ten-cent items
        Ui.Show("sum of 0.1 x10 (double)", sum);     // 0.9999999999999999
        Ui.Show("sum == 1.0", sum == 1.0);           // False  <- invoice now "wrong"
    }

    private static void DecimalIsExact()
    {
        Ui.Section("decimal stores 0.1 AS one-tenth, in base-10, exactly");

        decimal sum = 0m;
        for (int i = 0; i < 10; i++) sum += 0.1m;
        Ui.Show("sum of 0.1m x10 (decimal)", sum);   // 1.0
        Ui.Show("sum == 1.0m", sum == 1.0m);         // True
    }

    private static void MoneyInDoubleIsABug()
    {
        Ui.Section("Same invoice, two types — the reconciliation gap");

        // 3 items @ 0.10, taxed 7%, summed.
        double dPrice = 0.10, dTotal = 0;
        for (int i = 0; i < 3; i++) dTotal += dPrice;
        dTotal *= 1.07;

        decimal mPrice = 0.10m, mTotal = 0;
        for (int i = 0; i < 3; i++) mTotal += mPrice;
        mTotal *= 1.07m;

        Ui.Show("double total (raw)", dTotal);
        Ui.Show("decimal total (raw)", mTotal);
        Ui.Note("Per-operation the error is tiny; across a ledger it fails to reconcile. Never money in float/double.");
    }

    private static void RangeVsPrecision()
    {
        Ui.Section("Range vs precision are DIFFERENT axes");

        // double: colossal range, only ~15-17 significant digits.
        double big = 1.7e308;
        Ui.Show("double max-ish (1.7e308)", big);
        double lossy = 1234567890123456789.0;        // beyond ~17 digits -> rounded
        Ui.Show("double of 1234567890123456789", lossy);

        // decimal: modest range (~7.9e28), but ~28-29 exact digits within it.
        decimal exact = 1234567890123456789m;
        Ui.Show("decimal of 1234567890123456789", exact);
        Ui.Note("Money needs exact digits in a human-sized range -> decimal. Physics needs vast range, tolerates error -> double.");
    }

    private static void BankersRounding()
    {
        Ui.Section("MidpointRounding.ToEven = banker's rounding (the .NET default)");

        Ui.Show("Math.Round(2.5m)  (ToEven)", Math.Round(2.5m));  // 2
        Ui.Show("Math.Round(3.5m)  (ToEven)", Math.Round(3.5m));  // 4
        Ui.Show("Math.Round(2.5m, AwayFromZero)",
            Math.Round(2.5m, MidpointRounding.AwayFromZero));     // 3
        Ui.Note("Pick rounding mode on purpose for money — the default rounds .5 to the nearest EVEN.");
    }
}
