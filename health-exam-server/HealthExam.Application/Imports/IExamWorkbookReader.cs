using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HealthExam.Application.Imports;

public interface IExamWorkbookReader
{
    Task<ParsedImportSheet> ReadAsync(Stream file, string fileName = "", CancellationToken ct = default);
    Task<byte[]> CreateTemplateAsync(CancellationToken ct = default);
    Task<byte[]> CreateErrorWorkbookAsync(IReadOnlyList<ImportRowData> invalidRows, CancellationToken ct = default);
}
