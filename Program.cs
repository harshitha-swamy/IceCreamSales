// =============================================================================
// Ice Cream Parlor Sales Analysis
// .NET Core Console Application
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace IceCreamSales
{
    // -------------------------------------------------------------------------
    // Domain Model
    // -------------------------------------------------------------------------

    internal sealed class SaleRecord
    {
        public DateTime Date       { get; init; }
        public string   Sku        { get; init; } = string.Empty;
        public decimal  UnitPrice  { get; init; }
        public int      Quantity   { get; init; }
        public decimal  TotalPrice { get; init; }
    }

    internal sealed class ValidationError
    {
        public int    LineNumber { get; init; }
        public string RawLine   { get; init; } = string.Empty;
        public string Reason    { get; init; } = string.Empty;
    }

    // -------------------------------------------------------------------------
    // CSV Parser
    // -------------------------------------------------------------------------

    internal static class CsvParser
    {
        /// <summary>
        /// Parses CSV text into valid SaleRecords and a separate list of
        /// ValidationErrors.  The header row is always skipped.
        /// </summary>
        public static (List<SaleRecord> records, List<ValidationError> errors)
            Parse(string csvText)
        {
            var records = new List<SaleRecord>();
            var errors  = new List<ValidationError>();

            string[] lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++)   // i = 0 is the header
            {
                string rawLine = lines[i].Trim();
                if (rawLine.Length == 0) continue;

                string? validationReason = TryParseRow(rawLine, i + 1, out SaleRecord? record);

                if (validationReason is not null)
                {
                    errors.Add(new ValidationError
                    {
                        LineNumber = i + 1,
                        RawLine    = rawLine,
                        Reason     = validationReason
                    });
                }
                else
                {
                    records.Add(record!);
                }
            }

            return (records, errors);
        }

        /// <summary>
        /// Tries to parse one CSV row.
        /// Returns null on success (record is populated), or an error reason string.
        /// </summary>
        private static string? TryParseRow(string line, int lineNumber,
                                           out SaleRecord? record)
        {
            record = null;
            string[] parts = line.Split(',');

            if (parts.Length < 5)
                return "Row has fewer than 5 columns";

            // --- Date ---
            if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return $"Malformed date: '{parts[0].Trim()}'";

            // --- SKU ---
            string sku = parts[1].Trim();
            if (sku.Length == 0)
                return "SKU is empty";

            // --- Unit Price ---
            if (!decimal.TryParse(parts[2].Trim(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal unitPrice))
                return $"Non-numeric Unit Price: '{parts[2].Trim()}'";

            if (unitPrice < 0)
                return $"Unit Price is negative ({unitPrice})";

            // --- Quantity ---
            if (!int.TryParse(parts[3].Trim(), out int quantity))
                return $"Non-integer Quantity: '{parts[3].Trim()}'";

            if (quantity < 1)
                return $"Quantity is less than 1 ({quantity})";

            // --- Total Price ---
            if (!decimal.TryParse(parts[4].Trim(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out decimal totalPrice))
                return $"Non-numeric Total Price: '{parts[4].Trim()}'";

            if (totalPrice < 0)
                return $"Total Price is negative ({totalPrice})";

            // --- Consistency check ---
            decimal expectedTotal = unitPrice * quantity;
            if (expectedTotal != totalPrice)
                return $"Unit Price ({unitPrice}) × Quantity ({quantity}) = {expectedTotal}" +
                       $" but Total Price is {totalPrice}";

            record = new SaleRecord
            {
                Date       = date,
                Sku        = sku,
                UnitPrice  = unitPrice,
                Quantity   = quantity,
                TotalPrice = totalPrice
            };

            return null;   // success
        }
    }

    // -------------------------------------------------------------------------
    // Report Engine
    // -------------------------------------------------------------------------

    internal static class ReportEngine
    {
        // ------------------------------------------------------------------
        // 1. Total sales of the store
        // ------------------------------------------------------------------
        public static decimal TotalStoreSales(List<SaleRecord> records)
        {
            decimal total = 0;
            foreach (var r in records)
                total += r.TotalPrice;
            return total;
        }

        // ------------------------------------------------------------------
        // 2. Month-wise sales totals
        //    Returns: Dictionary<"YYYY-MM", totalRevenue>
        // ------------------------------------------------------------------
        public static Dictionary<string, decimal> MonthWiseSales(List<SaleRecord> records)
        {
            var map = new Dictionary<string, decimal>();

            foreach (var r in records)
            {
                string key = MonthKey(r.Date);
                map.TryGetValue(key, out decimal existing);
                map[key] = existing + r.TotalPrice;
            }

            return SortedByKey(map);
        }

        // ------------------------------------------------------------------
        // 3. Most popular item (highest total quantity) per month
        //    Also returns min, max, and average order size for that item
        // ------------------------------------------------------------------
        public sealed class PopularItemStats
        {
            public string  Sku         { get; init; } = string.Empty;
            public int     TotalQty    { get; init; }
            public int     MinOrder    { get; init; }
            public int     MaxOrder    { get; init; }
            public double  AvgOrder    { get; init; }
            public int     OrderCount  { get; init; }
        }

        public static Dictionary<string, PopularItemStats> MostPopularItemPerMonth(
            List<SaleRecord> records)
        {
            // month -> sku -> list of per-transaction quantities
            var monthSkuQties = new Dictionary<string, Dictionary<string, List<int>>>();

            foreach (var r in records)
            {
                string mk = MonthKey(r.Date);

                if (!monthSkuQties.TryGetValue(mk, out var skuMap))
                {
                    skuMap = new Dictionary<string, List<int>>();
                    monthSkuQties[mk] = skuMap;
                }

                if (!skuMap.TryGetValue(r.Sku, out var qtList))
                {
                    qtList = new List<int>();
                    skuMap[r.Sku] = qtList;
                }

                qtList.Add(r.Quantity);
            }

            var result = new Dictionary<string, PopularItemStats>();

            foreach (var (month, skuMap) in monthSkuQties)
            {
                string  topSku      = string.Empty;
                int     topTotal    = int.MinValue;

                // Find SKU with highest total quantity
                foreach (var (sku, qtList) in skuMap)
                {
                    int total = SumList(qtList);
                    if (total > topTotal)
                    {
                        topTotal = total;
                        topSku   = sku;
                    }
                }

                List<int> orders = skuMap[topSku];
                int   minOrder   = MinList(orders);
                int   maxOrder   = MaxList(orders);
                double avgOrder  = (double)SumList(orders) / orders.Count;

                result[month] = new PopularItemStats
                {
                    Sku        = topSku,
                    TotalQty   = topTotal,
                    MinOrder   = minOrder,
                    MaxOrder   = maxOrder,
                    AvgOrder   = avgOrder,
                    OrderCount = orders.Count
                };
            }

            return SortedByKey(result);
        }

        // ------------------------------------------------------------------
        // 4. Item generating the most revenue per month
        //    Returns: Dictionary<"YYYY-MM", (sku, revenue)>
        // ------------------------------------------------------------------
        public static Dictionary<string, (string Sku, decimal Revenue)>
            TopRevenueItemPerMonth(List<SaleRecord> records)
        {
            // month -> sku -> total revenue
            var monthSkuRev = new Dictionary<string, Dictionary<string, decimal>>();

            foreach (var r in records)
            {
                string mk = MonthKey(r.Date);

                if (!monthSkuRev.TryGetValue(mk, out var skuMap))
                {
                    skuMap = new Dictionary<string, decimal>();
                    monthSkuRev[mk] = skuMap;
                }

                skuMap.TryGetValue(r.Sku, out decimal existing);
                skuMap[r.Sku] = existing + r.TotalPrice;
            }

            var result = new Dictionary<string, (string, decimal)>();

            foreach (var (month, skuMap) in monthSkuRev)
            {
                string  topSku = string.Empty;
                decimal topRev = decimal.MinValue;

                foreach (var (sku, rev) in skuMap)
                {
                    if (rev > topRev)
                    {
                        topRev = rev;
                        topSku = sku;
                    }
                }

                result[month] = (topSku, topRev);
            }

            return SortedByKey(result);
        }

        // ------------------------------------------------------------------
        // 5. Month-to-month growth per item (%)
        //    Returns: Dictionary<sku, Dictionary<"YYYY-MM", growthPct?>>
        //    growthPct is null for the first month of each item (no prior data).
        // ------------------------------------------------------------------
        public static Dictionary<string, Dictionary<string, double?>>
            MonthToMonthGrowthPerItem(List<SaleRecord> records)
        {
            // sku -> month -> total revenue
            var skuMonthRev = new Dictionary<string, Dictionary<string, decimal>>();

            foreach (var r in records)
            {
                if (!skuMonthRev.TryGetValue(r.Sku, out var monthMap))
                {
                    monthMap = new Dictionary<string, decimal>();
                    skuMonthRev[r.Sku] = monthMap;
                }

                string mk = MonthKey(r.Date);
                monthMap.TryGetValue(mk, out decimal existing);
                monthMap[mk] = existing + r.TotalPrice;
            }

            var result = new Dictionary<string, Dictionary<string, double?>>();

            foreach (var (sku, monthMap) in skuMonthRev)
            {
                // Sort months chronologically
                var sortedMonths = new List<string>(monthMap.Keys);
                sortedMonths.Sort(StringComparer.Ordinal);

                var growthMap = new Dictionary<string, double?>();

                for (int i = 0; i < sortedMonths.Count; i++)
                {
                    string month = sortedMonths[i];

                    if (i == 0)
                    {
                        growthMap[month] = null;   // no prior month to compare
                    }
                    else
                    {
                        string  prevMonth = sortedMonths[i - 1];
                        decimal prevRev   = monthMap[prevMonth];
                        decimal currRev   = monthMap[month];

                        double? growth = prevRev == 0
                            ? null                                      // avoid divide-by-zero
                            : (double)((currRev - prevRev) / prevRev * 100);

                        growthMap[month] = growth;
                    }
                }

                result[sku] = growthMap;
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string MonthKey(DateTime d) =>
            d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        private static int SumList(List<int> list)
        {
            int s = 0;
            foreach (int v in list) s += v;
            return s;
        }

        private static int MinList(List<int> list)
        {
            int m = int.MaxValue;
            foreach (int v in list) if (v < m) m = v;
            return m;
        }

        private static int MaxList(List<int> list)
        {
            int m = int.MinValue;
            foreach (int v in list) if (v > m) m = v;
            return m;
        }

        /// <summary>Returns a new dictionary sorted by key (ascending).</summary>
        private static Dictionary<TKey, TValue> SortedByKey<TKey, TValue>(
            Dictionary<TKey, TValue> source) where TKey : notnull
        {
            var sorted = new Dictionary<TKey, TValue>();
            var keys   = new List<TKey>(source.Keys);
            keys.Sort(Comparer<TKey>.Default);
            foreach (var k in keys)
                sorted[k] = source[k];
            return sorted;
        }
    }

    // -------------------------------------------------------------------------
    // Console Printer
    // -------------------------------------------------------------------------

    internal static class Printer
    {
        public static void PrintHeader(string title)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 64));
            Console.WriteLine($"  {title}");
            Console.WriteLine(new string('=', 64));
        }

        public static void PrintSubHeader(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {title} ---");
        }

        public static void PrintTotalSales(decimal total)
        {
            PrintHeader("1. Total Store Sales");
            Console.WriteLine($"  Grand Total : {total:C2}");
        }

        public static void PrintMonthWiseSales(Dictionary<string, decimal> data)
        {
            PrintHeader("2. Month-wise Sales Totals");
            foreach (var (month, total) in data)
                Console.WriteLine($"  {month}  :  {total,10:C2}");
        }

        public static void PrintMostPopularItems(
            Dictionary<string, ReportEngine.PopularItemStats> data)
        {
            PrintHeader("3. Most Popular Item per Month (by Quantity Sold)");
            foreach (var (month, stats) in data)
            {
                Console.WriteLine($"  {month}  →  {stats.Sku}");
                Console.WriteLine($"           Total Qty : {stats.TotalQty}");
                Console.WriteLine($"           Orders    : {stats.OrderCount}");
                Console.WriteLine($"           Min Order : {stats.MinOrder}");
                Console.WriteLine($"           Max Order : {stats.MaxOrder}");
                Console.WriteLine($"           Avg Order : {stats.AvgOrder:F2}");
            }
        }

        public static void PrintTopRevenueItems(
            Dictionary<string, (string Sku, decimal Revenue)> data)
        {
            PrintHeader("4. Highest Revenue Item per Month");
            foreach (var (month, item) in data)
                Console.WriteLine($"  {month}  →  {item.Sku,-35}  {item.Revenue,10:C2}");
        }

        public static void PrintGrowthPerItem(
            Dictionary<string, Dictionary<string, double?>> data)
        {
            PrintHeader("5. Month-to-Month Revenue Growth per Item (%)");

            foreach (var (sku, monthMap) in data)
            {
                PrintSubHeader(sku);

                // Sort months chronologically
                var months = new List<string>(monthMap.Keys);
                months.Sort(StringComparer.Ordinal);

                foreach (var month in months)
                {
                    double? growth = monthMap[month];
                    string label = growth.HasValue
                        ? $"{growth.Value:+0.00;-0.00}%"
                        : "  N/A (first month)";
                    Console.WriteLine($"    {month}  :  {label}");
                }
            }
        }

        public static void PrintValidationErrors(List<ValidationError> errors)
        {
            PrintHeader("6. Data Validation Errors");

            if (errors.Count == 0)
            {
                Console.WriteLine("  No validation errors found.");
                return;
            }

            Console.WriteLine($"  {errors.Count} error(s) detected:\n");

            foreach (var e in errors)
            {
                Console.WriteLine($"  Line {e.LineNumber,3} | {e.Reason}");
                Console.WriteLine($"           Raw: {e.RawLine}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Entry Point
    // -------------------------------------------------------------------------

    internal static class Program
    {
        private const string CsvData = @"Date,SKU,Unit Price,Quantity,Total Price
2019-01-01,Death by Chocolate,180,5,900
2019-01-01,Cake Fudge,150,1,150
2019-01-01,Cake Fudge,150,1,150
2019-01-01,Cake Fudge,150,3,450
2019-01-01,Death by Chocolate,180,1,180
2019-01-01,Vanilla Double Scoop,80,3,240
2019-01-01,Butterscotch Single Scoop,60,5,300
2019-01-01,Vanilla Single Scoop,50,5,250
2019-01-01,Cake Fudge,150,5,750
2019-01-01,Hot Chocolate Fudge,120,3,360
2019-01-01,Butterscotch Single Scoop,60,5,300
2019-01-01,Chocolate Europa Double Scoop,100,1,100
2019-01-01,Hot Chocolate Fudge,120,2,240
2019-01-01,Caramel Crunch Single Scoop,70,4,280
2019-01-01,Hot Chocolate Fudge,120,2,240
2019-01-01,Hot Chocolate Fudge,120,4,480
2019-01-01,Hot Chocolate Fudge,120,2,240
2019-01-01,Cafe Caramel,160,5,800
2019-01-01,Vanilla Double Scoop,80,4,320
2019-01-01,Butterscotch Single Scoop,60,3,180
2019-02-01,Butterscotch Single Scoop,60,3,180
2019-02-01,Vanilla Single Scoop,50,2,100
2019-02-01,Butterscotch Single Scoop,60,3,180
2019-02-01,Vanilla Double Scoop,80,1,80
2019-02-01,Death by Chocolate,180,2,360
2019-02-01,Cafe Caramel,160,2,320
2019-02-01,Pista Single Scoop,60,3,180
2019-02-01,Hot Chocolate Fudge,120,2,240
2019-02-01,Vanilla Single Scoop,50,3,150
2019-02-01,Vanilla Single Scoop,50,5,250
2019-02-01,Cake Fudge,150,1,150
2019-02-01,Vanilla Single Scoop,50,4,200
2019-02-01,Vanilla Double Scoop,80,3,240
2019-02-01,Cake Fudge,150,1,150
2019-02-01,Vanilla Double Scoop,80,5,400
2019-02-01,Hot Chocolate Fudge,120,5,600
2019-02-01,Vanilla Double Scoop,80,2,160
2019-02-01,Vanilla Double Scoop,80,3,240
2019-02-01,Hot Chocolate Fudge,120,5,600
2019-02-01,Cake Fudge,150,5,750
2019-03-01,Vanilla Single Scoop,50,5,250
2019-03-01,Cake Fudge,150,5,750
2019-03-01,Pista Single Scoop,60,1,60
2019-03-01,Butterscotch Single Scoop,60,2,120
2019-03-01,Vanilla Double Scoop,80,1,80
2019-03-01,Cafe Caramel,160,1,160
2019-03-01,Cake Fudge,150,5,750
2019-03-01,Trilogy,160,5,800
2019-03-01,Butterscotch Single Scoop,60,3,180
2019-03-01,Death by Chocolate,180,2,360
2019-03-01,Butterscotch Single Scoop,60,1,60
2019-03-01,Hot Chocolate Fudge,120,3,360
2019-03-01,Cake Fudge,150,2,300
2019-03-01,Cake Fudge,150,2,300
2019-03-01,Vanilla Single Scoop,50,4,100
2019-03-01,Cafe Caramel,160,0,160
2019-03-01,Cake Fudge,150,5,750
2019-03-01,Cafe Caramel,160,5,800
2019-03-01,Almond Fudge,150,1,150
2019-03-01,Cake Fudge,150,1,150";

        public static void Main()
        {
            Console.WriteLine("Ice Cream Parlor — Sales Analysis Report");
            Console.WriteLine(DateTime.Now.ToString("R"));

            // --- Parse ---
            var (records, errors) = CsvParser.Parse(CsvData);

            // --- Run reports ---
            decimal totalSales = ReportEngine.TotalStoreSales(records);
            Printer.PrintTotalSales(totalSales);

            var monthWise = ReportEngine.MonthWiseSales(records);
            Printer.PrintMonthWiseSales(monthWise);

            var popular = ReportEngine.MostPopularItemPerMonth(records);
            Printer.PrintMostPopularItems(popular);

            var topRevenue = ReportEngine.TopRevenueItemPerMonth(records);
            Printer.PrintTopRevenueItems(topRevenue);

            var growth = ReportEngine.MonthToMonthGrowthPerItem(records);
            Printer.PrintGrowthPerItem(growth);

            Printer.PrintValidationErrors(errors);

            Console.WriteLine();
            Console.WriteLine("Report complete.");
        }
    }
}

// =============================================================================
// ANSWERS TO REFLECTION QUESTIONS
// =============================================================================
//
// Q1: What was the most complex part of the assignment for you personally, and why?
//
//     The month-to-month growth calculation was the trickiest section.
//     The challenge is that items don't all appear in every month, so the naive
//     approach of iterating consecutive calendar months breaks down when a SKU
//     has gaps (e.g., "Trilogy" only appears in March).  I had to build a per-SKU
//     sorted month list and compare only adjacent entries *within that SKU's own
//     history*, rather than against a fixed calendar grid.  Getting the boundary
//     case right — where the first month for any item should report "N/A" instead
//     of a division-by-zero or a false 0% — required careful thought.
//
// Q2: Describe a bug you expect to hit while implementing this and how you would debug it.
//
//     The most predictable bug is a silent mismatch in the validation stage:
//     specifically, floating-point / decimal precision drift when comparing
//     UnitPrice * Quantity to TotalPrice.  If the source data ever stores prices
//     as floating-point (e.g., 1.10 stored as 1.1000000000000001), a strict
//     equality check like `unitPrice * quantity != totalPrice` will flag rows
//     that are actually correct.
//
//     To debug this I would:
//     (a) Log the computed expected value alongside the stored value for every
//         flagged row to make the discrepancy visible.
//     (b) Check whether parsing with `decimal` (as done here) rather than
//         `double` resolves it, since decimal arithmetic is exact for base-10
//         numbers.
//     (c) If the source were truly floating-point, switch to a tolerance-based
//         comparison (Math.Abs(expected - total) < 0.01m) and document the
//         decision.
//
// Q3: Does your solution handle larger data sets without performance implications?
//
//     For most realistic retail datasets (up to a few million rows) the solution
//     scales well because:
//     - All processing is O(n) single-pass over the records list.  No nested
//       loops across the full dataset; only inner loops over per-SKU/per-month
//       sublists whose combined size equals n.
//     - Dictionary lookups are O(1) average, so building all the intermediate
//       maps is O(n) overall.
//     - Memory usage is O(n) for the records plus O(s * m) for the aggregation
//       maps (s = unique SKUs, m = unique months), both of which grow far slower
//       than n in practice.
//
//     For very large files (hundreds of millions of rows / multi-GB CSVs) the
//     current design holds all records in RAM simultaneously, which would become
//     a constraint.  The solution would need to switch to a streaming/chunked
//     read pattern — processing one line at a time and accumulating only the
//     aggregated map values, never the full record list.  The report logic is
//     already written to work on pre-aggregated dictionaries, so that migration
//     would be straightforward.
// =============================================================================
