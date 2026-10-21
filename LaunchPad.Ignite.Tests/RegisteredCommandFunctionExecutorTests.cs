using System.Net;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class RegisteredCommandFunctionExecutorTests
{
    [Fact]
    public async Task HttpGetFunctionEscapesParametersAndSelectsJsonValue()
    {
        var handler = new StubHttpMessageHandler("{\"users\":[{\"name\":\"Ada\"}]}");
        using var client = new HttpClient(handler);
        var executor = new RegisteredCommandFunctionExecutor(client);
        var function = new CommandFunctionDefinition(
            "userName",
            "httpGet",
            [new("id")],
            "https://example.test/users/<id>",
            "$.users[0].name",
            Namespace: "demo");

        var result = await executor.ExecuteAsync(function, ["user/42"], CancellationToken.None);

        Assert.Equal("Ada", result);
        Assert.Contains("user%2F42", Assert.Single(handler.RequestUris).AbsoluteUri);
    }

    [Fact]
    public async Task HttpGetFunctionLeavesOmittedOptionalPlaceholderEmpty()
    {
        var handler = new StubHttpMessageHandler("value");
        using var client = new HttpClient(handler);
        var executor = new RegisteredCommandFunctionExecutor(client);
        var function = new CommandFunctionDefinition(
            "optionalPath",
            "httpGet",
            [new("required"), new("optional", Optional: true)],
            "https://example.test/<required>/<optional>");

        await executor.ExecuteAsync(function, ["present"], CancellationToken.None);

        Assert.EndsWith("/present/", Assert.Single(handler.RequestUris).AbsolutePath);
    }

    [Fact]
    public async Task HttpGetFunctionReturnsFullBodyWithoutJsonSelector()
    {
        const string body = "value from api";
        using var client = new HttpClient(new StubHttpMessageHandler(body));
        var executor = new RegisteredCommandFunctionExecutor(client);
        var function = new CommandFunctionDefinition("body", "httpGet", [], "https://example.test/value");

        var result = await executor.ExecuteAsync(function, [], CancellationToken.None);

        Assert.Equal(body, result);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _getResponseBody;

        public StubHttpMessageHandler(string responseBody)
            : this(_ => responseBody)
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, string> getResponseBody)
        {
            _getResponseBody = getResponseBody;
        }

        public List<Uri> RequestUris { get; } = [];
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_getResponseBody(request))
            });
        }
    }
}
