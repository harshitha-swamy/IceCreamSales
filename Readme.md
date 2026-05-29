# 🍦 Ice Cream Parlor — Sales Analysis Engine

> A **.NET 10 console application** that ingests raw CSV sales data, validates it for integrity, and produces six analytical reports — built entirely on primitive data structures without LINQ, SQL, or third-party libraries.

---

## 📋 Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Reports Generated](#reports-generated)
- [Data Validation](#data-validation)
- [Design Decisions](#design-decisions)
- [Performance Considerations](#performance-considerations)
- [Known Data Issues in Source](#known-data-issues-in-source)
- [Reflection](#reflection)

---

## Overview

This project was built as part of a technical assessment for a **.NET Developer** role. The goal was to demonstrate proficiency in:

- **Algorithm design** using basic data structures (`Dictionary`, `List`)
- **Data parsing and validation** without relying on ORM or query frameworks
- **Clean code architecture** — separation of concerns, single-responsibility classes
- **Analytical thinking** — deriving business insights from raw transactional data

The dataset contains **59 sales transactions** from an ice cream parlor spanning **January–March 2019**, with fields: `Date`, `SKU`, `Unit Price`, `Quantity`, and `Total Price`.

---

## Features

| # | Report | Description |
|---|--------|-------------|
| 1 | **Total Store Sales** | Grand total revenue across all transactions |
| 2 | **Month-wise Sales** | Revenue breakdown per calendar month |
| 3 | **Most Popular Item** | Highest quantity sold per month + min/max/avg order stats |
| 4 | **Top Revenue Item** | Highest revenue-generating SKU per month |
| 5 | **Month-to-Month Growth** | Percentage revenue change per item between consecutive months |
| 6 | **Data Validation** | Flags all rows with integrity violations |

---

## Project Structure

```
IceCreamSales/
├── Program.cs              # Entry point + all application logic
│   ├── SaleRecord          # Immutable domain model (valid row)
│   ├── ValidationError     # Captures invalid rows with reason
│   ├── CsvParser           # Parses and validates raw CSV text
│   ├── ReportEngine        # Pure static report computation methods
│   └── Printer             # Console output formatting layer
├── IceCreamSales.csproj    # .NET 10 project configuration
└── README.md               # This file
```

### Class Responsibilities

```
CsvParser
  └─ Parse()  →  splits each row, validates 5 integrity rules,
                 returns (List<SaleRecord>, List<ValidationError>)

ReportEngine  (no state — all methods are pure functions)
  ├─ TotalStoreSales()
  ├─ MonthWiseSales()
  ├─ MostPopularItemPerMonth()
  ├─ TopRevenueItemPerMonth()
  └─ MonthToMonthGrowthPerItem()

Printer
  └─ One Print* method per report — all console I/O isolated here
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run Locally

```bash
# Clone or download the project
cd IceCreamSales

# Run directly
dotnet run

# Or build first, then execute
dotnet build
dotnet ./bin/Debug/net8.0/IceCreamSales.dll
```

### Run Online (No Installation)

Paste `Program.cs` into any of these online C# compilers:

| Compiler | URL |
|----------|-----|
| .NET Fiddle | https://dotnetfiddle.net — set compiler to **.NET 10** |
| Replit | https://replit.com — create a new **C# Console** repl |
| OneCompiler | https://onecompiler.com/csharp |

> **Note:** Change `internal` to `public` on class declarations if the online compiler reports access errors.

---

## Reports Generated

### 1. Total Store Sales
Single-pass accumulation over all valid records.

### 2. Month-wise Sales Totals
```
Dictionary<"YYYY-MM", decimal>
```
Groups and sums `TotalPrice` by month key.

### 3. Most Popular Item Per Month
```
Dictionary<month, Dictionary<sku, List<int>>>   // int = per-transaction quantity
```
Finds the SKU with the highest cumulative quantity per month, then computes order-level statistics (min, max, average) from its transaction list.

### 4. Top Revenue Item Per Month
```
Dictionary<month, Dictionary<sku, decimal>>
```
Aggregates `TotalPrice` per SKU per month and surfaces the top earner.

### 5. Month-to-Month Growth Per Item (%)
```
Dictionary<sku, Dictionary<month, revenue>>
→ sorted month list per SKU
→ compare adjacent entries only
```
Formula: `((Current - Previous) / Previous) × 100`

Handles edge cases:
- First month for any SKU → `N/A` (no prior baseline)
- Prior revenue = 0 → `N/A` (division-by-zero guard)
- SKUs with gaps in months → compares only months the item actually appeared in

### 6. Data Validation
See [Data Validation](#data-validation) section below.

---

## Data Validation

Every row is checked against five rules before being admitted into the report pipeline. Invalid rows are collected separately and never pollute the analytics.

| Rule | Check |
|------|-------|
| Price consistency | `UnitPrice × Quantity == TotalPrice` |
| Valid quantity | `Quantity >= 1` |
| Valid unit price | `UnitPrice >= 0` |
| Valid total price | `TotalPrice >= 0` |
| Valid date format | Parseable as `yyyy-MM-dd` |

### Known Issues in the Source Dataset

Two rows in the provided data fail validation:

```
Line 55 | 2019-03-01,Vanilla Single Scoop,50,4,100
         Reason: 50 × 4 = 200, but Total Price = 100  ❌

Line 56 | 2019-03-01,Cafe Caramel,160,0,160
         Reason: Quantity is less than 1 (0)  ❌
```

These rows are excluded from all reports and surfaced in the validation output.

---

## Design Decisions

### Why `decimal` instead of `double` for prices?

`decimal` uses base-10 arithmetic and is exact for monetary values. Using `double` risks floating-point drift (e.g., `50 × 4.0 = 199.9999...`) which would produce false validation failures on perfectly valid rows.

### Why are invalid rows excluded rather than coerced?

Silently fixing bad data (e.g., recalculating a wrong total) hides upstream data quality problems. The application surfaces them explicitly so they can be corrected at the source.

### Why a `Printer` class instead of inline `Console.WriteLine`?

Isolating all output means the report logic in `ReportEngine` is testable without capturing console output. It also makes it trivial to swap the output target (file, HTTP response, etc.) later.

### Why `init`-only properties on `SaleRecord`?

Records are created once by the parser and should never mutate. `init` enforces this at compile time without the ceremony of a full readonly struct.

---

## Performance Considerations

| Concern | Approach |
|---------|----------|
| Time complexity | O(n) — single pass through records for all reports |
| Space complexity | O(n) for record list + O(s × m) for aggregation maps (s = SKUs, m = months) |
| Dictionary lookups | O(1) average — no nested full-dataset loops |
| Sort operations | Applied only to small month/key lists, negligible cost |

**For very large datasets (100M+ rows / multi-GB files):**

The current design holds all records in memory simultaneously. For files that exceed available RAM, the solution would migrate to a **streaming/chunked reader** — processing one line at a time and accumulating only the aggregated map values (never the full record list). The `ReportEngine` methods already operate on pre-aggregated structures, so this migration would be confined to `CsvParser` and `Program.Main`.

---

## Reflection

### Most Complex Part

The **month-to-month growth calculation** (Report 5) was the most nuanced part. The challenge is that not every SKU appears in every month — a naive loop over `[Jan, Feb, Mar]` breaks down when a SKU has gaps (e.g., *Trilogy* only appears in March). The solution builds a **per-SKU sorted month list** and compares only adjacent entries *within that item's own history*, correctly producing `N/A` for a first appearance rather than a false 0% or a divide-by-zero exception.

### Bug I Expected to Hit

A **decimal precision mismatch** in the price consistency check. If prices were stored as `double`, `50 × 4.0` might evaluate to `199.9999999...` rather than `200`, causing valid rows to be flagged as errors. I pre-empted this by using `decimal` throughout and verified it by checking the two legitimately invalid rows are the only ones flagged.

### Scalability

The solution scales to millions of rows without modification (O(n) time, O(s×m) space). For multi-GB inputs, a streaming read pattern would be the only required change, and the report computation layer would remain untouched.

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Language | C# 12 |
| Runtime | .NET 10 |
| Data structures | `Dictionary<K,V>`, `List<T>` |
| External libraries | None |
| Database / ORM | None |

---

Contact
name: HARSHITHA S
GitHub: https://github.com/harshitha-swamy
LinkedIn: https://www.linkedin.com/in/harshitha-s-swamy/
Email: harshithaswamy3124@gmail.com

Thank you for reviewing this submission.