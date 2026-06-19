using inventory_admin.Dtos;
using inventory_admin.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace inventory_admin.Services;

public partial class InventoryService
{
    public async Task<List<AiTokenUsageRecord>> GetAiTokenUsageRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiTokenUsageRecords
            .Include(record => record.AiModelPrice)
            .OrderByDescending(record => record.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AiModelPrice>> GetAiModelPricesAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiModelPrices
            .OrderByDescending(price => price.IsDefault)
            .ThenBy(price => price.Provider)
            .ThenBy(price => price.ModelName)
            .ThenBy(price => price.RouteName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AiMonthlyBudget>> GetAiMonthlyBudgetsAsync(
        CancellationToken cancellationToken = default)
    {
        return await db.AiMonthlyBudgets
            .OrderByDescending(budget => budget.MonthStart)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, AiMonthlyBudget? Budget)> SaveAiMonthlyBudgetAsync(
        AiMonthlyBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.BudgetUsd < 0)
        {
            return (false, "Monthly budget cannot be negative.", null);
        }

        if (request.WarningThresholdPercent < 0 || request.WarningThresholdPercent > 100 ||
            request.CriticalThresholdPercent < 0 || request.CriticalThresholdPercent > 100)
        {
            return (false, "Budget thresholds must be between 0 and 100 percent.", null);
        }

        if (request.WarningThresholdPercent > request.CriticalThresholdPercent)
        {
            return (false, "Warning threshold cannot exceed critical threshold.", null);
        }

        var monthStart = ToMonthStart(request.MonthStart);
        var now = DateTime.UtcNow;
        var budget = await db.AiMonthlyBudgets
            .FirstOrDefaultAsync(value => value.MonthStart == monthStart, cancellationToken);

        if (budget is null)
        {
            budget = new AiMonthlyBudget
            {
                MonthStart = monthStart,
                CreatedAt = now
            };
            db.AiMonthlyBudgets.Add(budget);
        }

        budget.BudgetUsd = request.BudgetUsd;
        budget.WarningThresholdPercent = request.WarningThresholdPercent;
        budget.CriticalThresholdPercent = request.CriticalThresholdPercent;
        budget.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI monthly budget saved.", budget);
    }

    public async Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(
        DateTime? monthStart = null,
        CancellationToken cancellationToken = default)
    {
        var periodStart = ToMonthStart(monthStart ?? DateTime.UtcNow);
        var periodEnd = periodStart.AddMonths(1);

        var records = await db.AiTokenUsageRecords
            .Where(record => record.CreatedAt >= periodStart && record.CreatedAt < periodEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var budget = await db.AiMonthlyBudgets
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.MonthStart == periodStart, cancellationToken);

        var estimated = records.Sum(record => record.EstimatedCostUsd);
        var actual = records.Sum(record => record.ActualCostUsd ?? 0m);
        var effective = records.Sum(record => record.EffectiveCostUsd);

        return new AiUsageSummaryDto(
            periodStart,
            periodEnd,
            records.Count,
            records.Sum(record => record.PromptTokens),
            records.Sum(record => record.CachedInputTokens),
            records.Sum(record => record.CompletionTokens),
            records.Sum(record => record.ReasoningTokens),
            records.Sum(record => record.TotalTokens),
            records.Sum(record => record.ToolCallCount),
            records.Sum(record => record.SearchCallCount),
            estimated,
            actual,
            effective,
            actual - estimated,
            budget is null ? null : ToBudgetStatus(budget, effective),
            BuildUsageGroups(records, "feature"),
            BuildUsageGroups(records, "provider"),
            BuildUsageGroups(records, "model"),
            BuildUsageGroups(records, "route"));
    }

    public async Task<(bool Succeeded, string Message, AiModelPrice? Price)> SaveAiModelPriceAsync(
        AiModelPrice price,
        CancellationToken cancellationToken = default)
    {
        price.Provider = price.Provider.Trim();
        price.BillingProvider = NormalizeOptional(price.BillingProvider);
        price.UpstreamProvider = NormalizeOptional(price.UpstreamProvider);
        price.ModelName = price.ModelName.Trim();
        price.OpenRouterModelSlug = NormalizeOptional(price.OpenRouterModelSlug);
        price.RouteName = NormalizeOptional(price.RouteName) ?? "Custom";
        price.Currency = string.IsNullOrWhiteSpace(price.Currency) ? "USD" : price.Currency.Trim().ToUpperInvariant();
        price.SourceUrl = NormalizeOptional(price.SourceUrl);

        if (string.IsNullOrWhiteSpace(price.Provider))
        {
            return (false, "Provider is required.", null);
        }

        if (string.IsNullOrWhiteSpace(price.ModelName))
        {
            return (false, "Model name is required.", null);
        }

        if (price.Currency.Length != 3)
        {
            return (false, "Currency must be a 3-letter code.", null);
        }

        if (price.InputCostPerMillionTokens < 0 ||
            price.CachedInputCostPerMillionTokens < 0 ||
            price.OutputCostPerMillionTokens < 0 ||
            price.SearchCostPerThousandCalls < 0)
        {
            return (false, "Prices cannot be negative.", null);
        }

        var now = DateTime.UtcNow;
        price.EffectiveFrom = price.EffectiveFrom == default ? now.Date : price.EffectiveFrom;
        price.CreatedAt = now;
        price.UpdatedAt = now;
        price.IsCustom = true;
        price.IsDefault = false;

        db.AiModelPrices.Add(price);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI model price saved.", price);
    }

    public Task<(bool Succeeded, string Message, AiModelPrice? Price)> SaveAiModelPriceAsync(
        AiModelPriceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveAiModelPriceAsync(
            new AiModelPrice
            {
                Provider = request.Provider,
                BillingProvider = request.BillingProvider,
                UpstreamProvider = request.UpstreamProvider,
                ModelName = request.ModelName,
                OpenRouterModelSlug = request.OpenRouterModelSlug,
                RouteName = request.RouteName,
                InputCostPerMillionTokens = request.InputCostPerMillionTokens,
                CachedInputCostPerMillionTokens = request.CachedInputCostPerMillionTokens,
                OutputCostPerMillionTokens = request.OutputCostPerMillionTokens,
                SearchCostPerThousandCalls = request.SearchCostPerThousandCalls,
                EffectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow.Date,
                SourceUrl = request.SourceUrl
            },
            cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, AiTokenUsageRecord? Record)> SaveAiTokenUsageRecordAsync(
        AiTokenUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        record.FeatureName = record.FeatureName.Trim();
        record.Provider = string.IsNullOrWhiteSpace(record.Provider) ? "OpenAI" : record.Provider.Trim();
        record.BillingProvider = string.IsNullOrWhiteSpace(record.BillingProvider)
            ? record.Provider
            : record.BillingProvider.Trim();
        record.UpstreamProvider = NormalizeOptional(record.UpstreamProvider);
        record.ModelName = record.ModelName.Trim();
        record.OpenRouterModelSlug = NormalizeOptional(record.OpenRouterModelSlug);
        record.RouteName = NormalizeOptional(record.RouteName);
        record.Notes = NormalizeOptional(record.Notes);

        if (record.AiModelPriceId is > 0)
        {
            var price = await db.AiModelPrices
                .AsNoTracking()
                .FirstOrDefaultAsync(value => value.Id == record.AiModelPriceId, cancellationToken);

            if (price is null)
            {
                return (false, "Choose a valid AI model price.", null);
            }

            ApplyPriceSnapshot(record, price);
        }

        if (string.IsNullOrWhiteSpace(record.FeatureName))
        {
            return (false, "Feature name is required.", null);
        }

        if (string.IsNullOrWhiteSpace(record.ModelName))
        {
            return (false, "Model name is required.", null);
        }

        if (record.PromptTokens < 0 ||
            record.CompletionTokens < 0 ||
            record.CachedInputTokens < 0 ||
            record.ReasoningTokens < 0 ||
            record.ToolCallCount < 0 ||
            record.SearchCallCount < 0)
        {
            return (false, "Usage counts cannot be negative.", null);
        }

        if (record.PromptTokens + record.CachedInputTokens + record.CompletionTokens + record.SearchCallCount == 0 &&
            (record.ActualCostUsd ?? 0m) == 0)
        {
            return (false, "Enter at least one token, billable search call, or actual cost.", null);
        }

        if (record.PromptCostPerMillionTokens < 0 ||
            record.CachedInputCostPerMillionTokens < 0 ||
            record.CompletionCostPerMillionTokens < 0 ||
            record.SearchCostPerThousandCalls < 0 ||
            record.ActualCostUsd < 0)
        {
            return (false, "Prices cannot be negative.", null);
        }

        record.EstimatedCostUsd = CalculateTokenCost(
            record.PromptTokens,
            record.CachedInputTokens,
            record.CompletionTokens,
            record.SearchCallCount,
            record.PromptCostPerMillionTokens,
            record.CachedInputCostPerMillionTokens,
            record.CompletionCostPerMillionTokens,
            record.SearchCostPerThousandCalls);
        if (record.CreatedAt == default)
        {
            record.CreatedAt = DateTime.UtcNow;
        }

        db.AiTokenUsageRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return (true, "AI usage record saved.", record);
    }

    public Task<(bool Succeeded, string Message, AiTokenUsageRecord? Record)> SaveAiTokenUsageRecordAsync(
        AiTokenUsageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return SaveAiTokenUsageRecordAsync(
            new AiTokenUsageRecord
            {
                FeatureName = request.FeatureName,
                Provider = request.Provider,
                BillingProvider = request.BillingProvider,
                UpstreamProvider = request.UpstreamProvider,
                ModelName = request.ModelName,
                OpenRouterModelSlug = request.OpenRouterModelSlug,
                RouteName = request.RouteName,
                AiModelPriceId = request.AiModelPriceId,
                PromptTokens = request.PromptTokens,
                CachedInputTokens = request.CachedInputTokens,
                CompletionTokens = request.CompletionTokens,
                ReasoningTokens = request.ReasoningTokens,
                ToolCallCount = request.ToolCallCount,
                SearchCallCount = request.SearchCallCount,
                PromptCostPerMillionTokens = request.PromptCostPerMillionTokens,
                CachedInputCostPerMillionTokens = request.CachedInputCostPerMillionTokens,
                CompletionCostPerMillionTokens = request.CompletionCostPerMillionTokens,
                SearchCostPerThousandCalls = request.SearchCostPerThousandCalls,
                ActualCostUsd = request.ActualCostUsd,
                CreatedAt = request.CreatedAt ?? DateTime.UtcNow,
                Notes = request.Notes
            },
            cancellationToken);
    }

    public async Task<AiTokenUsageImportResult> ImportAiTokenUsageAsync(
        AiTokenUsageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAiTokenUsageImportAsync(request, cancellationToken);
        var selectedRows = request.SelectedRowNumbers is null || request.SelectedRowNumbers.Count == 0
            ? preview.Rows.Where(row => row.IsSelected)
            : preview.Rows.Where(row => request.SelectedRowNumbers.Contains(row.RowNumber));
        var imported = 0;
        var errors = preview.Errors.ToList();

        foreach (var previewRow in selectedRows)
        {
            if (!previewRow.IsValid)
            {
                errors.Add(new AiTokenUsageImportError(previewRow.RowNumber, "Row is invalid and was not imported."));
                continue;
            }

            if (previewRow.IsDuplicate)
            {
                errors.Add(new AiTokenUsageImportError(previewRow.RowNumber, "Likely duplicate row was not imported."));
                continue;
            }

            var record = ToAiTokenUsageRecord(previewRow);
            var result = await SaveAiTokenUsageRecordAsync(record, cancellationToken);

            if (result.Succeeded)
            {
                imported++;
            }
            else
            {
                errors.Add(new AiTokenUsageImportError(previewRow.RowNumber, result.Message));
            }
        }

        return new AiTokenUsageImportResult(imported, errors);
    }

    public async Task<AiTokenUsageImportPreviewResult> PreviewAiTokenUsageImportAsync(
        AiTokenUsageImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return new AiTokenUsageImportPreviewResult(
                [],
                [new AiTokenUsageImportError(0, "Paste CSV or JSON usage data to import.")]);
        }

        var errors = new List<AiTokenUsageImportError>();
        var rows = ParseAiUsageImportRows(request.Content, request.Format, errors);
        var existingRecords = await db.AiTokenUsageRecords
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var selectedRows = request.SelectedRowNumbers?.ToHashSet() ?? [];
        var previewRows = new List<AiTokenUsageImportPreviewRow>();

        foreach (var row in rows)
        {
            var rowErrors = new List<string>();
            if (!TryCreateAiTokenUsageRecord(row, rowErrors, out var record))
            {
                foreach (var rowError in rowErrors)
                {
                    errors.Add(new AiTokenUsageImportError(row.RowNumber, rowError));
                }
            }

            var isDuplicate = rowErrors.Count == 0 && IsLikelyAiUsageDuplicate(record, existingRecords);
            var isValid = rowErrors.Count == 0;
            var isSelected = request.SelectedRowNumbers is { Count: > 0 }
                ? selectedRows.Contains(row.RowNumber)
                : isValid && !isDuplicate;
            previewRows.Add(ToAiImportPreviewRow(row.RowNumber, record, isValid, isDuplicate, isSelected, rowErrors));
        }

        return new AiTokenUsageImportPreviewResult(previewRows, errors);
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageImportRows(
        string content,
        string? format,
        List<AiTokenUsageImportError> errors)
    {
        var normalizedFormat = NormalizeOptional(format)?.ToLowerInvariant();
        var trimmed = content.TrimStart();

        try
        {
            return normalizedFormat switch
            {
                null or "" when trimmed.StartsWith('[') || trimmed.StartsWith('{') => ParseAiUsageJsonRows(content, errors),
                null or "" => ParseAiUsageCsvRows(content, errors),
                "json" => ParseAiUsageJsonRows(content, errors),
                "csv" => ParseAiUsageCsvRows(content, errors),
                _ => AddImportParseError(errors, $"Unsupported AI usage import format '{format}'. Use csv or json.")
            };
        }
        catch (JsonException ex)
        {
            errors.Add(new AiTokenUsageImportError(0, $"JSON import could not be parsed: {ex.Message}"));
            return [];
        }
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageJsonRows(
        string content,
        List<AiTokenUsageImportError> errors)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var rows = new List<AiUsageImportRow>();

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetJsonProperty(root, out var data, "data", "records", "rows", "items", "usage"))
        {
            root = data;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            rows.Add(new AiUsageImportRow(1, JsonObjectToDictionary(root)));
            return rows;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new AiTokenUsageImportError(0, "JSON import must be an object, an array, or an object with a records/data array."));
            return [];
        }

        var rowNumber = 1;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                rows.Add(new AiUsageImportRow(rowNumber, JsonObjectToDictionary(item)));
            }
            else
            {
                errors.Add(new AiTokenUsageImportError(rowNumber, "JSON array item must be an object."));
            }

