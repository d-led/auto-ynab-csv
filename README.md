# auto-ynab-csv

> limited-use perpetual WIP automatic converter to [YNAB CSV](https://support.ynab.com/en_us/formatting-a-csv-file-an-overview-BJvczkuRq)

## Why?

- Account sync doesn't work for certain banks in YNAB.
- Some export formats require manual corrections even with available semi-automatic tools.
- Choosing the format manually should not be necessary.

## Usage

```shell
auto-ynab-csv {exported.csv} > {converted.csv}
```

replacing the placeholders accordingly.

Alternatively, let the app create a file next to the original one:

```shell
auto-ynab-csv convert --input {exported.csv}
```

A conversion is one step, not three. It echoes what it is about to do, then leaves one folder
next to the exports it read — `auto-ynab-report/<timestamp>/` — holding

- `index.html` — the searchable report to look at,
- `ynab/` — the rows to import into YNAB, one file per export,
- `audit.json` and `mail-index.json` — what was read, and what it was read from.

What a run prints, before any work and after it:

```text
Input:   7 export(s) in ~/Downloads/ynab-19oct2026
Output:  ~/Downloads/ynab-19oct2026/auto-ynab-report
Search:  on

Converting, indexing mail, and searching the mailbox for what it cannot explain…

Mail:    273 messages covering 2021-03-04 … 2026-10-18.
Found:   12 entries looked up, 34 mailbox hits, 9 matched, 3 bodies fetched.
Report:  ~/Downloads/ynab-19oct2026/auto-ynab-report/20261019-234524/index.html
Import:  ~/Downloads/ynab-19oct2026/auto-ynab-report/20261019-234524/ynab (7 file(s))
```

Every run gets its own timestamped folder, so nothing collides and yesterday's import is never
overwritten. The report is the same for one file and for a folder.

Pass a folder instead of a file and it converts the whole batch into one report:

```shell
auto-ynab-csv convert --input {folder-of-exports}
```

Add `--search` to let the conversion look in the mailbox for the entries the index cannot explain,
including messages that were never downloaded.

## GUI Usage

Drop the exported CSV files onto the app window. The report is written next to them, in
`auto-ynab-report/`, and opens from there.

![desktop app](./docs/img/desktop-app.png)

## Mail-assisted payees and memos (macOS, optional)

Bank exports hide what a purchase was. YNAB will only ever see the row it imports, so the context
has to travel in the row itself: the **Payee** becomes the merchant, and the **Memo** carries the
gist of the mail — the items with their prices, the order number, and where the purchase really
happened (`via paypal`, `via kleinanzeigen`). If your mail is in Apple Mail, the converter reads it
locally, correlates purchases with the exported entries, and writes the enriched rows.

### Why the mail client, and never the mail account

The tool never talks to a mail server. It has no IMAP host, no password, no app token, no OAuth
consent — there is no account configuration in it at all. The mail client is the source of truth
for three things that are not ours to manage:

- **Configuration** — which accounts exist, which mailboxes, which filters and threads. Asking for
  an IMAP host would mean asking the user to describe their mail setup a second time, and to keep
  both descriptions in step.
- **Authorization** — the accounts on this Mac are already authorized, by the system and by the
  user, once. A tool that connected directly would own a second set of credentials: to store, to
  rotate, to fail closed on, and to leak.
- **Content** — a body that was never downloaded is on the server, and the mail client is the only
  thing allowed to fetch it. A direct connection can only see what the server exposes and would
  have to reimplement per-provider quirks (Gmail labels, Exchange, 2FA, app passwords).

Connecting directly is possible — MailKit does it well — but it turns a converter into an account
manager, and this tool does not manage accounts. So mail assistance is **macOS with Apple Mail
only**: the local store is read where it is, and bodies are fetched through Mail itself, with the
permission macOS asks for.

Everywhere else nothing breaks: the conversion, the report and the YNAB rows still come out of the
exports alone, and the run says which half is missing instead of pretending.

| | macOS + Apple Mail | Windows / Linux |
| --- | --- | --- |
| convert an export, report, YNAB rows | yes | yes |
| read messages, correlate, enrich payee and memo | yes | no |
| fetch a body that was never downloaded | yes, through Mail | no |
| `mail sync` / `mail scan` / `mail patterns` | yes | patterns only |

1. Grant **Full Disk Access** to the program you run (`auto-ynab-csv`, or VS Code/Terminal while
   developing) in System Settings → Privacy & Security → Full Disk Access.
2. Index the mail; later runs read only what changed, and a conversion does this by itself:

   ```shell
   auto-ynab-csv mail sync
   ```

   While developing, `./scripts/mail-sync.sh` does the same from source and tells you when the
   process it runs in has no Full Disk Access. An editor's integrated terminal usually does not;
   `./scripts/mail-sync.sh --via-terminal` retries inside Terminal.app, which may have it.

3. See the facts that were extracted:

   ```shell
   auto-ynab-csv mail scan --limit 50
   ```

   To write a pattern, look at what it has to match first. `mail text --match <words>` prints the
   plain text of every message that mentions something — an order number from the bank row, for
   instance:

   ```shell
   auto-ynab-csv mail text --match 028-1234567-8901234
   ```

   The index keeps facts and not bodies, so that text is read from disk when asked for.

4. Correlate an export and write draft rules:

   ```shell
   auto-ynab-csv mail correlate --input data/samples/dkb-giro.csv
   ```

5. Write the searchable report for a folder of exports:

   ```shell
   auto-ynab-csv mail export --input <folder>
   ```

   The report goes into `auto-ynab-report/<timestamp>/` next to the first export, with
   `index.html`, `audit.json`, `mail-index.json` and the importable rows in `ynab/`. Pass
   `--out <dir>` to put it somewhere else. Files this tool wrote earlier are not mistaken for
   exports on the next run.

   Each run gets its own timestamped folder. Besides `index.html`, `mail-index.json` and
   `audit.json` it holds `ynab/`, one YNAB CSV per export, ready to import:

   ```text
   Date,Payee,Memo,Inflow,Outflow
   2026-10-02,Thomann GmbH,"Saitensatz 10-52, 12,50 € · Order 12345678 · via thomann",0,177.29
   ```

   A memo is only filled with what the mail proves: without a confident match the row stays
exactly as the bank wrote it.

   Money moving between two of your own exported accounts is named after the account on the
   other side of the crossing, and not after whoever the bank printed: the arrival reads
   `Transfer : Girokonto · 0001`, the departure `Transfer : n26` — the payee YNAB itself gives a
   transfer, so the import lands as a transfer instead of a payment to a person. The account
   name is the file's own, so it matches your YNAB account if you named the account the same way.
   The report names the same pair the same way in its `facts:` line. A card settlement is paired
   up to five days apart — the card account books the balance before the current account debits
   it — because both rows say that is what they are.

   The page searches every entry with AND over its words (prefixes, substrings and one typo
   count too). Every value in a result is labelled (`merchant:`, `amount:`, `order:`, `file:`,
   `facts:`), and a transaction is listed together with the mails that prove it — a query finds
   the pair, not two unrelated rows. The transfer graph sits behind a collapsible section and is
   drawn with [Mermaid](https://mermaid.js.org/) from a CDN; without a network the rest of the
   page still works.

   Only the mails a row was built from are on the page: the report is the evidence for what was
   imported, not a second copy of the mailbox. The audit block says how many of the indexed
   messages were kept.

   Rows link back into Apple Mail: every mail row, and every transaction whose row was built from
   a mail, has an `open in Mail` link. It is a `message://` URL made from the message's
   `Message-ID`, so it opens the exact message on this Mac.

### What the index covers

The index only knows what Mail has downloaded. `mail where` prints the oldest and newest message
it holds, and `mail sync` counts the stubs (`.partial.emlx`) Mail leaves for messages that are on
the server only. A stub keeps the headers and not the body, so `--search` reads the headers of
the stubs — the subject, the sender, the day — and never throws away a message that is only
there.

A conversion reads the span its exports cover, not the mailbox. The first row's day and the last
row's day decide how far back the store is read (with a week of margin, because a charge and its
mail can sit days apart), and only the facts inside that span are loaded for matching. Two runs
over different spans simply add to the same index — a differing span never clears it, since a
rebuild happens only when the patterns change or `mail sync --full` is asked for. Repeating a run
over the same span reads nothing again: every message is compared by file time and length before
it is parsed.

A long first sync reports its progress (`2,500/80,076 messages, 2,412 indexed, 0 already known, 0
unreadable`) and, when something cannot be read, names it — the file and the reason, five at a
time, so a broken container or a denied folder is something you can open instead of a bare
number.

The work survives an interruption. Messages are written in batches of 250 in one transaction, and
the rules fingerprint is recorded in the same transaction that clears the index for a rebuild —
so a run stopped with Ctrl-C keeps everything it had written, and the next start carries on
instead of clearing and parsing it all again (the counter then shows the messages as
`already known`).

For entries the index cannot explain at all, `--search` reads the stubs instead: the messages
whose body Mail still holds on the server. A stub is judged on what its headers say, and only
the ones whose day could still fit are offered — the query is built from what the entry itself
says, precise first:

- the **reference numbers** the transfer carries (digit runs of nine or more, e.g.
  `1050000000001`) — the number the confirmation quotes back, searched on its own;
- otherwise the **merchant** it names, but only inside the days the transaction allows, so a
  shop's newsletter from another month cannot stand in as evidence. A header says
  `Thomann Bestellung`, not `Thomann GmbH`, so the longest word of the name is what has to
  appear; the amount and the day still have to fit before anything is believed.

```shell
auto-ynab-csv convert --input <folder> --search
```

`--search` is also how messages that were never downloaded become evidence. Such a message has
headers on this Mac and its body on the server, so when a hit's date could still fit the entry,
Mail is asked for the body (AppleScript, `osascript`) — that makes Mail download it. The date is
checked first, a message that is already complete is never fetched, and each message is fetched at
most once per run. What the body yields — amounts, order numbers, vendor, the fields the rules
extract — is stored in the index; the body itself is not. macOS asks once for permission to
control Mail; without it, search still works and only bodies stay missing.

The index lives in `~/Library/Application Support/auto-ynab-csv/mail-index.sqlite`
(`%APPDATA%` on Windows, `$XDG_DATA_HOME` elsewhere). It is a cache: delete it and run
`mail sync` again at any time. Nothing is uploaded; mail never leaves the machine.

It is one SQLite file in WAL mode with `synchronous=NORMAL` and a five-second `busy_timeout`, so
the CLI and the app can touch it at the same time instead of failing on a lock. Search is
FTS5 over the tokenizer `unicode61 remove_diacritics 2 tokenchars '-_./'`: an order number or a
transaction code stays one token, so `028-1234567-8901234` is found as written, and `Händler`
matches `handler`. Queries are quoted word by word, so what you type cannot arrive as FTS5 syntax.

### Extending the patterns

Extraction rules turn mail patterns into named fields that matching can use. They are shipped as
one `patterns.jsonc` embedded in the binary — data, not code — so the rules that run are the rules
you can read. Copy them out and edit them:

```shell
auto-ynab-csv mail patterns --write      # -> ~/.config/auto-ynab-csv/patterns.jsonc
```

The shipped file is read first, then the same file name is laid over it, nearest wins:

- the executable's directory,
- `~/.config/auto-ynab-csv/patterns.jsonc`,
- the nearest `.auto-ynab-csv/patterns.jsonc` in a parent of the working directory.

Merging is **by rule id**, so a file with one rule replaces that rule and leaves the others
(`amazon.order`, `paypal.payment`, `thomann.order`, `kytary.order`, `db.booking`,
`kleinanzeigen.sale`) alone, and
a rule with a new id is simply added. A copy in your config wins over a later change to the
shipped rule — delete it to get the shipped version back.

One field name is special: `items` keeps **every** match instead of only the first, one item per
line. That is what the memo lists; every other field keeps its first match.

The same file names the senders whose mail no rule matches, under `vendors`. It merges **by
domain**, so adding a shop is one line and renaming one is a one-line override:

```jsonc
{
  "version": 1,
  "vendors": { "kabelshop.example": "Kabelshop", "amazon.de": "Amazon (mine)" },
  "rules": [] // unchanged, and still the shipped ones
}
```

```jsonc
{
  "version": 1,
  "rules": [
    {
      "id": "amazon.order",
      "sender": "amazon\\.",
      "subject": "(Bestellung|Order|Versand|Rechnung|invoice)",
      "vendor": "Amazon",
      "fields": {
        "orderNumber": "\\b\\d{3}-\\d{7}-\\d{7}\\b",
        "total": "(?:Gesamtsumme|Gesamtbetrag|Order Total|Summe|Total)[^0-9]{0,20}(?:EUR|€)?\\s*([0-9]+[.,][0-9]{2})",
        "items": "(?m)^\\s*\\d{1,2}\\s*x\\s+(?<item>[^\\r\\n]+)$"
      }
    }
  ]
}
```

Invisible direction marks and non-breaking spaces are removed before any pattern runs, so an
order number that arrives as `Order # ‫028-1234567-8901234` is matched as if it were written
plainly.

`auto-ynab-csv mail patterns` shows the rules in effect, the fingerprint the index was built
with, and every place a file is looked for. Changing a rule rebuilds the index on the next
`mail sync`, because stored facts would otherwise be stale. So does a change to the way a
message's text is read — the reader's own version is part of the same fingerprint — and so does
`mail sync --full`. Everything else, including the phrases under `transfers` and how a memo is
written, takes effect on the next conversion without touching the index.

## Checking the plan against the bank exports (optional)

The converter's job ends at importable rows. This one does not: it reads your YNAB plan over the API
and holds it against the same exports, so "does my plan say what my bank says" is a question with an
answer instead of a feeling.

The whole loop in one page — check, apply, check again, and which number to look at — is
[docs/workflow.md](docs/workflow.md).

```shell
export YNAB_TOKEN=...            # a personal access token; read from the environment, never printed
auto-ynab-csv ynab snapshot  --out artifacts/ynab
auto-ynab-csv ynab reconcile --snapshot artifacts/ynab --input {folder-of-exports}
```

`snapshot` reads the plan once — accounts, categories, payees, and every transaction since
2024-01-01, a month at a time — into `snapshot.json`. Everything afterwards is answered from that
file: a question about a balance costs no request, and the plan cannot change underneath an argument
about it. (A token allows 200 requests an hour; the snapshot spends about forty and says so as it
goes.) The `since_date` is always sent explicitly, because the API otherwise defaults to the last
year and would silently hide the very months worth checking.

`reconcile` pairs each export with the account it belongs to — by name, by the IBAN's last four
digits, or by being the only account holding that balance; `--account "Girokonto · 0001=<id>"`
decides a case by hand — then answers four questions per account:

- does the plan hold the balance the export declares for its last day,
- which bank rows the plan does not have,
- which plan transactions the bank does not have — split into duplicates of a matched row, entries
  that look like corrections (a payee that says `balance adjustment`, money pushed back into Ready to
  Assign), and everything else,
- **when** the two ledgers stopped agreeing: both balances are walked backwards from the day the
  export was written, so a single difference becomes a date range to look at.

```shell
auto-ynab-csv ynab accounts  --snapshot artifacts/ynab     # names, types, balances, ids
auto-ynab-csv ynab balances                                # the plan's balance per account; ONE request
auto-ynab-csv ynab reconcile --snapshot artifacts/ynab --input ~/Downloads/ynab-25-oct
auto-ynab-csv ynab apply     --plan artifacts/ynab/plan.json            # dry run: prints the plan
auto-ynab-csv ynab apply     --plan artifacts/ynab/plan.json --apply    # removes, moves, categorises
auto-ynab-csv ynab undo      --journal artifacts/ynab/journal.jsonl --plan-id <id> --apply
```

`reconcile` writes `reconcile.md` (what a person reads), `reconcile.json` (what a script reads) and
`plan.json` — the smallest set of changes that makes the two agree: the rows only the bank has are
added, the corrections holding a gap shut are removed, and the categories the plan's own history
argues for are set. Nothing changes until `apply` is given `--apply`; every created transaction is
`approved: false`, so it is visible in YNAB before it counts; and every action is written to a
journal first, so `undo` can put back what `apply` took.

### Rows to add cost nothing; the API is for what a file cannot do

Importing a CSV is free and an API request is not, so **rows are added by import by default**:
`apply` writes one file per account into `import/` beside the plan (`--csv DIR` puts them elsewhere),
and you drag them into YNAB the way you always have. The API is then only spent on the three things a
file cannot express — **removing** a wrong row, **moving** one to the account it belongs in, and
**categorising** — which for a repair is the small half of the work.

```shell
auto-ynab-csv ynab apply --plan artifacts/ynab/plan.json --apply            # deletes/moves/categories
auto-ynab-csv ynab apply --plan artifacts/ynab/plan.json --apply --create   # also add rows over the API
```

`--create` exists for the one case where the two must happen together: deleting one leg of a transfer
takes the other leg with it, and the row written back afterwards cannot wait for a manual import. Use
it when the plan's additions are repairs to its own removals; import the rest.

Two more ways the allowance is respected:

- `ynab balances` answers "does the plan hold what the bank says" with **one** request, by comparing
  against the closing balance each export prints. A snapshot costs one request per month of history,
  so this is the check to run between repairs.
- `./scripts/check.sh <exports> <snapshot-folder>` reuses a snapshot that is already there and spends
  **nothing**, so changing a rule and re-planning is free.

Two things keep a repair from being an accident:

- **an import id per movement** (`ayc-20261023-<hash>`), stable for the same account, day, amount and
  words — applying the same plan twice finds its own work and says so instead of adding a copy;
- **a review list that stops at 50**: categories are proposed from how the same payee was filed
  before (at least three times, at least 80 % of them the same), and what is left is short enough to
  decide rather than a wall.

An account whose export carries no balance — n26's download has none — cannot be proven by a
balance; the report prints `unknown` instead of inventing one.

Money that moves between two of your own accounts is recognised from the account number the export
prints for the other side: a row whose counterpart is one of the other exports is half of a
transfer. When both halves are missing the plan adds **one** entry, as a transfer, and lets YNAB
write the other leg — adding both would put the same money in twice. A movement the plan holds
twice, because a re-import never paired it, is reported as a duplicate and removed with the rest
(`--no-duplicates` leaves them where they are).

## Scripts

The scripts wrap the commands above and pass extra arguments straight through. The `.sh` and
`.bat`/`.ps1` twins do the same thing on macOS/Linux and Windows:

| what | bash | PowerShell |
| --- | --- | --- |
| build everything | `./scripts/build.sh` | `./scripts/build.ps1` |
| run the tests | `./scripts/test.sh` | `./scripts/test.ps1` |
| quality gate | `./scripts/quality-gate.sh` | `./scripts/quality-gate.ps1` |
| convert exports in place | `./scripts/convert.sh <folder>` | `./scripts/convert.ps1 <folder>` |
| convert with a mailbox search | `./scripts/convert.sh <folder> --search` | `./scripts/convert.ps1 <folder> -Search` |
| index mail from source | `./scripts/mail-sync.sh` | — |
| any `mail` command | `./scripts/mail.sh sync` | `./scripts/mail.ps1 sync` |
| report + YNAB CSVs | `./scripts/report.sh <folder>` | `./scripts/report.ps1 <folder>` |
| single-file packages | `./scripts/package.sh osx-arm64` | `./scripts/package.ps1 win-x64` |
| the CLI, raw | `./scripts/run.sh version` | `./scripts/run.ps1 version` |

```shell
./scripts/convert.sh ~/Downloads
```

### Quality gate

`./scripts/quality-gate.sh` runs every static check in one go and prints what to fix:

- `dotnet format` — formatting (advisory unless `--strict-format`),
- `dotnet build -warnaserror` — compiler warnings are errors,
- `dotnet test` with coverage — the suite must be green,
- **CRAP per method** — complexity and coverage combined (`scripts/crap.py`); above
  `--crap-threshold` (default 30) fails,
- **jscpd** — duplicate code above `--duplication` percent (default 3) fails.

CRAP and coverage reports land in `artifacts/quality-gate/`, which is git-ignored. Missing
tools are skipped with a note, never guessed.

For the layout of the code, when the index throws itself away and reads the mailbox again, and how
to add an extraction rule, see [docs/developer-guide.md](docs/developer-guide.md).

## Installation


### Download a Release

Pick a [release](https://github.com/d-led/auto-ynab-csv/releases) and the platform you are on
(`osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`). Windows downloads are `.zip`; the rest are `.tgz`.
There are two of them, and **nothing needs to be installed** — each program is one self-contained
file:

| download | what you get |
| --- | --- |
| `auto-ynab-csv-<version>-<platform>` | `auto-ynab-csv` — the command line — and `auto-ynab-csv-mcp`, the server an editor starts |
| `auto-ynab-csv-desktop-<version>-<platform>` | the window, for when you would rather click than type |

They are separate because the window is over half the weight, and nobody downloading a command line
wants a GUI riding along.

```shell
unzip auto-ynab-csv-0.0.6-win-x64.zip            # or: tar xzf auto-ynab-csv-*.tgz
chmod +x auto-ynab-csv auto-ynab-csv-mcp        # macOS and Linux only
sudo mv auto-ynab-csv auto-ynab-csv-mcp /usr/local/bin/     # or anywhere on your PATH
```

On macOS you may need to clear the quarantine flag on both:
`sudo xattr -d com.apple.quarantine /usr/local/bin/auto-ynab-csv*`.

The desktop download carries `AutoYnabCsv.DesktopApp.app` on macOS — a bundle rather than a bare
binary, because the window needs one for its identity and its privacy grants.

### Build Yourself

```shell
./scripts/build.sh
```

See the output for the binary location.

To build a Mac App bundle:

```shell
./scripts/build-app.sh
```

To package one self-contained file per target (a single `.exe`/binary for the CLI and the app;
on macOS a single `.app` inside the archive, because macOS needs the bundle):

```shell
./scripts/package.sh                     # the current OS
./scripts/package.sh osx-arm64 win-x64   # explicit targets
```

## Currently Supported Export Formats

- [n26 Download](https://support.n26.com/en-eu/account-and-personal-details/bank-statements-and-confirmations/how-to-get-bank-statement-n26)
- DKB Giro (new) CSV
- DKB Visa (new) CSV

## Alternatives

- [github.com/aniav/ynab-csv](https://github.com/aniav/ynab-csv) - manual number format conversion necessary afterwards, manual clean-up of CSV of summary lines that don't conform to the format

## Using the answers from an editor (MCP, optional)

The reconciliation already knows things you might want to ask about while staying in the editor: what a
plan would change, what an apply did, how far the plan has drifted from the bank. Those questions read
files that are already on disk, so answering them costs no API request — which is why they are served
over MCP rather than as more commands.

### What you need first

1. **Nothing, if you took a release.** `auto-ynab-csv-mcp` from the archive is a self-contained
   binary; the editor starts it directly. Skip to *Registering it*.

   **Or the .NET 10 SDK, if you work from source.** Same as the rest of this project:
   `dotnet --version` should print 10 or later — and the server has to be built, because the editor
   starts a *built* assembly:

   ```shell
   dotnet build AutoYnabCsv.Mcp
   ```

   It lands in `AutoYnabCsv.Mcp/bin/Debug/net10.0/auto-ynab-csv-mcp.dll`. If that file is missing the
   server shows as failed in the editor with "no such file" — build it.
2. **Something to read.** Every tool reports on files the console app produced earlier, so run
   `ynab snapshot` (and `ynab reconcile`, `ynab plan`, `ynab apply`) at least once. The default
   locations are `artifacts/ynab/<stamp>/` and `artifacts/ynab/journal-*.jsonl`; both are git-ignored
   and never committed.
3. **Your account mapping, one file, outside the repository.** The same file `scripts/check.sh` reads:

   ```shell
   mkdir -p ~/.config/auto-ynab-csv
   $EDITOR ~/.config/auto-ynab-csv/accounts.txt
   ```

   ```ini
   plan=00000000-0000-0000-0000-000000000000
   account=Girokonto · 0002=00000000-0000-0000-0000-000000000001
   account=Girokonto · 0003=00000000-0000-0000-0000-000000000002
   own-iban=n26=DE00 0000 0000 0000 0000 00
   expect-balance=n26=-25,00
   ```

   Keep it there rather than in the repository: it holds identifiers you probably do not want to
   publish. A missing file is not an error — it only means the tools cannot pair an export with an
   account on their own.

### Registering it

Create `.vscode/mcp.json` if it is not there yet (it is deliberately not tracked — the command in it
is yours, not the repository's), paste the block for the way you installed this, then in VS Code:

1. Open the Explorer or run **MCP: List Servers** from the command palette (`F1`).
2. `auto-ynab-csv` is listed; choose **Start** (or open the file and press the ▶ above it).
3. The editor asks you to trust the server the first time — it runs local code, so say yes.

To use it in another editor or in your own client, the registration is plain stdio. **If you
downloaded a release**, the command is the binary itself — no runtime, no path to a DLL:

```json
{
  "servers": {
    "auto-ynab-csv": {
      "type": "stdio",
      "command": "auto-ynab-csv-mcp"
    }
  }
}
```

**If you built from source**, `scripts/mcp.sh` builds the checkout and then runs the server, so every
start gets the code as it is on disk — no stale DLL, nothing to remember:

```json
{
  "servers": {
    "auto-ynab-csv": {
      "type": "stdio",
      "command": "/absolute/path/to/auto-ynab-csv/scripts/mcp.sh"
    }
  }
}
```

The script exists because `dotnet run` is not enough on its own: it prints build output on **stdout**,
which is exactly where the protocol's messages go, and the client reads that as garbage. `mcp.sh`
sends the build's noise to stderr and leaves stdout to the server. If you would rather avoid a
script, build first and point the registration at the assembly:

```json
{
  "servers": {
    "auto-ynab-csv": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["${workspaceFolder}/AutoYnabCsv.Mcp/bin/Debug/net10.0/auto-ynab-csv-mcp.dll"]
    }
  }
}
```

That one runs whatever was built last — `auto-ynab-csv-mcp --version`, or the `version` line the
server reports, says how old it is.

### The tools

| tool | arguments | answers |
| --- | --- | --- |
| `ynab_accounts` | `snapshotFolder` | the accounts of a snapshot and the balance each holds |
| `ynab_exports` | `exportsFolder` | what a folder of exports contains: account, period, declared balance, rows |
| `ynab_reconcile` | `snapshotFolder`, `exportsFolder`, optional `accounts` / `ownIbans` / `expectedBalances` | the full report: balances, missing and extra rows, where the ledgers diverge |
| `ynab_plan` | `planPath`, optional `largest` | a repair plan as tables instead of JSON — add, move, remove |
| `ynab_journal` | `journalPath`, optional `newest` | what an apply did, counted and listed |

`ynab_reconcile` takes only the two folders when your settings file is in place; the three optional
arguments (`["Girokonto · 0002=<id>", …]`) override what the file says for one call.

### What it will not do

Nothing here writes, and nothing here calls the API: no tool creates, moves or deletes a transaction,
and none of them spends a request from the hourly allowance. Reading the plan and writing changes stay
in the console app, where the allowance and the `--apply` gate already live. The server only reads
files under the paths you hand it.

## Structure

- [Converters, Detectors, Exporters](./AutoYnabCsv/)
- [Console App](./AutoYnabCsv.ConsoleApp/)
- [Plan checking against the bank](./AutoYnabCsv.Ynab/)
- [MCP server](./AutoYnabCsv.Mcp/)
- [Unit and Approval Tests](./AutoYnabCsv.Tests/)
    - see [data/samples](./data/samples) for sample inputs
    - see `*.approved.txt` for approved converted files

