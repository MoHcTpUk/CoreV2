using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sample.Api.Services;
using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Contracts.Queries;
using Shared.Api;
using Shared.Dtos;

namespace Gateway.Api.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
public class SamplesController(ISampleService service)
    : AbstractController<ISampleService, ResultContainer<SampleDto>, CreateSampleCommand, UpdateSampleCommand,
        DeleteSampleCommand, SampleQuery, SampleDto>(service)
{
    [HttpPost(nameof(Create))]
    public new Task<ResultContainer<SampleDto>> Create([FromBody] CreateSampleCommand command)
    {
        return base.Create(command);
    }

    [HttpPut(nameof(Update))]
    public new Task<ResultContainer<SampleDto>> Update([FromBody] UpdateSampleCommand command)
    {
        return base.Update(command);
    }

    [HttpDelete(nameof(Delete))]
    public new Task<ResultContainer<SampleDto>> Delete([FromBody] DeleteSampleCommand command)
    {
        return base.Delete(command);
    }

    [HttpGet(nameof(Get))]
    public new Task<ResultContainer<SampleDto>> Get([FromQuery] SampleQuery query)
    {
        return base.Get(query);
    }
}
