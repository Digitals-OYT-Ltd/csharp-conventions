using Microsoft.CodeAnalysis.Testing;
using Xunit;

// Fully qualified: a using alias never sees the other using directives in its own file.
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Testing.CSharp.TestSleepAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Testing.CSharp.Tests;

public sealed class TestSleepAnalyzerTests
{
    // Net80 carries Thread, Task and TimeProvider, so no stub is needed here.
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Expected(string display)
        => new DiagnosticResult(TestSleepAnalyzer.Rule).WithLocation(0).WithArguments(display);

    [Fact]
    public async Task Analyze_ThreadSleep_ReportsDT10004()
    {
        var test = Test("""
            using System.Threading;

            public sealed class CalculatorTests
            {
                public void Wait() => {|#0:Thread.Sleep(100)|};
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Thread.Sleep"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TaskDelayWithoutTimeProvider_ReportsDT10004()
    {
        var test = Test("""
            using System;
            using System.Threading.Tasks;

            public sealed class CalculatorTests
            {
                public Task WaitMilliseconds() => {|#0:Task.Delay(100)|};
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Task.Delay"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TaskDelayTimeSpanWithoutTimeProvider_ReportsDT10004()
    {
        var test = Test("""
            using System;
            using System.Threading.Tasks;

            public sealed class CalculatorTests
            {
                public Task WaitTimeSpan() => {|#0:Task.Delay(TimeSpan.FromSeconds(1))|};
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Task.Delay"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // The TimeProvider overload is the escape hatch the rule points at: a FakeTimeProvider drives
    // it from the test, so there is no wall clock involved.
    [Fact]
    public async Task Analyze_TaskDelayWithTimeProvider_ReportsNothing()
    {
        var test = Test("""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class CalculatorTests
            {
                public Task Wait(TimeProvider timeProvider)
                    => Task.Delay(TimeSpan.FromSeconds(1), timeProvider, CancellationToken.None);
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // 'Sleep' and 'Delay' are ordinary identifiers. Only the resolved symbol decides.
    [Fact]
    public async Task Analyze_UnrelatedMethodsOfTheSameName_ReportNothing()
    {
        var test = Test("""
            using System.Threading.Tasks;

            public static class Robot
            {
                public static void Sleep(int ms) { }
                public static Task Delay(int ms) => Task.CompletedTask;
            }

            public sealed class CalculatorTests
            {
                public void Wait()
                {
                    Robot.Sleep(100);
                    _ = Robot.Delay(100);
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AwaitedTaskDelay_ReportsDT10004()
    {
        var test = Test("""
            using System;
            using System.Threading.Tasks;

            public sealed class CalculatorTests
            {
                public async Task Wait() => await {|#0:Task.Delay(100)|};
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Task.Delay"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
