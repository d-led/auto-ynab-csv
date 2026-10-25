# Getting your plan to agree with your bank

**The whole thing in one line:** check the plan against the bank, read one column, repeat until it says
`0.00`.

| I want to | do | time |
| --- | --- | --- |
| know whether the plan matches the bank | *The check* | about 30 s |
| fix what does not match | *The fix*, then *The check* again | about a minute a pass |
| stop for today | anywhere — every step finishes on its own | — |

## Before the first run

Two things, once.

```bash
export YNAB_TOKEN=...     # YNAB → Account Settings → Developer Settings
```

Then run *The check* once **without** any `--account` lines. It will name the exports it could not place;
those names are the labels you map. Nothing is changed by this run.

```bash
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab reconcile \
  --snapshot artifacts/ynab/today --input ~/Downloads/exports
```

Look for lines saying `Unresolved export: <label>`. Add one `--account "<label>=<id>"` per label, and
keep them in your shell history — they do not change unless you rename an account.

## Where am I?

```bash
ls -t artifacts/ynab | head -3            # newest folder = the last check you ran
wc -l < artifacts/ynab/journal.jsonl      # how many actions have been applied so far
```

## The check — changes nothing

```bash
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab reconcile \
  --snapshot artifacts/ynab/today --input ~/Downloads/exports \
  --account "label=id" --account "label=id"
```

**Open `artifacts/ynab/today/reconcile.md` and read only the first table.** Nothing else in that file
matters yet.

**You are safe to walk away here.** This step only reads.

## The fix — changes your plan

Do this only when the difference column is not `0.00`.

```bash
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab apply \
  --plan artifacts/ynab/today/plan.json \
  --journal artifacts/ynab/journal.jsonl --apply
```

It prints one line: `created N, deleted N, categorized N, moved N`. That is your receipt.

**Then go back to *The check* with a new folder name.** Never apply twice without checking in between —
the second apply is working from a picture of the plan that no longer exists.

## Am I done?

One column. `difference`.

```
account              export says     plan says   difference   missing   extra
Giro Emergency           47.89         297.89      +250.00         1       0
```

- **export says** — the number the bank printed. This is the truth.
- **plan says** — what YNAB has. `†` means you typed this balance in yourself.
- **difference** — money in YNAB that the bank does not have.
- **missing / extra** — rows the next apply would add or remove.

Every row `0.00` = finished. Not "mostly" — every row.

## Am I going in circles?

Each pass should be smaller than the last. Real history with this tool:

| pass | to add | to remove | to move |
| --- | ---: | ---: | ---: |
| 1 | 25 | 90 | 32 |
| 2 | 16 | 7 | 12 |
| 3 | 2 | 8 | 0 |
| 4 | 16 | 2 | 0 |
| 5 | 3 | 15 | 0 |

Two to three passes is usual. **Pass 4 got bigger again, and that was a bug, not a mood** — rows the tool
had created were deleted by a later pass and could not be written back, so they were proposed forever.

So: **if the same numbers come back twice, stop.** Read `## Where the ledgers stop agreeing` in the
report — it dates the gap. A gap that will not move is a decision to make, not a loop to keep running.

## One line each

| you see | it means |
| --- | --- |
| `The hour's 200 requests are spent` | the tool keeps its own count of the 200/hour limit; it tells you when to come back and sends nothing. Not a failure. |
| a memo that stops mid-sentence | DK B writes interest statements longer than the API accepts. Cut, not lost. |
| `Unresolved export: <label>` | that export needs `--account`. If it prints no balance and no account number (n26 does neither), also `--own-iban` and `--expect-balance`. |
| the numbers do not shrink twice | stop and read `## Where the ledgers stop agreeing`. |
| you applied the wrong plan | one `undo`, below. |

A check of three years of history costs about 38 of the hour's 200 requests, so it is worth running twice
a day, not twice a minute. The count lives in
`~/Library/Application Support/auto-ynab-csv/ynab-requests.txt` — delete that file only if you know the
hour is free.

## Changing your mind

Every apply appends to the journal, and the journal is enough to walk back:

```bash
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab undo \
  --journal artifacts/ynab/journal.jsonl --apply
```

Undo replays the last apply backwards: it deletes what was created, recreates what was deleted, and moves rows back.

## What stays yours

The tool will not decide these:

- a transfer to an account you did not export — it cannot see the other side
- a category — it proposes one from history, and only when the payer has one 3 times in 4
- an account type

If the report asks for a decision, that is the tool saying it has run out of evidence. Answer it, then run the loop again.
