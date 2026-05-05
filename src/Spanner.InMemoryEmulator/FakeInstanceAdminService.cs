using Google.Cloud.Spanner.Admin.Instance.V1;
using Google.LongRunning;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// gRPC service implementation for InstanceAdmin.InstanceAdminBase.
/// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#instanceadmin
/// </summary>
public class FakeInstanceAdminService : InstanceAdmin.InstanceAdminBase
{
	private readonly FakeSpannerServerOptions _options;
	private readonly string _instanceName;
	private readonly string _projectName;

	public FakeInstanceAdminService(FakeSpannerServerOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_instanceName = $"projects/{options.ProjectId}/instances/{options.InstanceId}";
		_projectName = $"projects/{options.ProjectId}";
	}

	private Instance BuildInstanceProto() => new()
	{
		Name = _instanceName,
		Config = $"projects/{_options.ProjectId}/instanceConfigs/emulator-config",
		DisplayName = _options.InstanceId,
		NodeCount = 1,
		State = Instance.Types.State.Ready,
		CreateTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
	};

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.CreateInstance
	//   Creates an instance and begins preparing it to begin serving.
	public override Task<Operation> CreateInstance(CreateInstanceRequest request, ServerCallContext context)
	{
		var instance = BuildInstanceProto();

		var metadata = new CreateInstanceMetadata
		{
			Instance = instance,
			StartTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
			EndTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
		};

		var operation = new Operation
		{
			Name = $"{_instanceName}/operations/create",
			Done = true,
			Response = Any.Pack(instance),
			Metadata = Any.Pack(metadata),
		};

		return Task.FromResult(operation);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.GetInstance
	//   Gets information about a particular instance.
	public override Task<Instance> GetInstance(GetInstanceRequest request, ServerCallContext context)
	{
		if (request.Name != _instanceName)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Instance not found: {request.Name}"));
		}

		return Task.FromResult(BuildInstanceProto());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.ListInstances
	//   Lists all instances in the given project.
	public override Task<ListInstancesResponse> ListInstances(ListInstancesRequest request, ServerCallContext context)
	{
		var response = new ListInstancesResponse();

		if (request.Parent == _projectName)
		{
			response.Instances.Add(BuildInstanceProto());
		}

		return Task.FromResult(response);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.UpdateInstance
	//   Updates an instance. Returns a long-running operation.
	//   "Returns NOT_FOUND if the instance does not exist."
	public override Task<Operation> UpdateInstance(UpdateInstanceRequest request, ServerCallContext context)
	{
		if (request.Instance?.Name != _instanceName)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Instance not found: {request.Instance?.Name}"));
		}

		var instance = BuildInstanceProto();

		var metadata = new UpdateInstanceMetadata
		{
			Instance = instance,
			StartTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
			EndTime = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
		};

		var operation = new Operation
		{
			Name = $"{_instanceName}/operations/update",
			Done = true,
			Response = Any.Pack(instance),
			Metadata = Any.Pack(metadata),
		};

		return Task.FromResult(operation);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.DeleteInstance
	//   Deletes an instance. "Returns NOT_FOUND if the instance does not exist."
	public override Task<Empty> DeleteInstance(DeleteInstanceRequest request, ServerCallContext context)
	{
		if (request.Name != _instanceName)
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Instance not found: {request.Name}"));
		}

		// In the single-instance emulator, we don't actually delete the instance.
		// Just acknowledge the request.
		return Task.FromResult(new Empty());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.GetInstanceConfig
	//   "Gets information about a particular instance configuration."
	public override Task<InstanceConfig> GetInstanceConfig(GetInstanceConfigRequest request, ServerCallContext context)
	{
		var expectedConfigName = $"projects/{_options.ProjectId}/instanceConfigs/emulator-config";

		// Accept any config name under this project — the emulator returns a synthetic config.
		if (!request.Name.StartsWith($"projects/{_options.ProjectId}/instanceConfigs/"))
		{
			throw new RpcException(new Status(StatusCode.NotFound, $"Instance config not found: {request.Name}"));
		}

		var config = new InstanceConfig
		{
			Name = request.Name,
			DisplayName = request.Name.Split('/').Last(),
		};
		config.Replicas.Add(new ReplicaInfo
		{
			Location = "us-central1",
			Type = ReplicaInfo.Types.ReplicaType.ReadWrite,
			DefaultLeaderLocation = true,
		});

		return Task.FromResult(config);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/rpc/google.spanner.admin.instance.v1#google.spanner.admin.instance.v1.InstanceAdmin.ListInstanceConfigs
	//   "Lists the supported instance configurations for a given project."
	public override Task<ListInstanceConfigsResponse> ListInstanceConfigs(ListInstanceConfigsRequest request, ServerCallContext context)
	{
		var response = new ListInstanceConfigsResponse();

		if (request.Parent == _projectName)
		{
			var config = new InstanceConfig
			{
				Name = $"projects/{_options.ProjectId}/instanceConfigs/emulator-config",
				DisplayName = "Emulator Config",
			};
			config.Replicas.Add(new ReplicaInfo
			{
				Location = "us-central1",
				Type = ReplicaInfo.Types.ReplicaType.ReadWrite,
				DefaultLeaderLocation = true,
			});
			response.InstanceConfigs.Add(config);
		}

		return Task.FromResult(response);
	}
}
