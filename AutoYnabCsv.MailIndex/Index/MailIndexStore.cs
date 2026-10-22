using System.Globalization;
using System.Text.Json;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;
using Microsoft.Data.Sqlite;

namespace AutoYnabCsv.MailIndex.Index;

public sealed record FileState(long ModifiedTicks, long Length);

public sealed record SearchHit(string Path, string Subject, string Sender, DateTimeOffset ReceivedAt);

/// <summary>
/// The local index: parsed mail facts in one SQLite file, with FTS5 over subject and body.
/// It is a cache — deleting it and re-syncing must reproduce the same result.
/// </summary>
public sealed class MailIndexStore : IDisposable
{
    private const int MaxIndexedBodyChars = 20_000;
    private const string FingerprintKey = "rules_fingerprint";
    private const string LastSyncKey = "last_sync_at";
    private const string RebuildingKey = "rebuild_unfinished";

    private SqliteConnection _connection;
    private readonly string _path;
    private long _diskBytes;

    private MailIndexStore(SqliteConnection connection, string path)
    {
        _connection = connection;
        _path = path;
        RefreshSize();
    }

    /// <summary>
    /// How much the index takes on disk right now, database plus write-ahead log. It is a
    /// <c>stat</c> of two files, cached per batch: asking SQLite would need the lock the writer
    /// already holds, and a progress line has no business competing for it.
    /// </summary>
    public long DiskBytes => _diskBytes;

    public static string DefaultPath() =>
        Path.Combine(AppDirectories.DataDirectory(), "mail-index.sqlite");

    public static MailIndexStore Open(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A rebuild that was killed left its half-written file behind; it is never adopted.
        Forget(path + FreshSuffix);

        var connection = Connect(path);
        var migrated = CreateSchema(connection);
        AddMessageIdColumn(connection);

        return new MailIndexStore(connection, path) { Migrated = migrated };
    }

    /// <summary>
    /// One connection per process, opened and closed deliberately. Pooling would keep the file
    /// open behind our back: SQLite then cannot fold the write-ahead log back in, so the log grows
    /// for as long as the pool lives and a file that is moved into place may not exist yet.
    /// </summary>
    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");
        Execute(connection, "PRAGMA busy_timeout=5000;");

