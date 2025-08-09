using System;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
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
    [JsonSourceGenerationOptions()]
    [JsonSerializable(typeof(AuthenticationRequest))]
    [JsonSerializable(typeof(AuthenticationResponse))]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(GetUserDetailsRequest))]
    [JsonSerializable(typeof(GetMessagesRequest))]
    [JsonSerializable(typeof(SendMessageRequest))]
    [JsonSerializable(typeof(DeleteMessageRequest))]
    [JsonSerializable(typeof(GPSUpdateRequest))]
    [JsonSerializable(typeof(UpdateUserDetailsRequest))]
    internal partial class APIJsonContext : JsonSerializerContext { }

    //Update user details
    class UpdateUserDetailsRequest
    {
        public string? about { get; set; }
        public string? profilePicture { get; set; }
    }

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
        public int? userId { get; set; }
    }

    //Get messages
    class GetMessagesRequest
    {
        public int channelId { get; set; }
        public int? lastMessageId { get; set; }
    }

    //Send message
    class SendMessageRequest
    {
        public required string content { get; set; }
        public required int channelId { get; set; }
        public int? parentid { get; set; }
    }

    //Delete message
    struct DeleteMessageRequest
    {
        public required int messageId { get; set; }
    }

    //Update GPS location
    class GPSUpdateRequest
    {
        public required double lat { get; set; }
        public required double lon { get; set; }
    }

    //Get channels response
    struct GetChannelsResponse
    {
        public List<Channel> channels { get; set; }
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
        private readonly ConnectionManager _websocketManager;

        public RestAPI(string url, Database db, ConnectionManager websocketManager)
        {
            _url = url;
            _db = db;
            _websocketManager = websocketManager;
        }
        public void Start()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(_url);

            //Add https
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(443, listenOptions =>
                {
                    listenOptions.UseHttps("nearchat.duckdns.org.pfx");
                });
            });

            // Remove all logging providers
            builder.Logging.ClearProviders();

            // Optionally, add a filtered console logger
            builder.Logging.AddFilter("Microsoft", LogLevel.None);
            builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);

            WebApplication app = builder.Build();

            //Configure error handler
            app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

            app.UseWebSockets();


            app.Map("/subscribe", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                int? userId = _db.GetUserIdFromToken(token);
                if (userId == null)
                {
                    throw new AuthenticationException("Invalid token");
                }

                string? channelIdString = context.Request.Query["channelId"];


                int? channelId = Convert.ToInt32(channelIdString);
                if (channelId == null)
                {
                    throw new ArgumentException("Invalid channel id");
                }

                if (!_db.CanUserAccessChannel(userId.Value, channelId.Value))
                {
                    throw new SecurityException("Cannot access channel");
                }

                if (context.WebSockets.IsWebSocketRequest)
                {
                    WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

                    var socketFinishedTcs = new TaskCompletionSource<object>();

                    UserHandler handler = new(socket, channelId.Value, userId.Value, _websocketManager, socketFinishedTcs);
                    //await handler.SendMessage(new Message(1, "test", DateTime.Now, 1, null, 1));

                    await socketFinishedTcs.Task;
                }
            });

            // Configure routes
            app.MapGet("/", () => "NearChat REST API");

            app.MapPost("/register", async (HttpContext context) =>
            {
                AuthenticationRequest? requestBody = await context.Request.ReadFromJsonAsync(
                    APIJsonContext.Default.AuthenticationRequest
                );
                if (requestBody == null)
                {
                    throw new ArgumentException("Malformed request body");
                }

                string token = _db.RegisterUser(requestBody.username, requestBody.password);

                JsonSerializerOptions jso = new JsonSerializerOptions
                {
                    // Use UnsafeRelaxedJsonEscaping to prevent escaping of Base64 characters
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

                    // Use the TypeInfoResolver from the source-generated context
                    TypeInfoResolver = APIJsonContext.Default.AuthenticationResponse.Options.TypeInfoResolver
                };

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
#pragma warning disable IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.
                await context.Response.WriteAsJsonAsync<AuthenticationResponse>(
                        new AuthenticationResponse { token = token },
                        jso,
                        "application/json",
                        CancellationToken.None
                    );
#pragma warning restore IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code


            });

            app.MapPost("/updateUser", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                UpdateUserDetailsRequest? requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.UpdateUserDetailsRequest);

                if (requestBody?.about != null)
                {
                    _db.UpdateAbout(token, requestBody.about);
                }

                if (requestBody?.profilePicture != null)
                {
                    _db.UpdateProfilePicture(token, requestBody.profilePicture);
                }
            });

            app.MapPost("/queryUser", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                GetUserDetailsRequest requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.GetUserDetailsRequest);

                User user = _db.GetUserDetails(token, requestBody.userId);

                await context.Response.WriteAsync(user.ToJson());
            });

            app.MapPost("/login", async (HttpContext context) =>
            {
                AuthenticationRequest? requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.AuthenticationRequest) ?? throw new ArgumentException("Malformed request body");

                string token = _db.AuthenticateUser(requestBody.username, requestBody.password);

                JsonSerializerOptions jso = new JsonSerializerOptions
                {
                    // Use UnsafeRelaxedJsonEscaping to prevent escaping of Base64 characters
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

                    // Use the TypeInfoResolver from the source-generated context
                    TypeInfoResolver = APIJsonContext.Default.AuthenticationResponse.Options.TypeInfoResolver
                };

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
#pragma warning disable IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.
                await context.Response.WriteAsJsonAsync<AuthenticationResponse>(
                        new AuthenticationResponse { token = token },
                        jso,
                        "application/json",
                        CancellationToken.None
                    );
#pragma warning restore IL3050 // Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.
#pragma warning restore IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code
            });

            app.MapPost("/logout", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                if (!_db.Logout(token))
                {
                    throw new ArgumentException("Invalid token");
                }
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await context.Response.CompleteAsync();
                return;
            });

            app.MapPost("/getMessages", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                GetMessagesRequest? requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.GetMessagesRequest);
                if (requestBody == null)
                {
                    throw new ArgumentException("Malformed request body");
                }

                List<Message> messages = _db.GetMessages(token, requestBody.channelId, requestBody.lastMessageId);

                await context.Response.WriteAsJsonAsync<List<Message>>(
                    messages,
                    MessageSerialisation.Default.ListMessage,
                    "application/json",
                    CancellationToken.None);
            });

            app.MapPost("/deleteMessage", async (HttpContext context) => {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                DeleteMessageRequest requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.DeleteMessageRequest);

                _db.DeleteMessage(token, requestBody.messageId);
            });

            app.MapPost("/sendMessage", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                SendMessageRequest? requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.SendMessageRequest);
                if (requestBody == null)
                {
                    throw new ArgumentException("Malformed request body");
                }

                _db.SendMessage(token, requestBody.content, requestBody.channelId, requestBody.parentid);
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await context.Response.CompleteAsync();
                return;
            });

            app.MapPost("/updateGPS", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                GPSUpdateRequest? requestBody = await context.Request.ReadFromJsonAsync(APIJsonContext.Default.GPSUpdateRequest);
                if (requestBody == null)
                {
                    throw new ArgumentException("Malformed request body");
                }

                Address? addr = await ReverseGeocoding.ReverseGeocode(requestBody.lat, requestBody.lon);

                if (addr == null)
                {
                    throw new ArgumentException("Invalid coordinates");
                }

                _db.UpdateUserChannels(token, addr);

                List<Channel> channels = _db.GetChannels(token);

                await context.Response.WriteAsJsonAsync<List<Channel>>(
                    channels,
                    ChannelSerialisation.Default.ListChannel,
                    "application/json",
                    CancellationToken.None);
            });

            app.MapGet("/getChannels", async (HttpContext context) =>
            {
                string? token = context.Request.Headers.Authorization;
                if (token == null)
                {
                    throw new AuthenticationException("Authentication required for endpoint");
                }

                List<Channel> channels = _db.GetChannels(token);


                await context.Response.WriteAsJsonAsync<List<Channel>>(
                    channels,
                    ChannelSerialisation.Default.ListChannel,
                    "application/json",
                    CancellationToken.None);
            });

            app.Run();
        }
    }
}
