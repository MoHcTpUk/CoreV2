using Sample.Contracts.Dtos;
using Shared.Api.Commands;
using Shared.Dtos;

namespace Sample.Contracts.Commands;

public class DeleteSampleCommand : DeleteCommand<ResultContainer<SampleDto>>;