        return connection;
    }

    /// <summary>
    /// True when opening this file threw away an index built with an older tokenizer. The next
    /// sync then has to read everything again, which is worth saying out loud once.
    /// </summary>
    public bool Migrated { get; private init; }

    public int Count => ScalarInt("SELECT COUNT(*) FROM messages;");

    public string? Fingerprint
    {
        get => GetState(FingerprintKey);
        set => SetState(FingerprintKey, value ?? string.Empty);
    }

    /// <summary>When the index was last brought up to date; null when it never ran.</summary>
    public DateTimeOffset? LastSyncedAt =>
        GetState(LastSyncKey) is { } text
        && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
        && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    /// <summary>The newest message receipt date in the index; null when it holds nothing.</summary>
    public DateTimeOffset? IndexedThrough
    {
        get
        {
            var newest = ScalarLong("SELECT MAX(received_at) FROM messages;");
            return newest > 0 ? DateTimeOffset.FromUnixTimeSeconds(newest) : null;
        }
    }

    /// <summary>The oldest message receipt date in the index; null when it holds nothing.</summary>
    public DateTimeOffset? IndexedFrom
    {
        get
        {
            var oldest = ScalarLong("SELECT MIN(received_at) FROM messages;");
            return oldest > 0 ? DateTimeOffset.FromUnixTimeSeconds(oldest) : null;
        }
    }

    public void MarkSynced(DateTimeOffset at) =>
        SetState(LastSyncKey, at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// True while a rebuild has cleared the index but not filled it again. Nothing in it can be
    /// trusted to be complete, so the next run has to walk the whole mailbox rather than the days
    /// since the last sync — those days say nothing about what is missing.
    /// </summary>
    public bool RebuildUnfinished
    {
        get => GetState(RebuildingKey) == "1";
        set => SetState(RebuildingKey, value ? "1" : "0");
    }

    public IReadOnlyDictionary<string, FileState> KnownFiles()
    {
        var known = new Dictionary<string, FileState>(StringComparer.Ordinal);

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT path, modified_ticks, length FROM messages;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            known[reader.GetString(0)] = new FileState(reader.GetInt64(1), reader.GetInt64(2));
        }

        return known;
    }

    /// <summary>
    /// Stores what a message yielded. <paramref name="searchBody"/> is false for a body that was
    /// fetched on demand: the facts are kept, the text itself is not.
    /// </summary>
    public void Upsert(EmlxFile file, MailFacts facts, bool searchBody = true)
    {
        using var transaction = _connection.BeginTransaction(deferred: false);
        Write(transaction, file, facts, searchBody);
        transaction.Commit();
    }

    /// <summary>
    /// Many messages in one transaction: a first pass over a mailbox is tens of thousands of
    /// writes, and committing each of them separately is what makes it crawl. A batch is also
    /// the most a Ctrl-C can throw away.
    /// </summary>
    public void UpsertBatch(IReadOnlyList<(EmlxFile File, MailFacts Facts)> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction(deferred: false);

        foreach (var (file, facts) in messages)
        {
            Write(transaction, file, facts, searchBody: true);
        }

        transaction.Commit();
        RefreshSize();
    }

    private void RefreshSize() =>
        _diskBytes = Length(_path) + Length(_path + "-wal");

    /// <summary>A file that is not there yet, or just vanished, is simply no bytes.</summary>
    private static long Length(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void Write(
        SqliteTransaction transaction,
        EmlxFile file,
        MailFacts facts,
        bool searchBody
    )
    {
        Execute(transaction, "DELETE FROM mail_fts WHERE path = $path;", ("$path", file.Path));

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO messages(
                    path, received_at, sender, sender_name, subject, vendor,
                    order_numbers, amounts, fields, message_id, modified_ticks, length, indexed_at
                )
                VALUES(
                    $path, $received, $sender, $senderName, $subject, $vendor,
                    $orders, $amounts, $fields, $messageId, $modified, $length, $indexed
                )
                ON CONFLICT(path) DO UPDATE SET
                    received_at = excluded.received_at,
                    sender = excluded.sender,
                    sender_name = excluded.sender_name,
                    subject = excluded.subject,
                    vendor = excluded.vendor,
                    order_numbers = excluded.order_numbers,
                    amounts = excluded.amounts,
                    fields = excluded.fields,
                    message_id = excluded.message_id,
                    modified_ticks = excluded.modified_ticks,
                    length = excluded.length,
                    indexed_at = excluded.indexed_at;
                """;

            command.Parameters.AddWithValue("$path", file.Path);
            command.Parameters.AddWithValue("$received", facts.Mail.ReceivedAt.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$sender", facts.Mail.SenderAddress);
            command.Parameters.AddWithValue("$senderName", facts.Mail.SenderName);
            command.Parameters.AddWithValue("$subject", facts.Mail.Subject);
            command.Parameters.AddWithValue("$vendor", facts.Vendor);
            command.Parameters.AddWithValue("$orders", string.Join('\n', facts.OrderNumbers));
            command.Parameters.AddWithValue(
                "$amounts",
                string.Join('\n', facts.Amounts.Select(FormatAmount))
            );
            command.Parameters.AddWithValue("$fields", JsonSerializer.Serialize(facts.Fields));
            command.Parameters.AddWithValue("$messageId", facts.Mail.MessageId);
            command.Parameters.AddWithValue("$modified", file.LastWriteUtc.UtcTicks);
            command.Parameters.AddWithValue("$length", file.Length);
            command.Parameters.AddWithValue("$indexed", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.ExecuteNonQuery();
        }

        Execute(
            transaction,
            "INSERT INTO mail_fts(path, subject, body) VALUES ($path, $subject, $body);",
            ("$path", file.Path),
            ("$subject", facts.Mail.Subject),
            ("$body", searchBody ? BodyForIndex(facts.Mail.Text) : string.Empty)
        );
    }

    public int RemoveMissing(IReadOnlySet<string> seen)
    {
        var missing = KnownFiles().Keys.Where(path => !seen.Contains(path)).ToArray();

        foreach (var path in missing)
        {
            Delete(path);
        }

        return missing.Length;
    }

    /// <summary>
    /// Forgets everything, in a file of its own. Emptying the tables would be cheaper to write and
    /// dearer to live with: SQLite keeps the pages a delete frees, so an index that was once built
    /// for 80,000 messages stays that big however often it is cleared. The new file is written
    /// beside the old one and carries the fingerprint and the marker before it is put in place, so
    /// a run interrupted at any point still resumes into a complete walk. The fingerprint goes
    /// first, so work already done is not mistaken for "the rules changed" and cleared again.
    /// </summary>
    public void StartRebuild(string rulesFingerprint)
    {
        var fresh = _path + FreshSuffix;

        Forget(fresh);
        using (var file = Connect(fresh))
        {
            CreateSchema(file);

            using var transaction = file.BeginTransaction(deferred: false);
            SetState(transaction, FingerprintKey, rulesFingerprint);
            SetState(transaction, RebuildingKey, "1");
            transaction.Commit();
        }

        Adopt(fresh);
    }

    private const string FreshSuffix = ".rebuild";

    /// <summary>
    /// Puts a freshly written file where the index belongs. The write-ahead log of the old file
    /// goes with it: a stale one beside a new database is how a file gets recovered into nonsense.
    /// </summary>
    private void Adopt(string fresh)
    {
        _connection.Dispose();
        Forget(_path + "-wal");
        Forget(_path + "-shm");
        File.Move(fresh, _path, overwrite: true);

        _connection = Connect(_path);
        CreateSchema(_connection);
        AddMessageIdColumn(_connection);
        RefreshSize();
    }

    private static void Forget(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The stored facts, newest first, optionally only those inside a span — a conversion asks
    /// for the days its exports cover, not for the mailbox.
    /// </summary>
    public IReadOnlyList<MailFacts> LoadFacts(
        int? limit = null,
        DateTimeOffset? since = null,
        DateTimeOffset? until = null
    )
    {
        var facts = new List<MailFacts>();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT path, received_at, sender, sender_name, subject, vendor,
                   order_numbers, amounts, fields, message_id
            FROM messages
            WHERE ($since IS NULL OR received_at >= $since)
              AND ($until IS NULL OR received_at < $until)
            ORDER BY received_at DESC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$limit", limit ?? -1);
        command.Parameters.AddWithValue(
            "$since",
            since is { } from ? from.ToUnixTimeSeconds() : DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$until",
            until is { } to ? to.ToUnixTimeSeconds() : DBNull.Value
        );

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            facts.Add(
                new MailFacts(
                    new MailRecord(
                        reader.GetString(0),
                        FromUnixSeconds(reader.GetInt64(1)),
                        reader.GetString(4),
                        reader.GetString(2),
                        reader.GetString(3),
                        string.Empty,
                        reader.GetString(9)
                    ),
                    reader.GetString(5),
                    Split(reader.GetString(6)),
                    Split(reader.GetString(7)).Select(ParseAmount).OfType<decimal>().ToArray(),
                    ParseFields(reader.GetString(8))
                )
            );
        }

        return facts;
    }

    /// <summary>
    /// A literal-word search: every fragment is quoted, so what the user typed cannot arrive as an
    /// FTS5 operator. <c>302-1234567-8901234</c> finds the order number, and a stray quote is just
    /// a stray quote instead of a syntax error.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, int limit = 20)
    {
        var hits = new List<SearchHit>();
        var phrase = EscapeForMatch(query);

        if (phrase.Length == 0)
        {
            return hits;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT m.path, m.subject, m.sender, m.received_at
            FROM mail_fts
            JOIN messages m ON m.path = mail_fts.path
            WHERE mail_fts MATCH $query
            ORDER BY rank
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", phrase);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            hits.Add(
                new SearchHit(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    FromUnixSeconds(reader.GetInt64(3))
                )
            );
        }

        return hits;
    }

    public void Dispose() => _connection.Dispose();

    private void Delete(string path)
    {
        using var transaction = _connection.BeginTransaction(deferred: false);
        Execute(transaction, "DELETE FROM messages WHERE path = $path;", ("$path", path));
        Execute(transaction, "DELETE FROM mail_fts WHERE path = $path;", ("$path", path));
        transaction.Commit();
    }

    private string? GetState(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM sync_state WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private void SetState(string key, string value)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync_state(key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void SetState(SqliteTransaction transaction, string key, string value) =>
        Execute(
            transaction,
            """
            INSERT INTO sync_state(key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            ("$key", key),
            ("$value", value)
        );

    /// <summary>
    /// The tokenizer keeps <c>-_.</c> inside a word, so an order number or a transaction code is
    /// one token instead of fragments: <c>028-1234567-8901234</c> has to be findable as written.
    /// Diacritics are folded, because <c>Händler</c> and <c>handler</c> are the same word to us.
    /// </summary>
    private const string Tokenizer = "unicode61 remove_diacritics 2 tokenchars '-_./'";

    private static bool CreateSchema(SqliteConnection connection)
    {
        Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS messages (
                path TEXT PRIMARY KEY,
                received_at INTEGER NOT NULL,
                sender TEXT NOT NULL,
                sender_name TEXT NOT NULL,
                subject TEXT NOT NULL,
                vendor TEXT NOT NULL,
                order_numbers TEXT NOT NULL,
                amounts TEXT NOT NULL,
                fields TEXT NOT NULL DEFAULT '{}',
                message_id TEXT NOT NULL DEFAULT '',
                modified_ticks INTEGER NOT NULL,
                length INTEGER NOT NULL,
                indexed_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS sync_state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """
        );
        Execute(
            connection,
            $"CREATE VIRTUAL TABLE IF NOT EXISTS mail_fts USING fts5(path UNINDEXED, subject, body, tokenize = \"{Tokenizer}\");"
        );
        var migrated = MigrateTokenizer(connection);
        Execute(connection, "PRAGMA user_version = 3;");

        return migrated;
    }

    /// <summary>
    /// An index built by an older tokenizer is rebuilt rather than queried wrongly: the body text
    /// is not kept outside FTS, so dropping the table means forgetting the facts that went with it
    /// and letting the next sync read the messages again.
    /// </summary>
    private static bool MigrateTokenizer(SqliteConnection connection)
    {
        if (FtsTokenizesLike(connection, Tokenizer))
        {
            return false;
        }

        Execute(
            connection,
            """
            DROP TABLE IF EXISTS mail_fts;
            DELETE FROM messages;
            DELETE FROM sync_state;
            """
        );
        Execute(
            connection,
            $"CREATE VIRTUAL TABLE mail_fts USING fts5(path UNINDEXED, subject, body, tokenize = \"{Tokenizer}\");"
        );

        return true;
    }

    private static bool FtsTokenizesLike(SqliteConnection connection, string tokenizer)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'mail_fts';";

        return command.ExecuteScalar() is string sql && sql.Contains(tokenizer, StringComparison.Ordinal);
    }

    /// <summary>An index written before the Message-ID was kept gains the column, not a rebuild.</summary>
    private static void AddMessageIdColumn(SqliteConnection connection)
    {
        if (HasColumn(connection, "messages", "message_id"))
        {
            return;
        }

        Execute(
            connection,
            "ALTER TABLE messages ADD COLUMN message_id TEXT NOT NULL DEFAULT '';"
        );
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private int ScalarInt(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private long ScalarLong(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static string BodyForIndex(string text) =>
        text.Length <= MaxIndexedBodyChars ? text : text[..MaxIndexedBodyChars];

    private static string FormatAmount(decimal amount) =>
        amount.ToString(CultureInfo.InvariantCulture);

    /// <summary>Quotes every word so FTS5 reads it as text, never as syntax.</summary>
    private static string EscapeForMatch(string query) =>
        string.Join(
            ' ',
            query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => $"\"{word.Replace("\"", "\"\"")}\"")
        );

    private static decimal ParseAmount(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;

    private static IReadOnlyList<string> Split(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyDictionary<string, string> ParseFields(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? new Dictionary<string, string>();

    private static DateTimeOffset FromUnixSeconds(long seconds) =>
        seconds <= 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(seconds);
}
