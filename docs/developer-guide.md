# Developer guide

How the code is laid out, how to build and test it, and — the part that costs the most time when it
is wrong — when the mail index throws itself away and reads the mailbox again.

## Projects

| Project | What it owns |
| --- | --- |
| `AutoYnabCsv` | The bank side: detectors, converters, the shared contracts, the CSV writer. Knows nothing about mail. |
| `AutoYnabCsv.MailIndex` | The mail side: Apple Mail walk, extraction rules, correlation, enrichment, the SQLite index, the report site. |
| `AutoYnabCsv.ConsoleApp` | The CLI (`convert`, `mail sync`, `mail scan`, `mail correlate`, `mail export`, `mail patterns`, `mail where`, `mail transfers`). |
| `AutoYnabCsv.DesktopApp` | The Avalonia window: the same use cases, driven by hand. |
| `AutoYnabCsv.Tests` | Everything above, tested through its public behaviour. |

The direction of dependency is one way: `ConsoleApp` and `DesktopApp` → `MailIndex` → `AutoYnabCsv`.
`MailIndex` never reaches into a UI, and `AutoYnabCsv` never reaches into mail.

## Build, test, ship

```shell
./scripts/build.sh            # dotnet build auto-ynab-csv.sln
./scripts/test.sh             # the whole suite (MSTest)
./scripts/quality-gate.sh     # format, build -warnaserror, tests + coverage, CRAP, duplication
./scripts/quality-gate.sh --strict-format   # what CI runs
./scripts/run.sh --help       # the CLI, built and run in one step
./scripts/mail-sync.sh        # a sync over ~/Library/Mail
```

PowerShell equivalents (`build.ps1`, `test.ps1`, `quality-gate.ps1`, `run.ps1`, `mail.ps1`) exist
for Windows and take the same arguments.

The gate fails on: a formatting difference (`csharpier`), a warning (the build runs with
`-warnaserror`), a failing test, less than the required line coverage, a method whose CRAP score is
above 30, or more than 3 % duplicated code. A green gate is the definition of "done" here, not a
nice-to-have.

### Mail access on macOS

Reading `~/Library/Mail` needs Full Disk Access for the terminal (or the app) that runs the sync.
Without it the walk fails with *Operation not permitted*; nothing else in the project needs it. A
sandboxed shell, including the one an editor may hand you, often has no such access — run the sync
from your own terminal when it reports the folder as unreadable.

## The index, and when it is thrown away

The index is one SQLite file — `~/Library/Application Support/auto-ynab-csv/mail-index.sqlite`
(`%APPDATA%` on Windows, `$XDG_DATA_HOME` elsewhere) — holding the facts of every message, plus an
FTS5 table over subject and body. It is a **cache**: deleting it and syncing again must give the
same answer, only slower.

### What makes a sync re-read everything

| Trigger | Where it is decided | Effect |
| --- | --- | --- |
| Rules fingerprint differs | `MailSyncService.Sync` → `store.Fingerprint != rules.Fingerprint` | Fresh file, whole mailbox walked |
| `mail sync --full` | `forceFull` | Same, whatever the fingerprint says |
| Previous rebuild did not finish | `MailIndexStore.RebuildUnfinished` | Whole mailbox walked (the holes are of unknown age) |
| Tokenizer changed | `MailIndexStore.MigrateTokenizer` on open | `mail_fts`, `messages` and `sync_state` dropped; the next sync re-reads |
| The file was deleted | — | First run: everything is read |

Everything else is incremental: a message is parsed only when its file is new, or its
`LastWriteTime` **and** length changed since the stored row.

### What the fingerprint covers

`ExtractionRuleSet.FingerprintOf` hashes, in order:

- every rule's `id`, `sender`, `subject`, `body`, `vendor`, `stop`, and each field's
  `name`, `where` and `pattern`;
- every `vendor` entry (domain → display name), sorted by domain;
- `MessageParser.TextVersion` — the shape of the text a message is reduced to.

So: change a pattern → rebuild. Rename a vendor → rebuild. Change **how text is read** (for example
`HtmlToText` learning to keep line breaks or image descriptions) → bump `TextVersion`, and the
rebuild follows. Forgetting that bump is the one silent bug this design can have: the index would
keep facts extracted from text the current reader would no longer produce, and nothing would look
wrong until a memo came out short.

**Not** in the fingerprint, and deliberately so — these cost no re-index:

- `transfers` (phrases that say money merely moved) and `opaquePayees` (payees worth a mailbox
  search) in `patterns.jsonc`;
- correlation thresholds (`AmountWindowDays = 5`, `SearchWindowDays = 60`);
- anything that only shapes the output: the memo order, `MaxMemoLength`, the payee's spelling, the
  YNAB CSV columns.

