# Getting your plan to agree with your bank

One page. Five commands. No theory.

Every step below prints what it did. If a step prints nothing, it did nothing.

## What you need once

| thing | where it comes from | how long it lasts |
| --- | --- | --- |
| `YNAB_TOKEN` | YNAB → Account Settings → Developer Settings | until you revoke it |
| Plan id | `ynab plans` | forever |
| Bank exports | one folder per bank download | replace it each run |
| Account mapping | `--account "label=id"`, one per export | until you rename an account |

Get the labels first — they are what the tool calls each file:

```bash
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab accounts
```

## The loop

Run these two, in this order, and nothing else:

```bash
# 1. Read the plan, then hold it against the exports. Changes nothing.
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab reconcile \
  --snapshot artifacts/ynab/today --input ~/Downloads/exports \
  --account "label=id" --account "label=id"
```

Open `artifacts/ynab/today/reconcile.md`. Read only the first table.

```bash
# 2. Only if the difference column is not 0.00.
dotnet run --project AutoYnabCsv.ConsoleApp -- ynab apply \
  --plan artifacts/ynab/today/plan.json \
  --journal artifacts/ynab/journal.jsonl --apply
```

Then run step 1 again into a new folder. Repeat until the difference column is `0.00`.

## Reading the first table

```
account              export says     plan says   difference   missing   extra
Giro Emergency           47.89         297.89      +250.00         1       0
```

- **export says** — the number printed by the bank. This is the truth.
- **plan says** — what YNAB has. `†` means you typed this balance by hand.
- **difference** — money YNAB has that the bank does not.
- **missing / extra** — rows to add / remove in the next apply.

`0.00` on every line means done. Nothing else in the file matters until then.

## How many passes

Two or three is normal, and each one is smaller. What happened on a real file:

| pass | rows to add | to remove | to move |
| --- | ---: | ---: | ---: |
| 1 | 25 | 90 | 32 |
| 2 | 16 | 7 | 12 |
| 3 | 2 | 8 | 0 |

Stuck on the same numbers twice? Stop and read the `## Where the ledgers stop agreeing` section — it dates the gap. A gap that will not move is a decision, not a bug.

## Things that will bite you

- **200 requests an hour.** A snapshot costs one request per month of history, so a 3-year plan costs about 35. Two checks a minute will get you a `429` and a wait. That is the limit working, not a failure.
- **A memo is capped at 500 characters.** DK B puts a whole interest statement in one. It is cut, not rejected.
- **`cleared` is a word, not a boolean.** `cleared`, `uncleared`, `reconciled`.
- **Transfers are one row, not two.** Creating both legs doubles the money.
- **An export that prints no balance and no account number** (n26 does neither) needs `--own-iban` and `--expect-balance`, or its transfers are invisible.
- **The plan is only as good as the export.** An export that stops last month will "delete" last month's rows. Check the period, not just the balance.

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
