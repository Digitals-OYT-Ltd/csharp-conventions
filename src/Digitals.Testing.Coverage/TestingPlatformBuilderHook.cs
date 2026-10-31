using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Services;

namespace Digitals.Testing.Coverage;

/// <summary>
/// Entry point the Microsoft.Testing.Platform source generator calls to register the coverage gate.
/// </summary>
/// <remarks>
/// Wired up by the <c>TestingPlatformBuilderHook</c> item in <c>build/Digitals.Testing.props</c>.
/// The generator emits a call to this exact type name into the consumer's
/// <c>SelfRegisteredExtensions.cs</c>, so the namespace and type name are part of the contract:
/// renaming either breaks every consumer's build with an unresolved symbol in generated code.
/// </remarks>
public static class TestingPlatformBuilderHook
{
    /// <summary>Registers the coverage gate with the test application builder.</summary>
    /// <param name="builder">The builder supplied by the platform.</param>
    /// <param name="arguments">Raw command line arguments. Unused; options are read from the platform.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static void AddExtensions(ITestApplicationBuilder builder, string[] arguments)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.CommandLine.AddProvider(static () => new CoverageGateCommandLineProvider());

        // One instance in two roles. The data consumer collects the artifact paths during the run
        // and the lifetime handler reads them afterwards, so they have to be the same object.
        CoverageGate? gate = null;
        CoverageGate Resolve(IServiceProvider services)
            => gate ??= new CoverageGate(services.GetCommandLineOptions(), services.GetOutputDevice());

        builder.TestHost.AddDataConsumer(Resolve);
        builder.TestHost.AddTestHostApplicationLifetime(Resolve);
    }
}