            rowNumber++;
        }

        return rows;
    }

    private static bool TryGetJsonProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        var normalizedNames = names.Select(NormalizeImportKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (normalizedNames.Contains(NormalizeImportKey(property.Name)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<string, string?> JsonObjectToDictionary(JsonElement element)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[NormalizeImportKey(property.Name)] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText()
            };
        }

        return values;
    }

    private static IReadOnlyList<AiUsageImportRow> ParseAiUsageCsvRows(
        string content,
        List<AiTokenUsageImportError> errors)
    {
        var records = SplitCsvRecords(content);
        if (records.Count == 0)
        {
            errors.Add(new AiTokenUsageImportError(0, "CSV import is empty."));
            return [];
        }

        var headers = records[0]
            .Select(NormalizeImportKey)
            .ToList();

        if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
        {
            errors.Add(new AiTokenUsageImportError(0, "CSV import must include a header row."));
            return [];
        }

        var rows = new List<AiUsageImportRow>();
        for (var index = 1; index < records.Count; index++)
        {
            var cells = records[index];
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < headers.Count && column < cells.Count; column++)
            {
                if (!string.IsNullOrWhiteSpace(headers[column]) && !IsSecretImportKey(headers[column]))
                {
                    values[headers[column]] = cells[column];
                }
            }

            rows.Add(new AiUsageImportRow(index + 1, values));
        }

        return rows;
    }

    private static List<List<string>> SplitCsvRecords(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                row.Add(current.ToString().Trim());
                current.Clear();
            }
            else if ((character == '\n' || character == '\r') && !inQuotes)
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(current.ToString().Trim());
                current.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                current.Append(character);
            }
        }

        if (inQuotes)
        {
            return rows;
        }

        row.Add(current.ToString().Trim());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static bool TryCreateAiTokenUsageRecord(
        AiUsageImportRow row,
        List<string> errors,
        out AiTokenUsageRecord record)
    {
        var provider = ReadString(row.Values, "provider", "directprovider") ?? "OpenAI";
        var billingProvider = ReadString(row.Values, "billingprovider", "billing", "gateway") ?? provider;
        var notes = ReadString(row.Values, "notes", "note");

        if (ContainsSecretLikeValue(notes))
        {
            errors.Add("Notes look like they contain a secret and were not imported.");
        }

        record = new AiTokenUsageRecord
        {
            FeatureName = ReadString(row.Values, "featurename", "feature", "source", "app", "endpoint") ?? "Imported usage",
            Provider = provider,
            BillingProvider = billingProvider,
            UpstreamProvider = ReadString(row.Values, "upstreamprovider", "upstream", "providername"),
            ModelName = ReadString(row.Values, "modelname", "model", "modelslug", "openroutermodelslug") ?? string.Empty,
            OpenRouterModelSlug = ReadString(row.Values, "openroutermodelslug", "modelpermaslug", "modelslug"),
            RouteName = ReadString(row.Values, "routename", "route", "router", "servicetier"),
            PromptTokens = ReadInt(row.Values, errors, "prompttokens", "inputtokens", "tokensprompt", "nativetokensprompt"),
            CachedInputTokens = ReadInt(row.Values, errors, "cachedinputtokens", "cachetokens", "cachedtokens", "nativetokenscached"),
            CompletionTokens = ReadInt(row.Values, errors, "completiontokens", "outputtokens", "tokenscompletion", "nativetokenscompletion"),
            ReasoningTokens = ReadInt(row.Values, errors, "reasoningtokens", "nativetokensreasoning"),
            ToolCallCount = ReadInt(row.Values, errors, "toolcallcount", "toolcalls"),
            SearchCallCount = ReadInt(row.Values, errors, "searchcallcount", "searchcalls", "numsearchresults"),
            PromptCostPerMillionTokens = ReadDecimal(row.Values, errors, "promptcostpermilliontokens", "inputcostpermilliontokens"),
            CachedInputCostPerMillionTokens = ReadDecimal(row.Values, errors, "cachedinputcostpermilliontokens"),
            CompletionCostPerMillionTokens = ReadDecimal(row.Values, errors, "completioncostpermilliontokens", "outputcostpermilliontokens"),
            SearchCostPerThousandCalls = ReadDecimal(row.Values, errors, "searchcostperthousandcalls"),
            ActualCostUsd = ReadNullableDecimal(row.Values, errors, "actualcostusd", "costusd", "cost", "totalcost", "usage", "upstreaminferencecost"),
            CreatedAt = ReadDateTime(row.Values, errors, "createdat", "created", "date", "timestamp") ?? DateTime.UtcNow,
            Notes = notes
        };

        if (string.IsNullOrWhiteSpace(record.ModelName))
        {
            errors.Add("Model is required.");
        }

        var hasBillableUsage = record.PromptTokens + record.CachedInputTokens + record.CompletionTokens + record.SearchCallCount > 0 ||
            (record.ActualCostUsd ?? 0m) > 0;
        if (!hasBillableUsage)
        {
            errors.Add("Enter at least one token, billable search call, or actual cost.");
        }

        return errors.Count == 0;
    }

    private static AiTokenUsageImportPreviewRow ToAiImportPreviewRow(
        int rowNumber,
        AiTokenUsageRecord record,
        bool isValid,
        bool isDuplicate,
        bool isSelected,
        IReadOnlyList<string> errors)
    {
        var estimatedCost = CalculateTokenCost(
            record.PromptTokens,
            record.CachedInputTokens,
            record.CompletionTokens,
            record.SearchCallCount,
            record.PromptCostPerMillionTokens,
            record.CachedInputCostPerMillionTokens,
            record.CompletionCostPerMillionTokens,
            record.SearchCostPerThousandCalls);

        return new AiTokenUsageImportPreviewRow(
            rowNumber,
            isValid,
            isDuplicate,
            isSelected,
            record.FeatureName,
            record.Provider,
            record.BillingProvider,
            record.UpstreamProvider,
            record.ModelName,
            record.OpenRouterModelSlug,
            record.RouteName,
            record.PromptTokens,
            record.CachedInputTokens,
            record.CompletionTokens,
            record.ReasoningTokens,
            record.ToolCallCount,
            record.SearchCallCount,
            record.PromptCostPerMillionTokens,
            record.CachedInputCostPerMillionTokens,
            record.CompletionCostPerMillionTokens,
            record.SearchCostPerThousandCalls,
            estimatedCost,
            record.ActualCostUsd,
            record.ActualCostUsd ?? estimatedCost,
            record.CreatedAt,
            record.Notes,
            errors);
    }

    private static AiTokenUsageRecord ToAiTokenUsageRecord(AiTokenUsageImportPreviewRow row)
    {
        return new AiTokenUsageRecord
        {
            FeatureName = row.FeatureName,
            Provider = row.Provider,
            BillingProvider = row.BillingProvider,
            UpstreamProvider = row.UpstreamProvider,
            ModelName = row.ModelName,
            OpenRouterModelSlug = row.OpenRouterModelSlug,
            RouteName = row.RouteName,
            PromptTokens = row.PromptTokens,
            CachedInputTokens = row.CachedInputTokens,
            CompletionTokens = row.CompletionTokens,
            ReasoningTokens = row.ReasoningTokens,
            ToolCallCount = row.ToolCallCount,
            SearchCallCount = row.SearchCallCount,
            PromptCostPerMillionTokens = row.PromptCostPerMillionTokens,
            CachedInputCostPerMillionTokens = row.CachedInputCostPerMillionTokens,
            CompletionCostPerMillionTokens = row.CompletionCostPerMillionTokens,
            SearchCostPerThousandCalls = row.SearchCostPerThousandCalls,
            ActualCostUsd = row.ActualCostUsd,
            CreatedAt = row.CreatedAt,
            Notes = row.Notes
        };
    }

    private static bool IsLikelyAiUsageDuplicate(
        AiTokenUsageRecord incoming,
        IReadOnlyList<AiTokenUsageRecord> existingRecords)
    {
        var incomingCost = incoming.ActualCostUsd ?? CalculateTokenCost(
            incoming.PromptTokens,
            incoming.CachedInputTokens,
            incoming.CompletionTokens,
            incoming.SearchCallCount,
            incoming.PromptCostPerMillionTokens,
            incoming.CachedInputCostPerMillionTokens,
            incoming.CompletionCostPerMillionTokens,
            incoming.SearchCostPerThousandCalls);

        return existingRecords.Any(existing =>
            string.Equals(existing.Provider, incoming.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.ModelName, incoming.ModelName, StringComparison.OrdinalIgnoreCase) &&
            existing.CreatedAt.Date == incoming.CreatedAt.Date &&
            existing.PromptTokens == incoming.PromptTokens &&
            existing.CachedInputTokens == incoming.CachedInputTokens &&
            existing.CompletionTokens == incoming.CompletionTokens &&
            existing.ReasoningTokens == incoming.ReasoningTokens &&
            existing.SearchCallCount == incoming.SearchCallCount &&
            Math.Abs(existing.EffectiveCostUsd - incomingCost) < 0.000001m);
    }

    private static string NormalizeImportKey(string key)
    {
        return new string(key
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool IsSecretImportKey(string key)
    {
        var normalized = NormalizeImportKey(key);
        return normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized is "key" or "token" or "accesstoken" or "refreshtoken";
    }

    private static bool ContainsSecretLikeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("authorization:", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("bearer ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(Dictionary<string, string?> row, params string[] keys)
    {
        foreach (var key in keys.Select(NormalizeImportKey))
        {
            if (!IsSecretImportKey(key) &&
                row.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int ReadInt(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add($"{keys[0]} must be a whole number.");
        return 0;
    }

    private static decimal ReadDecimal(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        return ReadNullableDecimal(row, errors, keys) ?? 0m;
    }

    private static decimal? ReadNullableDecimal(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        errors.Add($"{keys[0]} must be a number.");
        return null;
    }

    private static DateTime? ReadDateTime(Dictionary<string, string?> row, List<string> errors, params string[] keys)
    {
        var value = ReadString(row, keys);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ||
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
        }

        errors.Add($"{keys[0]} must be a date/time.");
        return null;
    }

    private static IReadOnlyList<AiUsageImportRow> AddImportParseError(
        List<AiTokenUsageImportError> errors,
        string message)
    {
        errors.Add(new AiTokenUsageImportError(0, message));
        return [];
    }

    private static DateTime ToMonthStart(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return new DateTime(utcValue.Year, utcValue.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static AiBudgetStatusDto ToBudgetStatus(AiMonthlyBudget budget, decimal effectiveSpendUsd)
    {
        var percentUsed = budget.BudgetUsd == 0
            ? effectiveSpendUsd > 0 ? 100m : 0m
            : Math.Round(effectiveSpendUsd / budget.BudgetUsd * 100m, 2, MidpointRounding.AwayFromZero);
        var isCritical = percentUsed >= budget.CriticalThresholdPercent;
        var isWarning = !isCritical && percentUsed >= budget.WarningThresholdPercent;
        var status = isCritical ? "Critical" : isWarning ? "Warning" : "Ok";

        return new AiBudgetStatusDto(
            budget.Id,
            budget.MonthStart,
            budget.BudgetUsd,
            budget.WarningThresholdPercent,
            budget.CriticalThresholdPercent,
            effectiveSpendUsd,
            budget.BudgetUsd - effectiveSpendUsd,
            percentUsed,
            isWarning,
            isCritical,
            status);
    }

    private static IReadOnlyList<AiUsageGroupDto> BuildUsageGroups(
        IReadOnlyList<AiTokenUsageRecord> records,
        string groupBy)
    {
        static string ProviderKey(AiTokenUsageRecord record) =>
            $"{record.BillingProvider} / {record.Provider} / {record.UpstreamProvider ?? "Direct"}";

        static string ModelKey(AiTokenUsageRecord record) =>
            string.IsNullOrWhiteSpace(record.OpenRouterModelSlug)
                ? record.ModelName
                : record.OpenRouterModelSlug;

        static string RouteKey(AiTokenUsageRecord record) =>
            string.IsNullOrWhiteSpace(record.RouteName)
                ? $"{ModelKey(record)} / Standard"
                : $"{ModelKey(record)} / {record.RouteName}";

        var groups = groupBy switch
        {
            "feature" => records.GroupBy(record => record.FeatureName),
            "provider" => records.GroupBy(ProviderKey),
            "model" => records.GroupBy(ModelKey),
            "route" => records.GroupBy(RouteKey),
            _ => records.GroupBy(record => record.FeatureName)
        };

        return groups
            .Select(group =>
            {
                var first = group.First();
                var estimated = group.Sum(record => record.EstimatedCostUsd);
                var actual = group.Sum(record => record.ActualCostUsd ?? 0m);
                var effective = group.Sum(record => record.EffectiveCostUsd);

                return new AiUsageGroupDto(
                    group.Key,
                    first.Provider,
                    first.BillingProvider,
                    first.UpstreamProvider,
                    first.ModelName,
                    first.OpenRouterModelSlug,
                    first.RouteName,
                    group.Count(),
                    group.Sum(record => record.TotalTokens),
                    group.Sum(record => record.SearchCallCount),
                    estimated,
                    actual,
                    effective);
            })
            .OrderByDescending(group => group.EffectiveCostUsd)
            .ThenBy(group => group.Key)
            .ToList();
    }

    private static decimal CalculateTokenCost(
        int promptTokens,
        int cachedInputTokens,
        int completionTokens,
        int searchCallCount,
        decimal promptCostPerMillionTokens,
        decimal cachedInputCostPerMillionTokens,
        decimal completionCostPerMillionTokens,
        decimal searchCostPerThousandCalls)
    {
        var inputCost = promptTokens / 1_000_000m * promptCostPerMillionTokens;
        var cachedInputCost = cachedInputTokens / 1_000_000m * cachedInputCostPerMillionTokens;
        var outputCost = completionTokens / 1_000_000m * completionCostPerMillionTokens;
        var searchCost = searchCallCount / 1_000m * searchCostPerThousandCalls;
        return Math.Round(inputCost + cachedInputCost + outputCost + searchCost, 6, MidpointRounding.AwayFromZero);
    }

    private static void ApplyPriceSnapshot(AiTokenUsageRecord record, AiModelPrice price)
    {
        record.Provider = price.Provider;
        record.BillingProvider = price.BillingProvider ?? price.Provider;
        record.UpstreamProvider = price.UpstreamProvider;
        record.ModelName = price.ModelName;
        record.OpenRouterModelSlug = price.OpenRouterModelSlug;
        record.RouteName = price.RouteName;
        record.PromptCostPerMillionTokens = price.InputCostPerMillionTokens;
        record.CachedInputCostPerMillionTokens = price.CachedInputCostPerMillionTokens;
        record.CompletionCostPerMillionTokens = price.OutputCostPerMillionTokens;
        record.SearchCostPerThousandCalls = price.SearchCostPerThousandCalls;
    }

    private sealed record AiUsageImportRow(
        int RowNumber,
        Dictionary<string, string?> Values);
}
