using System.Net;
using Google.Cloud.Spanner.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace InMemoryEmulator.Spanner;

/// <summary>
/// In-process gRPC server that implements the Spanner and DatabaseAdmin services.
/// Real <see cref="SpannerConnection"/> instances can connect to this server.
/// </summary>
public class FakeSpannerServer : IDisposable, IAsyncDisposable
{
	private readonly InMemorySpannerDatabase _database;
	private readonly FakeSpannerServerOptions _options;
	private readonly FakeSpannerService _service;
	private readonly FakeDatabaseAdminService _databaseAdminService;
	private readonly FakeInstanceAdminService _instanceAdminService;
	private WebApplication? _app;
	private int _port;

	public FakeSpannerServer()
		: this(new InMemorySpannerDatabase(), new FakeSpannerServerOptions())
	{
	}

	public FakeSpannerServer(InMemorySpannerDatabase database)
		: this(database, new FakeSpannerServerOptions())
	{
	}

	public FakeSpannerServer(FakeSpannerServerOptions options)
		: this(new InMemorySpannerDatabase(), options)
	{
	}

	public FakeSpannerServer(InMemorySpannerDatabase database, FakeSpannerServerOptions options)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_service = new FakeSpannerService(_database, _options);
		_databaseAdminService = new FakeDatabaseAdminService(_database, _options);
		_instanceAdminService = new FakeInstanceAdminService(_options);
	}

	/// <summary>The bound port after <see cref="Start"/> is called.</summary>
	public int Port => _port;

	/// <summary>The backing in-memory database.</summary>
	public InMemorySpannerDatabase Database => _database;

	/// <summary>The gRPC service instance, for fault injection and request logging.</summary>
	public FakeSpannerService Service => _service;

	/// <summary>The Database Admin gRPC service instance.</summary>
	public FakeDatabaseAdminService DatabaseAdminService => _databaseAdminService;

	/// <summary>The Instance Admin gRPC service instance.</summary>
	public FakeInstanceAdminService InstanceAdminService => _instanceAdminService;

	/// <summary>
	/// Spanner connection string pointing at this server.
	/// </summary>
	public string ConnectionString =>
		$"Data Source=projects/{_options.ProjectId}/instances/{_options.InstanceId}/databases/{_options.DatabaseId};" +
		$"Host=localhost;Port={_port};EmulatorDetection=EmulatorOnly";

	/// <summary>
	/// Optional callback invoked when a gRPC request is received.
	/// Shorthand for <c>Service.OnRequestReceived</c>.
	/// </summary>
	public Action<SpannerRequestEvent>? OnRequestReceived
	{
		get => Service.OnRequestReceived;
		set => Service.OnRequestReceived = value;
	}

	/// <summary>
	/// Optional callback invoked after a gRPC request has been executed.
	/// Shorthand for <c>Service.OnResponseSent</c>.
	/// </summary>
	public Action<SpannerResponseEvent>? OnResponseSent
	{
		get => Service.OnResponseSent;
		set => Service.OnResponseSent = value;
	}

	/// <summary>Starts the gRPC server synchronously.</summary>
	public void Start()
	{
		StartAsync().GetAwaiter().GetResult();
	}

	/// <summary>Starts the gRPC server asynchronously.</summary>
	public async Task StartAsync()
	{
		var builder = WebApplication.CreateBuilder();

		builder.WebHost.ConfigureKestrel(options =>
		{
			options.Listen(IPAddress.Loopback, 0, listenOptions =>
			{
				listenOptions.Protocols = HttpProtocols.Http2;
			});
		});

		builder.Services.AddGrpc();
		builder.Services.AddSingleton(_service);
		builder.Services.AddSingleton(_databaseAdminService);
		builder.Services.AddSingleton(_instanceAdminService);

		_app = builder.Build();
		_app.MapGrpcService<FakeSpannerService>();
		_app.MapGrpcService<FakeDatabaseAdminService>();
		_app.MapGrpcService<FakeInstanceAdminService>();

		await _app.StartAsync();

		// Extract the actual bound port
		var addresses = _app.Urls;
		foreach (var address in addresses)
		{
			if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
			{
				_port = uri.Port;
				break;
			}
		}

		// If port extraction from Urls failed, try the server addresses feature
		if (_port == 0)
		{
			var serverAddresses = _app.Services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>();
			if (serverAddresses != null)
			{
				var addressFeature = serverAddresses.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
				if (addressFeature != null)
				{
					foreach (var address in addressFeature.Addresses)
					{
						if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
						{
							_port = uri.Port;
							break;
						}
					}
				}
			}
		}
	}

	/// <summary>Stops the gRPC server.</summary>
	public void Stop()
	{
		StopAsync().GetAwaiter().GetResult();
	}

	/// <summary>Stops the gRPC server asynchronously.</summary>
	public async Task StopAsync()
	{
		if (_app != null)
		{
			await _app.StopAsync();
		}
	}

	/// <summary>
	/// Creates a real <see cref="SpannerConnection"/> pointing at this fake server.
	/// Requires <c>SPANNER_EMULATOR_HOST</c> environment variable to be set to <c>localhost:{Port}</c>.
	/// </summary>
	public SpannerConnection CreateConnection()
	{
		// Ref: https://cloud.google.com/spanner/docs/emulator#using_the_emulator
		//   "Set SPANNER_EMULATOR_HOST to localhost:<port>. The SDK connects via this env var
		//    when EmulatorDetection is EmulatorOnly."
		Environment.SetEnvironmentVariable("SPANNER_EMULATOR_HOST", $"localhost:{_port}");

		var connectionStringBuilder = new SpannerConnectionStringBuilder
		{
			DataSource = $"projects/{_options.ProjectId}/instances/{_options.InstanceId}/databases/{_options.DatabaseId}",
			EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly
		};

		return new SpannerConnection(connectionStringBuilder);
	}

	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
		GC.SuppressFinalize(this);
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		_database.Dispose();
		if (_app != null)
		{
			await _app.DisposeAsync();
		}
		GC.SuppressFinalize(this);
	}
}
