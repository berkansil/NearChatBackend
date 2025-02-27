using System;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NearChat;

namespace NearChatBackend
{
    [JsonSerializable(typeof(AuthenticationRequest))]
    [JsonSerializable(typeof(AuthenticationResponse))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(GetUserDetailsRequest))]
    internal partial class APIJsonContext : JsonSerializerContext { }

    //Register and login requests
    class AuthenticationRequest
    {
        public required string username { get; set; }
        public required string password { get; set; }
    }

    struct AuthenticationResponse
    {
        public required string token { get; init; }
    }

    //Get user details
    struct GetUserDetailsRequest
    {
        public int? userid { get; set; }
    }


    //Generic error class
    public class ErrorResponse
    {
        public required string error { get; set; }
        public string? message { get; set; }
    }

    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context); // Continue processing the request
            }
            catch (ArgumentException e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

                await context.Response.WriteAsJsonAsync<ErrorResponse>(
                    new ErrorResponse { error = "InvalidArgument", message = e.Message },
                    APIJsonContext.Default.ErrorResponse,
                    "application/json",
                    CancellationToken.None
                );
            }
            catch (AuthenticationException e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await context.Response.WriteAsJsonAsync<ErrorResponse>(
                    new ErrorResponse { error = "AuthenticationError", message = e.Message },
                    APIJsonContext.Default.ErrorResponse,
                    "application/json",
                    CancellationToken.None
                );
            }
            catch (Exception e)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsJsonAsync<ErrorResponse>(
                    new ErrorResponse { error = "ServerError", message = e.Message },
                    APIJsonContext.Default.ErrorResponse,
                    "application/json",
                    CancellationToken.None
                );
            }
        }

    }

    internal class RestAPI
    {
        private readonly string _url;
        private readonly Database _db;

        public RestAPI(string url, Database db)
        {
            _url = url;
            _db = db;
        }

        public void Start()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(_url);

            // Remove all logging providers
            builder.Logging.ClearProviders();

            // Optionally, add a filtered console logger
            builder.Logging.AddFilter("Microsoft", LogLevel.None);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);

            WebApplication app = builder.Build();

            //Configure error handler
            app.UseMiddleware<GlobalExceptionHandlerMiddleware>();


            // Configure routes
            app.MapGet("/", () => "NearChat REST API");

            app.MapGet("/throw", (HttpContext context) =>
            {
                throw new AuthenticationException("test");
            });

            app.MapPost("/register", async (HttpContext context) =>
            {
                AuthenticationRequest? requestBody = await context.Request.ReadFromJsonAsync(
                    APIJsonContext.Default.AuthenticationRequest
                );
                if (requestBody == null)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await context.Response.WriteAsync("Malformed request body");
                    return;
                }

                string token = _db.RegisterUser(requestBody.username, requestBody.password);
                KeyValuePair<string, string> response = new KeyValuePair<string, string>("token", token);

                await context.Response.WriteAsJsonAsync<AuthenticationResponse>(
                    new AuthenticationResponse { token = token },
                    APIJsonContext.Default.AuthenticationResponse,
                    "application/json",
                    CancellationToken.None
                );

            });

            app.MapPost("/queryUser", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authorisation required for endpoint");
                }

                GetUserDetailsRequest requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.GetUserDetailsRequest);

                User user = _db.GetUserDetails(token, requestBody.userid);

                await context.Response.WriteAsync(user.ToJson());
            });

            app.Run();
        }
    }
}
