using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Profit.Core;

public sealed record ImportRecord(string Id, string ImportKey, string Identity, string Fingerprint);
public sealed record ImportReplacement(ImportedTransaction Row, ImportRecord Existing);
public sealed class ImportMergePlan
{
    public List<ImportedTransaction> Added { get; } = [];
    public List<ImportReplacement> Replacements { get; } = [];
    public List<ImportReplacement> Matched { get; } = [];
}

public static class ImportMerge
{
    public static string Fingerprint(string date, string code, string customer, decimal quantity, decimal price, decimal total, decimal deductions)
    {
        var values = new[] { date, code.ToUpperInvariant(), Rules.MatchText(customer).ToUpperInvariant(), quantity.ToString("G29", CultureInfo.InvariantCulture), price.ToString("G29", CultureInfo.InvariantCulture), total.ToString("G29", CultureInfo.InvariantCulture), deductions.ToString("G29", CultureInfo.InvariantCulture) };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    }

    public static ImportMergePlan Plan(IEnumerable<ImportedTransaction> rows, IEnumerable<ImportRecord> existing)
    {
        var result = new ImportMergePlan(); var available = existing.Where(x => x.ImportKey.Length > 0).ToList();
        var unmatched = new List<ImportedTransaction>();
        foreach (var row in rows)
        {
            static string Group(string identity) => identity[..Math.Max(0, identity.LastIndexOf(':'))];
            var record = available.FirstOrDefault(x => x.ImportKey == row.ImportKey)
                ?? available.FirstOrDefault(x => x.Identity == row.Identity && x.Fingerprint == row.Fingerprint)
                ?? available.FirstOrDefault(x => x.Identity.Length > 0 && Group(x.Identity) == Group(row.Identity) && x.Fingerprint == row.Fingerprint)
                ?? available.FirstOrDefault(x => x.Identity.Length == 0 && x.Fingerprint == row.Fingerprint);
            if (record is not null) { result.Matched.Add(new(row, record)); available.Remove(record); continue; }
            unmatched.Add(row);
        }
        foreach (var row in unmatched)
        {
            var record = available.FirstOrDefault(x => x.Identity.Length > 0 && x.Identity == row.Identity);
            if (record is not null) { result.Replacements.Add(new(row, record)); available.Remove(record); }
            else result.Added.Add(row);
        }
        return result;
    }
}
