using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace Identity.WebApi;

public static class OpenApiConfiguration
{
    public static void TransformDocument(OpenApiDocument document)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Identity API",
            Version = "v1",
            Description = "OAuth2/OpenID Connect token endpoint. Served at https://auth.coreapp.local:8443 (or equivalent)."
        };

        if (document.Servers is { Count: > 0 })
        {
            foreach (var server in document.Servers)
            {
                if (!string.IsNullOrEmpty(server.Url) &&
                    server.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    server.Url = "https://" + server.Url.Substring(7);
            }
        }
        
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
        {
            {
                "OAuth2",
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        Password = new OpenApiOAuthFlow
                        {
                            TokenUrl = new Uri("/connect/token", UriKind.Relative),
                            Scopes = new Dictionary<string, string>
                            {
                                { "openid", "OpenID Connect" },
                                { "email", "Email" },
                                { "profile", "Profile" },
                                { "roles", "Roles" }
                            }
                        }
                    }
                }
            }
        };

        document.Paths ??= new OpenApiPaths();

        document.Paths["/connect/token"] = new OpenApiPathItem
        {
            Operations = new Dictionary<OperationType, OpenApiOperation>
            {
                {
                    OperationType.Post,
                    new OpenApiOperation
                    {
                        Summary = "OAuth2 Token Endpoint",
                        Description = "Obtain OAuth2 access token. Send 'id' and 'verificationCode' to complete an e-mail verification, or use grant_type=password / grant_type=refresh_token directly. The 'auth_request' grant is internal-only and cannot be invoked externally: the verification middleware produces it and passes the single-use ticket to the token endpoint as server-side request state, never as a form field, so a request that sends grant_type=auth_request itself is rejected with invalid_grant.",
                        Tags = new List<OpenApiTag> { new() { Name = "Authentication" } },
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                {
                                    "application/x-www-form-urlencoded",
                                    new OpenApiMediaType
                                    {
                                        Schema = new OpenApiSchema
                                        {
                                            Type = "object",
                                            Required = new HashSet<string> { "grant_type" },
                                            Properties = new Dictionary<string, OpenApiSchema>
                                            {
                                                {
                                                    "grant_type",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Grant type. Supported values: 'password' or 'refresh_token'. Omit it when sending 'id' and 'verificationCode': the verification middleware rewrites the request as the internal 'auth_request' grant, which cannot be requested directly.",
                                                        Enum = new List<IOpenApiAny>
                                                        {
                                                            new OpenApiString("password"),
                                                            new OpenApiString("refresh_token")
                                                        }
                                                    }
                                                },
                                                {
                                                    "scope",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Requested scopes"
                                                    }
                                                },
                                                {
                                                    "client_id",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Requested client Id"
                                                    }
                                                },
                                                {
                                                    "refresh_token",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Refresh token (required for grant_type=refresh_token)"
                                                    }
                                                },
                                                {
                                                    "verificationCode",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Email verification code for auth flow"
                                                    }
                                                },
                                                {
                                                    "id",
                                                    new OpenApiSchema
                                                    {
                                                        Type = "string",
                                                        Description = "Auth request id"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        Responses = new OpenApiResponses
                        {
                            ["200"] = new OpenApiResponse
                            {
                                Description = "Successful response with access token",
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    {
                                        "application/json",
                                        new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchema
                                            {
                                                Type = "object",
                                                Properties = new Dictionary<string, OpenApiSchema>
                                                {
                                                    {
                                                        "access_token",
                                                        new OpenApiSchema
                                                        {
                                                            Type = "string",
                                                            Description = "JWT access token"
                                                        }
                                                    },
                                                    {
                                                        "token_type",
                                                        new OpenApiSchema
                                                        {
                                                            Type = "string",
                                                            Description = "Token type (usually 'Bearer')"
                                                        }
                                                    },
                                                    {
                                                        "expires_in",
                                                        new OpenApiSchema
                                                        {
                                                            Type = "integer",
                                                            Description = "Token lifetime in seconds"
                                                        }
                                                    },
                                                    {
                                                        "refresh_token",
                                                        new OpenApiSchema
                                                        {
                                                            Type = "string",
                                                            Description = "Refresh token for renewing access token"
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            ["400"] = new OpenApiResponse
                            {
                                Description = "Bad request (invalid credentials or parameters)",
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/problem+json"] = new()
                                    {
                                        Schema = Rfc7807SchemaRef()
                                    }
                                }
                            },
                            ["500"] = new OpenApiResponse
                            {
                                Description = "Internal server error",
                                Content = new Dictionary<string, OpenApiMediaType>
                                {
                                    ["application/problem+json"] = new()
                                    {
                                        Schema = Rfc7807SchemaRef()
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        document.Components.Schemas ??= new Dictionary<string, OpenApiSchema>();
        document.Components.Schemas.TryAdd("Rfc7807ProblemDetails", new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                { "type", new OpenApiSchema { Type = "string", Nullable = true } },
                { "title", new OpenApiSchema { Type = "string", Nullable = true } },
                { "status", new OpenApiSchema { Type = "integer", Format = "int32", Nullable = true } },
                { "detail", new OpenApiSchema { Type = "string", Nullable = true } },
                { "instance", new OpenApiSchema { Type = "string", Nullable = true } },
                {
                    "errors", new OpenApiSchema
                    {
                        Type = "object",
                        Nullable = true,
                        AdditionalProperties = new OpenApiSchema
                        {
                            Type = "array",
                            Items = new OpenApiSchema { Type = "string" }
                        }
                    }
                },
                {
                    "extensions", new OpenApiSchema
                    {
                        Type = "object",
                        Nullable = true,
                        AdditionalProperties = new OpenApiSchema()
                    }
                }
            }
        });

        foreach (var pathItem in document.Paths.Values)
        foreach (var operation in pathItem.Operations.Values)
        {
            operation.Responses.TryAdd("500", new OpenApiResponse
            {
                Description = "Internal server error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new()
                    {
                        Schema = Rfc7807SchemaRef()
                    }
                }
            });
        }
    }

    private static OpenApiSchema Rfc7807SchemaRef() => new()
    {
        Reference = new OpenApiReference
        {
            Type = ReferenceType.Schema,
            Id = "Rfc7807ProblemDetails"
        }
    };
}
