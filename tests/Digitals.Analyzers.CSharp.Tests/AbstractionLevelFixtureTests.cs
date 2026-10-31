using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// A batch-import handler written twice, run through all six abstraction-level rules at once.
/// The ❌ scores 12 on S1541 and S3776 and so passes the complexity gate; the point of these rules
/// is that it no longer passes. The ✅ is the same use case as a recipe of named steps, helpers
/// included, and must stay silent.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AbstractionLevelFixtureTests
{
    private const string Stubs = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Probe.Domain
        {
        public enum BatchStatus { None, Failed, Processed }
        public enum BatchFailureReason { None, NoDocumentsDetected, ExpressMultipleDocuments, AllDuplicates }
        public enum BatchPriority { Normal, Express }

        public sealed class Batch
        {
            public Guid Id { get; set; }
            public bool IsProcessed { get; set; }
            public string SourceBlobName { get; set; } = "";
            public string SourceFileName { get; set; } = "";
            public BatchStatus Status { get; set; }
            public BatchFailureReason FailureReason { get; set; }
            public DateTimeOffset ProcessedDate { get; set; }
            public string SubmittedBy { get; set; } = "";
            public BatchPriority Priority { get; set; }

            public void Reject(BatchFailureReason reason, DateTimeOffset now)
            {
                Status = BatchStatus.Failed;
                FailureReason = reason;
                ProcessedDate = now;
            }

            public void MarkProcessed(DateTimeOffset now)
            {
                Status = BatchStatus.Processed;
                ProcessedDate = now;
            }
        }

        public sealed class BatchDocument
        {
            public Guid Id { get; set; }
            public Guid BatchId { get; set; }
            public string BlobName { get; set; } = "";
            public string FileName { get; set; } = "";
            public string FileExtension { get; set; } = "";
            public string ContentType { get; set; } = "";
            public int PageCount { get; set; }
            public long? FileSizeInBytes { get; set; }
            public Guid JobWorkId { get; set; }
            public DateTimeOffset CreatedDate { get; set; }
            public double ClassificationScore { get; set; }
        }
        }

        namespace Probe
        {
        using Probe.Domain;

        public sealed class JobWork { public Guid Id { get; set; } public Guid JobId { get; set; } public string CreatedBy { get; set; } = ""; }
        public sealed class BatchImportJobArgs { public Guid BatchId { get; set; } }
        public sealed class BatchStatusJobArgs { public Guid BatchId { get; set; } }
        public sealed class DocumentOcrJobArgs { public Guid DocumentId { get; set; } }
        public enum NotificationKind { None, BatchRejected, BatchProcessing }
        public static class JobWorkNames { public const string BatchStatusUpdate = "a"; public const string DocumentOcr = "b"; }

        public sealed class Download { public bool IsSuccess { get; set; } public string ErrorMessage { get; set; } = ""; public byte[] Content { get; set; } = Array.Empty<byte>(); }
        public sealed class Upload { public bool IsSuccess { get; set; } public string ErrorMessage { get; set; } = ""; }
        public sealed class ClassifiedDoc
        {
            public string BlobName { get; set; } = "";
            public byte[] Content { get; set; } = Array.Empty<byte>();
            public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
            public string FileName { get; set; } = "";
            public string FileExtension { get; set; } = "";
            public string ContentType { get; set; } = "";
            public int PageCount { get; set; }
            public double Score { get; set; }
        }

        public interface IBatchRepository { Task<Batch?> GetByIdAsync(Guid id, CancellationToken ct); void Update(Batch b); Task SaveChangesAsync(CancellationToken ct); }
        public interface IBatchDocumentRepository { void AddRange(IEnumerable<BatchDocument> docs); }
        public interface IBlobStorage { Task<Download> DownloadAsync(string name, CancellationToken ct); Task<Upload> UploadAsync(string name, Stream s, IDictionary<string, string> m, CancellationToken ct); }
        public interface IClassifier { Task<List<ClassifiedDoc>> ClassifyAsync(byte[] content, string fileName, CancellationToken ct); }
        public interface IUnitOfWork { void Begin(); Task CommitAsync(CancellationToken ct); }
        public interface IJobManager { Task CreateJobWorkAsync(Guid jobId, string name, object args, string createdBy, bool flag, CancellationToken ct); }
        public interface INotifications { Task SendAsync(string to, NotificationKind kind, CancellationToken ct); }
        public interface IMailer { Task SendRejectionEmailAsync(Batch b, string reason, CancellationToken ct); }
        public interface ILog { void LogInformation(string message, params object[] args); }
        public abstract class JobProcessor<TArgs> { protected abstract Task ProcessJobAsync(JobWork jobWork, TArgs payload, CancellationToken ct); }

        public abstract class ProcessorBase : JobProcessor<BatchImportJobArgs>
        {
            protected readonly IBatchRepository batchRepository = null!;
            protected readonly IBatchDocumentRepository batchDocumentRepository = null!;
            protected readonly IBlobStorage blobStorage = null!;
            protected readonly IClassifier classifier = null!;
            protected readonly IUnitOfWork unitOfWork = null!;
            protected readonly IJobManager jobManager = null!;
            protected readonly INotifications notifications = null!;
            protected readonly IMailer mailer = null!;
            protected readonly ILog _logger = null!;
            protected readonly TimeProvider timeProvider = TimeProvider.System;
        }
        }
        """;

    // The ❌: I/O checks, construction and the failure protocol all inline in one body.
    private const string Bad = """
        public sealed class BadProcessor : ProcessorBase
        {
            protected override async Task ProcessJobAsync(
                JobWork jobWork,
                BatchImportJobArgs payload,
                CancellationToken ct)
            {
                _logger.LogInformation("Starting batch import for JobWork {JobWorkId}", jobWork.Id);

                var batch = await batchRepository.GetByIdAsync(payload.BatchId, ct);
                if (batch is null) throw new InvalidOperationException($"Batch {payload.BatchId} not found.");
                if (batch.IsProcessed) { _logger.LogInformation("Already processed"); return; }

                var download = await blobStorage.DownloadAsync(batch.SourceBlobName, ct);
                if (!download.IsSuccess) throw new InvalidOperationException(download.ErrorMessage);

                var documents = await classifier.ClassifyAsync(download.Content, batch.SourceFileName, ct);

                if (documents.Count == 0)
                {
                    unitOfWork.Begin();
                    batch.Status = BatchStatus.Failed;
                    batch.FailureReason = BatchFailureReason.NoDocumentsDetected;
                    batch.ProcessedDate = timeProvider.GetUtcNow();
                    batchRepository.Update(batch);
                    await batchRepository.SaveChangesAsync(ct);
                    await jobManager.CreateJobWorkAsync(jobWork.JobId, JobWorkNames.BatchStatusUpdate,
                        new BatchStatusJobArgs { BatchId = batch.Id }, jobWork.CreatedBy, true, ct);
                    await unitOfWork.CommitAsync(ct);
                    await notifications.SendAsync(batch.SubmittedBy, NotificationKind.BatchRejected, ct);
                    await mailer.SendRejectionEmailAsync(batch, "No documents detected.", ct);
                    return;
                }

                if (batch.Priority == BatchPriority.Express && documents.Count > 1)
                {
                    unitOfWork.Begin();
                    batch.Status = BatchStatus.Failed;
                    batch.FailureReason = BatchFailureReason.ExpressMultipleDocuments;
                    batch.ProcessedDate = timeProvider.GetUtcNow();
                    batchRepository.Update(batch);
                    await batchRepository.SaveChangesAsync(ct);
                    await jobManager.CreateJobWorkAsync(jobWork.JobId, JobWorkNames.BatchStatusUpdate,
                        new BatchStatusJobArgs { BatchId = batch.Id }, jobWork.CreatedBy, true, ct);
                    await unitOfWork.CommitAsync(ct);
                    await notifications.SendAsync(batch.SubmittedBy, NotificationKind.BatchRejected, ct);
                    await mailer.SendRejectionEmailAsync(batch, "Express batch may contain a single document only.", ct);
                    return;
                }

                var sizeByName = documents.ToDictionary(d => d.BlobName, d => (long?)d.Content.LongLength);
                foreach (var doc in documents)
                {
                    using var stream = new MemoryStream(doc.Content, writable: false);
                    var upload = await blobStorage.UploadAsync(doc.BlobName, stream, doc.Metadata, ct);
                    if (!upload.IsSuccess) throw new InvalidOperationException(upload.ErrorMessage);
                }

                var extracted = new List<BatchDocument>();
                foreach (var doc in documents)
                {
                    extracted.Add(new BatchDocument
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batch.Id,
                        BlobName = doc.BlobName,
                        FileName = doc.FileName,
                        FileExtension = doc.FileExtension,
                        ContentType = doc.ContentType,
                        PageCount = doc.PageCount,
                        FileSizeInBytes = sizeByName[doc.BlobName],
                        JobWorkId = jobWork.Id,
                        CreatedDate = timeProvider.GetUtcNow(),
                        ClassificationScore = doc.Score
                    });
                }

                if (extracted.Count == 0)
                {
                    unitOfWork.Begin();
                    batch.Status = BatchStatus.Failed;
                    batch.FailureReason = BatchFailureReason.AllDuplicates;
                    batch.ProcessedDate = timeProvider.GetUtcNow();
                    batchRepository.Update(batch);
                    await batchRepository.SaveChangesAsync(ct);
                    await jobManager.CreateJobWorkAsync(jobWork.JobId, JobWorkNames.BatchStatusUpdate,
                        new BatchStatusJobArgs { BatchId = batch.Id }, jobWork.CreatedBy, true, ct);
                    await unitOfWork.CommitAsync(ct);
                    await notifications.SendAsync(batch.SubmittedBy, NotificationKind.BatchRejected, ct);
                    await mailer.SendRejectionEmailAsync(batch, "All documents were duplicates.", ct);
                    return;
                }

                unitOfWork.Begin();
                batchDocumentRepository.AddRange(extracted);
                batch.Status = BatchStatus.Processed;
                batch.ProcessedDate = timeProvider.GetUtcNow();
                batchRepository.Update(batch);
                await batchRepository.SaveChangesAsync(ct);
                foreach (var doc in extracted)
                {
                    await jobManager.CreateJobWorkAsync(jobWork.JobId, JobWorkNames.DocumentOcr,
                        new DocumentOcrJobArgs { DocumentId = doc.Id }, jobWork.CreatedBy, false, ct);
                }
                await unitOfWork.CommitAsync(ct);
                await notifications.SendAsync(batch.SubmittedBy, NotificationKind.BatchProcessing, ct);
            }
        }
        """;

    // The ✅: the same use case as named steps, helpers written out, and the entity changing
    // through behaviour methods.
    private const string Good = """
        public sealed class BatchContext
        {
            public BatchContext(Batch batch, JobWork jobWork) { Batch = batch; JobWork = jobWork; }
            public Batch Batch { get; }
            public JobWork JobWork { get; }
            public bool AlreadyProcessed => Batch.IsProcessed;
            public bool IsExpress => Batch.Priority == BatchPriority.Express;
        }

        public static class ClassifiedDocExtensions
        {
            public static BatchDocument ToBatchDocument(this ClassifiedDoc doc, BatchContext ctx, DateTimeOffset now) => new BatchDocument
            {
                Id = Guid.NewGuid(),
                BatchId = ctx.Batch.Id,
                BlobName = doc.BlobName,
                FileName = doc.FileName,
                FileExtension = doc.FileExtension,
                ContentType = doc.ContentType,
                PageCount = doc.PageCount,
                FileSizeInBytes = doc.Content.LongLength,
                JobWorkId = ctx.JobWork.Id,
                CreatedDate = now,
                ClassificationScore = doc.Score,
            };
        }

        public sealed class GoodProcessor : ProcessorBase
        {
            protected override async Task ProcessJobAsync(JobWork jobWork, BatchImportJobArgs payload, CancellationToken ct)
            {
                var ctx = await LoadContextAsync(payload.BatchId, jobWork, ct);
                if (ctx.AlreadyProcessed) return;

                var content = await DownloadSourceAsync(ctx, ct);
                var documents = await classifier.ClassifyAsync(content, ctx.Batch.SourceFileName, ct);

                if (documents.Count == 0)
                    { await AbortAsync(ctx, BatchFailureReason.NoDocumentsDetected, ct); return; }

                if (ctx.IsExpress && documents.Count > 1)
                    { await AbortAsync(ctx, BatchFailureReason.ExpressMultipleDocuments, ct); return; }

                await UploadDocumentsAsync(documents, ct);
                var now = timeProvider.GetUtcNow();
                var extracted = documents.Select(d => d.ToBatchDocument(ctx, now)).ToList();

                if (extracted.Count == 0)
                    { await AbortAsync(ctx, BatchFailureReason.AllDuplicates, ct); return; }

                await PersistAndEnqueueOcrAsync(extracted, ctx, ct);
                await notifications.SendAsync(ctx.Batch.SubmittedBy, NotificationKind.BatchProcessing, ct);
            }

            private async Task<BatchContext> LoadContextAsync(Guid batchId, JobWork jobWork, CancellationToken ct)
            {
                var batch = await batchRepository.GetByIdAsync(batchId, ct)
                    ?? throw new InvalidOperationException($"Batch {batchId} not found.");
                return new BatchContext(batch, jobWork);
            }

            private async Task<byte[]> DownloadSourceAsync(BatchContext ctx, CancellationToken ct)
            {
                var download = await blobStorage.DownloadAsync(ctx.Batch.SourceBlobName, ct);
                if (!download.IsSuccess) throw new InvalidOperationException(download.ErrorMessage);
                return download.Content;
            }

            private async Task UploadDocumentsAsync(List<ClassifiedDoc> documents, CancellationToken ct)
            {
                foreach (var doc in documents)
                {
                    using var stream = new MemoryStream(doc.Content, writable: false);
                    var upload = await blobStorage.UploadAsync(doc.BlobName, stream, doc.Metadata, ct);
                    if (!upload.IsSuccess) throw new InvalidOperationException(upload.ErrorMessage);
                }
            }

            private async Task AbortAsync(BatchContext ctx, BatchFailureReason reason, CancellationToken ct)
            {
                unitOfWork.Begin();
                ctx.Batch.Reject(reason, timeProvider.GetUtcNow());
                batchRepository.Update(ctx.Batch);
                await batchRepository.SaveChangesAsync(ct);
                await jobManager.CreateJobWorkAsync(ctx.JobWork.JobId, JobWorkNames.BatchStatusUpdate,
                    new BatchStatusJobArgs { BatchId = ctx.Batch.Id }, ctx.JobWork.CreatedBy, true, ct);
                await unitOfWork.CommitAsync(ct);
                await notifications.SendAsync(ctx.Batch.SubmittedBy, NotificationKind.BatchRejected, ct);
                await mailer.SendRejectionEmailAsync(ctx.Batch, reason.ToString(), ct);
            }

            private async Task PersistAndEnqueueOcrAsync(List<BatchDocument> extracted, BatchContext ctx, CancellationToken ct)
            {
                unitOfWork.Begin();
                batchDocumentRepository.AddRange(extracted);
                ctx.Batch.MarkProcessed(timeProvider.GetUtcNow());
                batchRepository.Update(ctx.Batch);
                await batchRepository.SaveChangesAsync(ct);
                foreach (var doc in extracted)
                {
                    await jobManager.CreateJobWorkAsync(ctx.JobWork.JobId, JobWorkNames.DocumentOcr,
                        new DocumentOcrJobArgs { DocumentId = doc.Id }, ctx.JobWork.CreatedBy, false, ct);
                }

                await unitOfWork.CommitAsync(ct);
            }
        }
        """;

    private static readonly string[] AllDesignRuleIds = ["DT00013", "DT00014", "DT00015", "DT00016", "DT00017", "DT00018"];

    private static readonly ImmutableArray<DiagnosticAnalyzer> DesignAnalyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
        new RepeatedStepSequenceAnalyzer(),
        new LargeObjectInitializerAnalyzer(),
        new OrchestrationStatementBudgetAnalyzer(),
        new MemberAssignmentRunAnalyzer(),
        new InlineResultUnwrapAnalyzer(),
        new OrchestrationInfrastructureCallAnalyzer());

    [Fact]
    public async Task BadHandler_PassesComplexityGate_ReportsEveryDesignRule()
    {
        var ids = await DiagnosticIdsAsync(InApplicationLayer(Bad));

        Assert.Equal(AllDesignRuleIds, ids.Distinct().Order());
    }

    [Fact]
    public async Task GoodHandler_WrittenToTheStandard_ReportsNothing()
    {
        var ids = await DiagnosticIdsAsync(InApplicationLayer(Good));

        Assert.Empty(ids);
    }

    // Entities and processors in separate namespaces, as a real solution would have them; no rule reads the names.
    private static string InApplicationLayer(string types) =>
        Stubs + "\nnamespace Probe.Application\n{\nusing Probe.Domain;\n" + types + "\n}\n";

    internal static async Task<IReadOnlyList<string>> DiagnosticIdsAsync(string source)
    {
        var references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "Fixture",
            [CSharpSyntaxTree.ParseText(source, path: "/Fixture.cs", cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        // No options: the processors count as orchestration because they call several collaborators.
        var options = new AnalyzerOptions([], new FixedOptions(new Dictionary<string, string>()));
        var diagnostics = await compilation
            .WithAnalyzers(DesignAnalyzers, options)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        return diagnostics.Select(d => d.Id).ToList();
    }

    private sealed class FixedOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Values(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
                values.TryGetValue(key, out value);
        }
    }
}
