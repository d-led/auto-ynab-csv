using System.Globalization;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>
/// The generated page. It has no dependencies and needs no network: the documents sit in a
/// <c>application/json</c> block and the matcher runs in the page itself.
/// </summary>
public static class SiteTemplate
{
    public static string Html(string json, string generated, string indexedThrough, string audit) =>
        Page.Replace("__GENERATED__", generated)
            .Replace("__INDEXED_THROUGH__", indexedThrough)
            .Replace("__AUDIT__", audit)
            .Replace("__DATA__", json);

    /// <summary>The audit block, rendered on the server so it reads even without JavaScript.</summary>
    public static string AuditHtml(SiteAudit audit, int evidenceMails)
    {
        var rows = string.Join(
            string.Empty,
            audit.Inputs.Select(input =>
                $"<tr><td>{E(input.File)}</td><td>{E(input.Detected)}</td>"
                    + $"<td class=\"num\">{input.Entries}</td></tr>"
            )
        );

        var skipped = audit.Skipped.Count == 0
            ? string.Empty
            : $"<p>Skipped: {string.Join(", ", audit.Skipped.Select(skip => $"{E(Path.GetFileName(skip.File))} ({E(skip.Reason)})"))}</p>";

        var version = audit.Mail.MailboxVersion is { } v ? $"V{v}" : "unknown";
        var hint = audit.Mail.Messages > 0
            ? string.Empty
            : """
                <p class="hint">No mail is indexed, so payees and memos stay as the bank wrote them.
                Mail is optional: grant Full Disk Access to this app in System Settings → Privacy &amp;
                Security → Full Disk Access, then run <code>auto-ynab-csv mail sync</code> and convert
                again.</p>
                """;

        return $"""
            {hint}
            <details class="audit">
              <summary>Audit: {E(audit.Summary)}</summary>
              <p>
                index: {E(audit.Mail.IndexPath)}<br>
                mailbox: {E(audit.Mail.MailboxRoot)} ({version})<br>
                messages: {audit.Mail.Messages} indexed, {evidenceMails} kept as evidence<br>
                covers {E(Range(audit.Mail))}, last sync {E(Day(audit.Mail.LastSyncedAt))}<br>
                rules: {E(audit.Mail.RulesFingerprint)}<br>
                generated {E(audit.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} by {E(audit.ToolVersion)}
              </p>
              <table>
                <tr><th>file</th><th>detected</th><th>entries</th></tr>
                {rows}
              </table>
              {skipped}
            </details>
            """;
    }

