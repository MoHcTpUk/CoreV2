using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Contracts.Queries;
using Shared.Api.Services;

namespace Sample.Api.Services;

public interface ISampleService
    : IService<CreateSampleCommand, UpdateSampleCommand, DeleteSampleCommand, SampleQuery, SampleDto>;
