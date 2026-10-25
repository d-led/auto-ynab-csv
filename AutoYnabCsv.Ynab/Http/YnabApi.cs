using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Http;

/// <summary>
/// The REST surface this tool uses, one method per endpoint. Milliunits stay as the API sends them;
/// the conversion to a printed amount happens where a balance is compared, never here.
/// </summary>
public sealed class YnabApi : IYnabGateway
{
    public const string BaseUrl = "https://api.ynab.com/v1/";

    /// <summary>
    /// A wait this short is cheaper than a failed command; an hour of sitting still is not, and is
    /// refused with the time instead.
    /// </summary>
    private static readonly TimeSpan WaitWorthTaking = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly RequestBudget _budget;
    private readonly Action<string>? _say;

    public YnabApi(HttpClient http, string accessToken, RequestBudget? budget = null, Action<string>? say = null)
    {
        _http = http;
        _budget = budget ?? new RequestBudget();
        _say = say;
        _http.BaseAddress ??= new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IReadOnlyList<YnabPlanRef>> PlansAsync(CancellationToken cancellationToken = default)
    {
        var data = await SendAsync<PlanListData>("plans", HttpMethod.Get, null, cancellationToken);
        return [.. data.Plans.Select(plan => new YnabPlanRef(plan.Id, plan.Name))];
    }

    public async Task<IReadOnlyList<YnabAccount>> AccountsAsync(string planId, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync<AccountsData>($"plans/{planId}/accounts", HttpMethod.Get, null, cancellationToken);
        return
        [
            .. data.Accounts.Select(account => new YnabAccount(
                Id: account.Id,
                Name: account.Name,
                Type: account.Type,
                OnBudget: account.OnBudget,
                Closed: account.Closed,
                Balance: new Milliunits(account.Balance).ToMoney(),
                Cleared: new Milliunits(account.ClearedBalance).ToMoney(),
                Uncleared: new Milliunits(account.UnclearedBalance).ToMoney()
            ))
        ];
    }

    public async Task<IReadOnlyList<YnabCategory>> CategoriesAsync(string planId, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync<CategoriesData>($"plans/{planId}/categories", HttpMethod.Get, null, cancellationToken);
        return
        [
            .. data.CategoryGroups.SelectMany(group => group.Categories.Select(category => new YnabCategory(
                Id: category.Id,
                Name: category.Name,
                GroupName: group.Name,
                Internal: category.Internal
            )))
        ];
    }

    public async Task<IReadOnlyList<YnabPayee>> PayeesAsync(string planId, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync<PayeesData>($"plans/{planId}/payees", HttpMethod.Get, null, cancellationToken);
        return [.. data.Payees.Select(payee => new YnabPayee(payee.Id, payee.Name, payee.TransferAccountId))];
    }

    public async Task<IReadOnlyList<YnabTransaction>> TransactionsAsync(
        string planId,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default
    )
    {
        var query = $"since_date={since:yyyy-MM-dd}&until_date={until:yyyy-MM-dd}";
        var data = await SendAsync<TransactionsData>($"plans/{planId}/transactions?{query}", HttpMethod.Get, null, cancellationToken);

        return
        [
            .. data.Transactions.Select(transaction => new YnabTransaction(
                Id: transaction.Id,
                Date: transaction.Date,
                Amount: new Milliunits(transaction.Amount).ToMoney(),
                AccountId: transaction.AccountId,
                AccountName: transaction.AccountName,
                PayeeId: transaction.PayeeId,
                PayeeName: transaction.PayeeName,
                CategoryId: transaction.CategoryId,
                CategoryName: transaction.CategoryName,
                Memo: transaction.Memo,
                Cleared: IsCleared(transaction.Cleared),
                Approved: transaction.Approved,
                TransferAccountId: transaction.TransferAccountId,
                ImportId: transaction.ImportId
            ))
        ];
    }

    public async Task<string> CreateAsync(string planId, PlannedCreate create, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            transaction = new
            {
                account_id = create.AccountId,
                date = create.Date.ToString("yyyy-MM-dd"),
                amount = create.Amount.ToMilliunits().Value,
                // A transfer names the far side and lets YNAB write the other leg and its payee;
                // sending a payee name as well would fight that.
                payee_name = create.TransferAccountId is null ? create.PayeeName : null,
                transfer_account_id = create.TransferAccountId,
                memo = Capped(create.Memo),
                category_id = create.CategoryId,
                cleared = "cleared",
                approved = false,
                import_id = create.ImportId,
            },
        };

        var data = await SendAsync<TransactionData>($"plans/{planId}/transactions", HttpMethod.Post, body, cancellationToken);
        return data.Transaction.Id;
    }

