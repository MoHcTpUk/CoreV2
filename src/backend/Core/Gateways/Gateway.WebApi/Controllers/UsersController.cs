using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Users.Contracts.Commands.AuthRequest;
using Users.Contracts.Dtos;
using Users.Api.Services;
using Shared.Api.RateLimiting;

namespace Gateway.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UsersController(IUserService userService)
    : ControllerBase
{
    [HttpPost(nameof(CreateAuthRequest))]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthRequestPolicy)]
    [Produces("application/json", "application/problem+json")]
    public async Task<ActionResult<AuthRequestDto>> CreateAuthRequest(
        CreateAuthRequestCommand requestCommand)
    {
        var result = await userService.CreateAuthRequest(requestCommand);
        return result;
    }
}