Those are read at the moment a row is written, so a change takes effect on the next conversion
without touching the index.

### How a rebuild is done

Never in place. `MailIndexStore.StartRebuild`:

1. deletes any leftover `<path>.rebuild` (a killed rebuild is never adopted);
2. creates `<path>.rebuild` with the schema, and — in one transaction, fingerprint first — writes
   the new fingerprint and `rebuild_unfinished = 1`;
3. closes the old connection, deletes the old `-wal` and `-shm` (a stale log beside a new database
   is how a file is recovered into nonsense), and `File.Move`s the fresh file over the old one.

SQLite keeps the pages a `DELETE` frees, so emptying the tables would leave an eighty-thousand
message file at its largest size forever; a new file is the only way back to a small one.

Because the fingerprint and the marker are committed before the first message is read, a run that
is interrupted at any point resumes into a *complete* walk rather than into a half-index whose
missing days are invisible. `MailIndexSync.Run` clears the marker at the end, after
`MarkSynced`.

Messages are written in batches of 250 inside one transaction: an interruption costs at most one
batch, and each commit stays short.

### Incremental runs

`MailSyncService.Sync`:

- `window = since ?? (lastSync − OverlapDays)` — a week of overlap, because a charge and its mail
  can sit days apart;
- `walk.Complete` holds only files inside that window, so files outside it are neither read nor
  missed;
- `pruneMissing` is on **only** when the window is `null` — a partial walk knows nothing about the
  messages it did not look at, and must not delete their rows.

### Schema and connections

`PRAGMA user_version = 3`; tables `messages`, `sync_state`, `mail_fts` (FTS5,
`unicode61 remove_diacritics 2 tokenchars '-_./'`, so `028-1234567-8901234` stays one token).
Bodies are capped at `MaxIndexedBodyChars = 20_000` characters — the body itself is never stored.

Connections are opened deliberately with `Pooling = false`, `journal_mode=WAL`,
`synchronous=NORMAL`, `busy_timeout=5000`. Pooling is off because a pooled-but-closed handle keeps
the file open behind our back: SQLite can then not fold the write-ahead log back in, and a file
being moved into place may not exist yet. Do not turn it on.

## Adding or changing a rule

Rules are data, not code: `AutoYnabCsv.MailIndex/Extraction/patterns.jsonc`, embedded in the
binary. A developer edits the shipped file; a user lays their own over it (see the README). Merge is
by rule `id`, and vendors merge by domain.

1. Find a real message. `./scripts/run.sh mail text --limit 20 --since 2026-10-01` prints what the
   rules will see — the *converted* text, not the raw HTML, which is the only thing regexes can be
   written against.
2. Write the narrowest pattern that only this shop's mail satisfies: anchor on a label
   (`Bestellnummer`, `Order #`, `Transaktionscode`) rather than on shape alone.
3. Use `items` for a list — every field named `items` keeps **all** matches, one per line, and is
   the only field that does. Everything else keeps its first match.
4. Add a case to `AutoYnabCsv.Tests/MailIndex/ShippedRulesTest.cs` with the real line, direction
   marks and all. If the fact is only visible in HTML, build the body with `FactsOfHtml`, which runs
   the real `HtmlToText.Convert` — a rule tested against hand-typed text passes while the product
   fails.
5. Run `./scripts/test.sh`, then the gate. The next `mail sync` rebuilds by itself; no manual
   deletion is needed.

### What the text looks like

`MessageParser` prefers the plain-text part, and converts HTML only when there is none. `HtmlToText`
turns block-level tags and `<br>` into newlines, keeps the `alt` description of an image (a shop
writes the whole product title there when the link beside it is cut short with `...`), drops scripts
and styles, decodes entities, and collapses whitespace per line. So `(?m)^…$` means a line a human
would see — but only because `Convert` keeps those breaks. Anything that changes this shape needs
`MessageParser.TextVersion` bumped, as above.

## Where the pieces live

| Concern | File |
| --- | --- |
| Walk the Mail store, name what cannot be read | `MailIndex/Mail/EmlxWalker.cs`, `Index/MailSyncService.cs` |
| Reduce a message to facts | `Mail/MessageParser.cs`, `Mail/HtmlToText.cs`, `Extraction/*` |
| Store, search, rebuild | `Index/MailIndexStore.cs`, `Index/MailIndexSync.cs` |
| Decide which mail explains a row | `Correlation/*` |
| Payee and memo for YNAB | `Enrichment/EntryEnricher.cs` |
| The HTML report and the YNAB CSV | `Web/SiteExporter.cs`, `Web/SiteTemplate.cs`, `Web/EnrichedCsvWriter.cs` |