    public async Task<YnabTransaction> DeleteAsync(
        string planId,
        string transactionId,
        CancellationToken cancellationToken = default
    )
    {
        var data = await SendAsync<TransactionData>(
            $"plans/{planId}/transactions/{transactionId}",
            HttpMethod.Delete,
            null,
            cancellationToken
        );

        var gone = data.Transaction;
        return new YnabTransaction(
            gone.Id,
            gone.Date,
            new Milliunits(gone.Amount).ToMoney(),
            gone.AccountId,
            gone.AccountName,
            gone.PayeeId,
            gone.PayeeName,
            gone.CategoryId,
            gone.CategoryName,
            gone.Memo,
            IsCleared(gone.Cleared),
            gone.Approved,
            gone.TransferAccountId,
            gone.ImportId
        );
    }

    public async Task CategorizeAsync(
        string planId,
        string transactionId,
        string categoryId,
        CancellationToken cancellationToken = default
    )
    {
        var body = new { transaction = new { category_id = categoryId } };
        await SendAsync<TransactionData>(
            $"plans/{planId}/transactions/{transactionId}",
            HttpMethod.Patch,
            body,
            cancellationToken
        );
    }

    public async Task MoveAsync(
        string planId,
        string transactionId,
        string accountId,
        CancellationToken cancellationToken = default
    )
    {
        var body = new { transaction = new { account_id = accountId } };
        await SendAsync<TransactionData>(
            $"plans/{planId}/transactions/{transactionId}",
            HttpMethod.Patch,
            body,
            cancellationToken
        );
    }

    public async Task AssignAsync(
        string planId,
        DateOnly month,
        string categoryId,
        long budgetedMilliunits,
        CancellationToken cancellationToken = default
    )
    {
        var body = new { category = new { budgeted = budgetedMilliunits } };
        await SendAsync<MonthCategoryData>(
            $"plans/{planId}/months/{month:yyyy-MM-01}/categories/{categoryId}",
            HttpMethod.Patch,
            body,
            cancellationToken
        );
    }

    private async Task<T> SendAsync<T>(string path, HttpMethod method, object? body, CancellationToken cancellationToken)
        where T : class
    {
        // Not a loop: the hour is counted here, so a refusal is the end of the hour, not a moment.
        await _budget.AcquireAsync(
            WaitWorthTaking,
            delay => _say?.Invoke($"the hour's {RequestBudget.PerHour} requests are nearly spent; waiting {delay.TotalSeconds:0}s"),
            cancellationToken
        );

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _budget.MarkHourSpent();
            throw new RequestBudgetExhaustedException(_budget.DelayBeforeNext(), await ErrorDetail(response, cancellationToken));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new YnabApiException(response.StatusCode, await ErrorDetail(response, cancellationToken));
        }

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, cancellationToken);
        return envelope?.Data ?? throw new YnabApiException(response.StatusCode, "the answer had no data");
    }

    /// <summary>YNAB says <c>cleared</c>, <c>uncleared</c> or <c>reconciled</c> — never true or false.</summary>
    private static bool IsCleared(string? state) =>
        !string.Equals(state, "uncleared", StringComparison.OrdinalIgnoreCase);

    /// <summary>A DK B statement line can run past the API's 500 characters; the end is cut, not lost.</summary>
    private static string Capped(string memo, int maxLength = 500) =>
        memo.Length <= maxLength ? memo : string.Concat(memo.AsSpan(0, maxLength - 1), "…");

    private static async Task<string> ErrorDetail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, cancellationToken);
            return error?.Error?.Detail ?? response.ReasonPhrase ?? "no detail";
        }
        catch (JsonException)
        {
            return response.ReasonPhrase ?? "no detail";
        }
    }

    private sealed record Envelope<T>(T Data) where T : class;

    private sealed record ErrorEnvelope(ErrorDto? Error);

    private sealed record ErrorDto(string? Id, string? Name, string? Detail);

    private sealed record PlanListData(IReadOnlyList<PlanDto> Plans);

    private sealed record PlanDto(string Id, string Name);

    private sealed record AccountsData(IReadOnlyList<AccountDto> Accounts);

    private sealed record AccountDto(
        string Id,
        string Name,
        string Type,
        bool OnBudget,
        bool Closed,
        long Balance,
        long ClearedBalance,
        long UnclearedBalance
    );

    private sealed record CategoriesData(IReadOnlyList<CategoryGroupDto> CategoryGroups);

    private sealed record CategoryGroupDto(string Id, string Name, IReadOnlyList<CategoryDto> Categories);

    private sealed record CategoryDto(string Id, string Name, bool Internal);

    private sealed record PayeesData(IReadOnlyList<PayeeDto> Payees);

    private sealed record PayeeDto(string Id, string Name, string? TransferAccountId);

    private sealed record TransactionsData(IReadOnlyList<TransactionDto> Transactions);

    private sealed record TransactionData(TransactionDto Transaction);

    private sealed record MonthCategoryData(CategoryDto Category);

    private sealed record TransactionDto(
        string Id,
        DateOnly Date,
        long Amount,
        string AccountId,
        string AccountName,
        string? PayeeId,
        string? PayeeName,
        string? CategoryId,
        string? CategoryName,
        string? Memo,
        string Cleared,
        bool Approved,
        string? TransferAccountId,
        string? ImportId
    );
}
