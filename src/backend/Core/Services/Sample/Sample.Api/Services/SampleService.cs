using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Contracts.Queries;
using Shared.Api.Services;
using Shared.Messaging.Messaging;

namespace Sample.Api.Services;

public class SampleService(ICommandBus commandBus)
    : Service<CreateSampleCommand, UpdateSampleCommand, DeleteSampleCommand, SampleQuery, SampleDto>(commandBus),
        ISampleService
{
    protected override string Subject => MessagingSubject.SampleService;
}
