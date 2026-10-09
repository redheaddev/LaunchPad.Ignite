using System.Net;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;
using LaunchPad.Ignite.Extensions.Weather;

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

    [Theory]
    [InlineData("72.9", "72")]
    [InlineData("-3.9", "-3")]
    public async Task WeatherFunctionGeocodesExactZipAndReturnsTruncatedTemperature(string temperature, string expected)
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.Host.StartsWith("geocoding-api.", StringComparison.Ordinal)
                ? "{\"results\":[{\"name\":\"Mount Vernon\",\"latitude\":37.1,\"longitude\":-93.8,\"postcodes\":[\"65738\"]}]}"
                : $"{{\"current\":{{\"temperature_2m\":{temperature}}}}}");
        using var client = new HttpClient(handler);
        var executor = new RegisteredCommandFunctionExecutor(
            client,
            new Dictionary<string, ICommandFunctionExecutor>(StringComparer.OrdinalIgnoreCase)
            {
                ["weather:temp"] = new OpenMeteoWeatherFunctionExecutor(client)
            });
        var function = new CommandFunctionDefinition("temp", null, [new("zip")], Namespace: "weather");

        var result = await executor.ExecuteAsync(function, ["65738"], CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains("countryCode=US", handler.RequestUris[0].Query);
        Assert.Contains("temperature_unit=fahrenheit", handler.RequestUris[1].Query);
    }

    [Fact]
    public async Task WeatherFunctionRejectsNonFiveDigitZipWithoutCallingService()
    {
        var handler = new StubHttpMessageHandler("{}");
        using var client = new HttpClient(handler);
        var executor = new RegisteredCommandFunctionExecutor(
            client,
            new Dictionary<string, ICommandFunctionExecutor>(StringComparer.OrdinalIgnoreCase)
            {
                ["weather:temp"] = new OpenMeteoWeatherFunctionExecutor(client)
            });
        var function = new CommandFunctionDefinition("temp", null, [new("zip")], Namespace: "weather");

        await Assert.ThrowsAsync<FormatException>(() =>
            executor.ExecuteAsync(function, ["6573"], CancellationToken.None));

        Assert.Empty(handler.RequestUris);
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_getResponseBody(request))
            });
        }
    }
}