    private static string E(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Range(MailAudit mail) =>
        $"{Day(mail.IndexedFrom)} … {Day(mail.IndexedThrough)}";

    private static string Day(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";

    private const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Mail and transactions</title>
<style>
  :root { color-scheme: light dark; --bg:#fbfbfa; --fg:#1f2328; --muted:#6b7280; --line:#e5e7eb; --card:#fff; --accent:#2563eb; --bank:#7c3aed; }
  @media (prefers-color-scheme: dark) { :root { --bg:#111214; --fg:#e6e6e6; --muted:#9aa0a6; --line:#26282c; --card:#17191c; --accent:#7aa7ff; --bank:#b794f6; } }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--bg); color:var(--fg); font:15px/1.45 -apple-system,BlinkMacSystemFont,"Segoe UI",system-ui,sans-serif; }
  header { padding:20px 22px 8px; }
  h1 { font-size:18px; margin:0 0 4px; font-weight:600; }
  .muted { color:var(--muted); font-size:13px; }
  .search { padding:8px 22px 14px; position:sticky; top:0; background:var(--bg); border-bottom:1px solid var(--line); }
  input { width:100%; padding:10px 12px; border:1px solid var(--line); border-radius:10px; background:var(--card); color:var(--fg); font-size:15px; }
  input:focus { outline:2px solid var(--accent); outline-offset:1px; }
  ol { list-style:none; margin:0; padding:10px 22px 40px; }
  li { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:10px 12px; margin:8px 0; }
  .row { display:flex; gap:10px; align-items:baseline; flex-wrap:wrap; }
  .kind { font-size:11px; text-transform:uppercase; letter-spacing:.04em; color:var(--muted); }
  .kind.bank { color:var(--bank); }
  .date { font-variant-numeric:tabular-nums; color:var(--muted); font-size:13px; }
  .title { font-weight:600; }
  .meta { color:var(--muted); font-size:13px; margin-top:2px; }
  .body { margin:6px 0 0; color:var(--muted); font-size:13px; white-space:pre-wrap; overflow-wrap:anywhere; }
  .amount { margin-left:auto; font-variant-numeric:tabular-nums; }
  .maillink { color:var(--accent); text-decoration:none; }
  .maillink:hover { text-decoration:underline; }
  .f { display:inline-block; margin-right:12px; }
  .f b { color:var(--muted); font-weight:500; }
  .f b::after { content:': '; }
  .evidence { list-style:none; margin:8px 0 0 16px; padding:0 0 0 10px; border-left:2px solid var(--line); }
  .evidence-row { background:transparent; border:none; padding:4px 0; margin:0; }
  .evidence-row .title { font-weight:500; }
  mark { background:transparent; color:var(--accent); font-weight:600; }
  .audit { margin-top:8px; font-size:12px; color:var(--muted); }
  .audit summary { cursor:pointer; }
  .audit table { border-collapse:collapse; margin-top:6px; font-size:12px; width:100%; }
  .audit th, .audit td { text-align:left; padding:3px 8px; border-bottom:1px solid var(--line); }
  .audit th { font-weight:500; }
  .audit .num { text-align:right; font-variant-numeric:tabular-nums; }
  .audit p { margin:6px 0; line-height:1.6; }
  .flows { padding:10px 22px 0; }
  .flows summary { cursor:pointer; font-size:15px; font-weight:600; margin:6px 0 4px; }
  #flow { display:block; margin:10px 0 14px; background:transparent; }
  .hint { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:8px 12px; font-size:13px; color:var(--muted); }
  .hint code { font-size:12px; }
</style>
</head>
<body>
<header>
  <h1>Mail and transactions</h1>
  <div class="muted">Generated __GENERATED__. Mail index covers __INDEXED_THROUGH__. Type words: all of them must match; prefixes, substrings and one-typo words count.</div>
  __AUDIT__
</header>
<div class="search">
  <input id="q" type="search" placeholder="e.g. tho 25 pic" autofocus autocomplete="off" spellcheck="false">
</div>
<details id="flows" class="flows">
  <summary>Money between your accounts <span id="flow-count" class="muted"></span></summary>
  <div id="flow-legend" class="muted"></div>
  <pre id="flow" class="mermaid"></pre>
</details>
<ol id="results"></ol>
<script id="data" type="application/json">__DATA__</script>
<script>
const DATA = JSON.parse(document.getElementById('data').textContent);
const docs = DATA.documents.map(prepare);
const results = document.getElementById('results');
const input = document.getElementById('q');

function norm(value) {
  return (value || '').toString().toLowerCase()
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .replace(/ß/g, 'ss');
}

function tokens(value) {
  return norm(value).split(/[^a-z0-9]+/).filter(token => token.length > 0);
}

function isSubsequence(needle, haystack) {
  let found = 0;
  for (const character of haystack) {
    if (character === needle[found]) found++;
    if (found === needle.length) return true;
  }
  return false;
}

function editDistance(a, b) {
  const row = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) {
    let previous = row[0];
    row[0] = i;
    for (let j = 1; j <= b.length; j++) {
      const current = row[j];
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, previous + (a[i - 1] === b[j - 1] ? 0 : 1));
      previous = current;
    }
  }
  return row[b.length];
}

// 100 exact, 80 prefix, 60 substring, 40 in-order subsequence, 30 one typo.
function scoreToken(token, candidates) {
  let best = 0;
  for (const candidate of candidates) {
    if (candidate === token) return 100;
    if (candidate.startsWith(token)) best = Math.max(best, 80);
    else if (candidate.includes(token)) best = Math.max(best, 60);
    else if (isSubsequence(token, candidate)) best = Math.max(best, 40);
    else if (token.length >= 4 && Math.abs(candidate.length - token.length) <= 1
             && editDistance(token, candidate) <= 1) best = Math.max(best, 30);
  }
  return best;
}

function prepare(document) {
  const vendor = `${document.vendor || ''} ${document.merchant || ''}`;
  return {
    document,
    date: document.date || '',
    title: tokens(document.title),
    vendor: tokens(vendor),
    orders: tokens(document.orders),
    amounts: tokens(document.amounts),
    text: tokens(document.text)
  };
}

// Mail that proves a transaction belongs to it: one result per transaction, evidence underneath.
const bankOfMail = new Map();
for (const entry of docs) {
  if (entry.document.kind !== 'bank') continue;
  for (const id of entry.document.relatedIds || []) bankOfMail.set(id, entry.document.id);
}

const groups = new Map();
for (const entry of docs) {
  const d = entry.document;
  const key = d.kind === 'bank' ? d.id : (bankOfMail.get(d.id) || d.id);

  if (!groups.has(key)) groups.set(key, { key, primary: null, evidence: [] });

  const group = groups.get(key);
  if (d.kind === 'bank') group.primary = entry;
  else group.evidence.push(entry);
}

for (const group of groups.values()) {
  if (!group.primary && group.evidence.length > 0) group.primary = group.evidence.shift();
}

function search(query) {
  const needles = tokens(query);
  const all = [...groups.values()];

  if (needles.length === 0) {
    return all
      .map(group => ({ group, score: 0 }))
      .sort((a, b) => dateOf(b.group).localeCompare(dateOf(a.group)))
      .slice(0, 100);
  }

  const hits = [];
  for (const group of all) {
    const score = scoreGroup(group, needles);
    if (score > 0) hits.push({ group, score });
  }

  return hits
    .sort((a, b) => b.score - a.score || dateOf(b.group).localeCompare(dateOf(a.group)))
    .slice(0, 100);
}

// A transaction and the mails that prove it are one thing: a query sees them together.
function scoreGroup(group, needles) {
  let total = 0;
  for (const needle of needles) {
    let best = 0;
    for (const entry of membersOf(group)) best = Math.max(best, scoreEntry(entry, needle));
    if (best === 0) return 0;
    total += best;
  }
  return total;
}

function scoreEntry(entry, needle) {
  return Math.max(
    scoreToken(needle, entry.title) * 3,
    scoreToken(needle, entry.vendor) * 3,
    scoreToken(needle, entry.orders) * 2,
    scoreToken(needle, entry.amounts) * 2,
    scoreToken(needle, entry.text)
  );
}

function membersOf(group) {
  return [group.primary, ...group.evidence].filter(Boolean);
}

function dateOf(group) {
  const first = group.primary || group.evidence[0];
  return first ? first.date : '';
}

function escapeHtml(value) {
  return (value || '').toString()
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// Every value says what it is, so nothing in the list is anonymous.
const FIELDS = {
  bank: [
    ['merchant', d => d.merchant],
    ['amount', d => d.amounts],
    ['order', d => d.orders],
    ['transfer', d => d.fields],
    ['file', d => d.source]
  ],
  mail: [
    ['vendor', d => d.vendor],
    ['order', d => d.orders],
    ['amount', d => d.amounts],
    ['facts', d => d.fields],
    ['from', d => d.source]
  ]
};

function labelled(d) {
  const fields = FIELDS[d.kind] || FIELDS.mail;
  return fields
    .map(([label, value]) => [label, (value(d) || '').toString()])
    .filter(([, value]) => value.length > 0)
    .map(([label, value]) => `<span class="f"><b>${label}</b>${escapeHtml(value)}</span>`)
    .join('');
}

function mailLink(d) {
  return d.mailUrl
    ? `<a class="maillink" href="${escapeHtml(d.mailUrl)}">open in Mail</a>`
    : '';
}

function render(hits) {
  if (hits.length === 0) {
    results.innerHTML = '<li class="muted">Nothing matches.</li>';
    return;
  }

  results.innerHTML = hits.map(({ group }) => {
    const d = (group.primary || group.evidence[0]).document;
    const text = (d.text || '').slice(0, 300);
    const evidence = group.evidence.length > 0
      ? `<ul class="evidence">${group.evidence.map(entry => evidenceRow(entry.document)).join('')}</ul>`
      : '';

    return `<li class="group">
      <div class="row">
        <span class="kind ${escapeHtml(d.kind)}">${escapeHtml(d.kind)}</span>
        <span class="date">${escapeHtml(d.date)}</span>
        <span class="title">${escapeHtml(d.title)}</span>
        <span class="amount">${escapeHtml(d.amounts)}</span>
      </div>
      <div class="meta">${labelled(d)}${mailLink(d) ? ' · ' + mailLink(d) : ''}</div>
      ${text ? `<p class="body">${escapeHtml(text)}</p>` : ''}
      ${evidence}
    </li>`;
  }).join('');
}

function evidenceRow(d) {
  return `<li class="evidence-row">
    <div class="row">
      <span class="kind ${escapeHtml(d.kind)}">${escapeHtml(d.kind)}</span>
      <span class="date">${escapeHtml(d.date)}</span>
      <span class="title">${escapeHtml(d.title)}</span>
    </div>
    <div class="meta">${labelled(d)}${mailLink(d) ? ' · ' + mailLink(d) : ''}</div>
  </li>`;
}

function drawFlows() {
  const flows = DATA.flows || [];
  const section = document.getElementById('flows');
  const legend = document.getElementById('flow-legend');
  const count = document.getElementById('flow-count');
  const diagram = document.getElementById('flow');

  if (flows.length === 0) {
    section.remove();
    return;
  }

  legend.textContent = 'Each arrow is one direction: A to B and B to A are separate and never netted.';
  count.textContent = `· ${flows.length} directions`;

  const money = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const ids = new Map();
  const node = name => {
    if (!ids.has(name)) ids.set(name, `account${ids.size}`);
    return `${ids.get(name)}["${name.replace(/"/g, '&quot;')}"]`;
  };

  diagram.textContent = ['graph LR']
    .concat(flows.map(flow => `  ${node(flow.from)} -->|"${money.format(flow.total)} · ${flow.count}x"| ${node(flow.to)}`))
    .join('\n');

  // Mermaid is drawn on first open: inside a closed <details> it would measure zero.
  let drawn = false;
  section.addEventListener('toggle', () => {
    if (!section.open || drawn) return;
    drawn = true;

    import('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs')
      .then(({ default: mermaid }) => {
        mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', flowchart: { curve: 'basis' } });
        return mermaid.run({ nodes: [diagram] });
      })
      .catch(() => {
        diagram.textContent = 'Mermaid comes from a CDN and needs a network connection.';
      });
  });
}

input.addEventListener('input', () => render(search(input.value)));
render(search(''));
drawFlows();
</script>
</body>
</html>
""";
}
