using AutoMapper;
using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Domain;
using Sample.Domain.Aggregates;
using Shared.Application.Handlers;

namespace Sample.Application.Handlers;

public class CreateSampleHandler(ISampleRepository repository, IMapper mapper)
    : CreateAggregateHandler<SampleAggregate, SampleDto, CreateSampleCommand>(repository, mapper);
