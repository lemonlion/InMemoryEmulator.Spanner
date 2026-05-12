using Google.Cloud.Spanner.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InMemoryEmulator.Spanner;

/// <summary>
/// Extension methods for registering the in-memory Spanner emulator with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Replaces all Spanner registrations with an in-memory fake.
	/// </summary>
	public static IServiceCollection AddInMemorySpanner(
		this IServiceCollection services,
		Action<InMemorySpannerOptions>? configure = null)
	{
		var options = new InMemorySpannerOptions();
		configure?.Invoke(options);

		// Create the database and server
		var databaseOptions = new InMemorySpannerDatabaseOptions
		{
			StatePersistenceDirectory = options.StatePersistenceDirectory
		};
		var database = new InMemorySpannerDatabase(databaseOptions);
		options.OnDatabaseCreated?.Invoke(database);

		var serverOptions = new FakeSpannerServerOptions
		{
			ProjectId = options.ProjectId,
			InstanceId = options.InstanceId,
			DatabaseId = options.DatabaseId
		};
		var server = new FakeSpannerServer(database, serverOptions);
		options.OnServerCreated?.Invoke(server);
		server.Start();

		// Register singletons
		services.RemoveAll<SpannerConnection>();
		services.AddSingleton(database);
		services.AddSingleton(server);
		services.AddSingleton(server.Service);
		services.AddTransient(_ => server.CreateConnection());

		return services;
	}
}
