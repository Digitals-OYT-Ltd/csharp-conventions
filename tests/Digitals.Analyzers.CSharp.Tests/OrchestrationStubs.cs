namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// Collaborators for the orchestration tests. A method is orchestration when it calls two or more
/// of its type's collaborators, so a handler here holds an <c>IOrderStore</c> and an <c>INotifier</c>.
/// No framework type is involved.
/// </summary>
internal static class OrchestrationStubs
{
    public const string Source = """
        namespace Acme.Ports
        {
            public interface IOrderStore
            {
                System.Threading.Tasks.Task SaveAsync(string order);
            }

            public interface INotifier
            {
                System.Threading.Tasks.Task NotifyAsync(string message);
            }

            public static class NotifierExtensions
            {
                public static System.Threading.Tasks.Task ShoutAsync(this INotifier notifier, string message) =>
                    notifier.NotifyAsync(message.ToUpperInvariant());
            }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger
            {
                void LogInformation(string message);
            }
        }

        namespace Acme.Jobs
        {
            public abstract class JobProcessor<T>
            {
                protected abstract System.Threading.Tasks.Task ProcessAsync(T payload);
            }
        }
        """;

    /// <summary>The two collaborator fields every orchestration test type holds.</summary>
    public const string Collaborators = """
                private readonly Acme.Ports.IOrderStore store = null!;
                private readonly Acme.Ports.INotifier notifier = null!;
        """;

    /// <summary><paramref name="count"/> lines of 'Use();', indented for a method body.</summary>
    public static string Statements(int count) =>
        string.Join("\n", Enumerable.Range(0, count).Select(_ => "            Use();"));
}
