using AutoMapper;
using Sample.Contracts.Commands;
using Sample.Contracts.Dtos;
using Sample.Domain;
using Sample.Domain.Aggregates;
using Shared.Application.Handlers;

namespace Sample.Application.Handlers;

public class UpdateSampleHandler(ISampleRepository repository, IMapper mapper)
    : UpdateAggregateHandler<SampleAggregate, SampleDto, UpdateSampleCommand>(repository, mapper);
