using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.RepeatedStepSequenceAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class RepeatedStepSequenceAnalyzerTests
{
    private const string Types = """
        namespace App
        {
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            public enum Reason { None, Empty, Express, Duplicate }

            public interface IUnitOfWork { Task CommitAsync(CancellationToken ct); }

            public interface INotifications { Task SendAsync(string to, CancellationToken ct); }

            public interface IMailer { Task SendRejectionAsync(Batch batch, Reason reason, CancellationToken ct); }

            public sealed class Batch
            {
                public string SubmittedBy { get; set; } = "";
                public int Status { get; set; }
                public void Reject(Reason reason) { }
            }

            public sealed class ImportHandler
            {
                private readonly IUnitOfWork unitOfWork = null!;
                private readonly INotifications notifications = null!;
                private readonly IMailer mailer = null!;
                private readonly List<string> names = new();

                public async Task Handle(Batch batch, int count, bool express, CancellationToken ct)
                {
        BODY
                }

        MEMBERS
            }
        }
        """;

    // The failure protocol from the standard: copied into two branches, the second copy already missing the email.
    private const string DriftedCopies = """
                    if (count == 0)
                    {
                        {|#0:batch.Reject(Reason.Empty);|}
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                        await mailer.SendRejectionAsync(batch, Reason.Empty, ct);
                        return;
                    }

                    if (express && count > 1)
                    {
                        {|#1:batch.Reject(Reason.Express);|}
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                        return;
                    }
        """;

    private static readonly string Unmarked = DriftedCopies.Replace("{|#0:", "", StringComparison.Ordinal).Replace("{|#1:", "", StringComparison.Ordinal).Replace(";|}", ";", StringComparison.Ordinal);

    private static Verify Test(string body, string members = "", string? config = null)
    {
        var test = new Verify
        {
            TestCode = Types.Replace("BODY", body, StringComparison.Ordinal).Replace("MEMBERS", members, StringComparison.Ordinal),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        if (config is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true\n" + config));
        }

        return test;
    }

    private static DiagnosticResult Warning(int run, string steps, int copies)
    {
        var result = new DiagnosticResult("DT00013", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        for (var i = 0; i < copies; i++)
        {
            result = result.WithLocation(i);
        }

        return result.WithArguments("Handle", run, steps, copies);
    }

    [Fact]
    public async Task DriftedCopies_ReportsSharedRun()
    {
        var test = Test(DriftedCopies);
        test.ExpectedDiagnostics.Add(Warning(3, "Reject, CommitAsync, SendAsync", 2));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThreeFullCopies_ReportsWholeRun()
    {
        var test = Test("""
                    if (count == 0)
                    {
                        {|#0:batch.Reject(Reason.Empty);|}
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                        await mailer.SendRejectionAsync(batch, Reason.Empty, ct);
                        return;
                    }

                    if (express)
                    {
                        {|#1:batch.Reject(Reason.Express);|}
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                        await mailer.SendRejectionAsync(batch, Reason.Express, ct);
                        return;
                    }

                    if (count > 10)
                    {
                        {|#2:batch.Reject(Reason.Duplicate);|}
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                        await mailer.SendRejectionAsync(batch, Reason.Duplicate, ct);
                        return;
                    }
            """);
        test.ExpectedDiagnostics.Add(Warning(5, "Reject, CommitAsync, SendAsync, SendRejectionAsync", 3));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CopiesInOneBlock_ReportsDT00013()
    {
        var test = Test("""
                    {|#0:await notifications.SendAsync("ops", ct);|}
                    await unitOfWork.CommitAsync(ct);
                    batch.Status = 1;
                    {|#1:await notifications.SendAsync("audit", ct);|}
                    await unitOfWork.CommitAsync(ct);
                    batch.Status = 2;
            """);
        test.ExpectedDiagnostics.Add(Warning(3, "SendAsync, CommitAsync", 2));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OnlyTwoStepsShared_NoDiagnostic()
    {
        var test = Test("""
                    if (count == 0)
                    {
                        batch.Reject(Reason.Empty);
                        await unitOfWork.CommitAsync(ct);
                        return;
                    }

                    if (express)
                    {
                        batch.Reject(Reason.Express);
                        await unitOfWork.CommitAsync(ct);
                        await mailer.SendRejectionAsync(batch, Reason.Express, ct);
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OneReceiver_NoDiagnostic()
    {
        var test = Test("""
                    if (count == 0)
                    {
                        names.Add("a");
                        names.Add("b");
                        names.Remove("c");
                    }

                    if (express)
                    {
                        names.Add("d");
                        names.Add("e");
                        names.Remove("f");
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CopiesInDifferentMembers_NoDiagnostic()
    {
        var test = Test(
            """
                    batch.Reject(Reason.Empty);
                    await unitOfWork.CommitAsync(ct);
                    await notifications.SendAsync(batch.SubmittedBy, ct);
            """,
            """
                    private async Task RejectExpressAsync(Batch batch, CancellationToken ct)
                    {
                        batch.Reject(Reason.Express);
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalFunction_AnalysedOnItsOwn()
    {
        var test = Test("""
                    batch.Reject(Reason.Empty);
                    await unitOfWork.CommitAsync(ct);
                    await notifications.SendAsync(batch.SubmittedBy, ct);

                    async Task RejectExpressAsync()
                    {
                        batch.Reject(Reason.Express);
                        await unitOfWork.CommitAsync(ct);
                        await notifications.SendAsync(batch.SubmittedBy, ct);
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LengthOption_RaisesTheBar()
    {
        var test = Test(Unmarked, config: "digitals_repeated_sequence_length = 4");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OccurrencesOption_RaisesTheBar()
    {
        var test = Test(Unmarked, config: "digitals_repeated_sequence_occurrences = 3");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var body = "#pragma warning disable DT00013\n" + Unmarked + "\n#pragma warning restore DT00013";
        await Test(body).RunAsync(TestContext.Current.CancellationToken);
    }
}
