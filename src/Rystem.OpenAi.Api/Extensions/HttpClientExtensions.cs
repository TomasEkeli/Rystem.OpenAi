﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rystem.OpenAi
{
    public static class HttpClientExtensions
    {
        private static readonly JsonSerializerOptions s_options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        private static async Task<HttpResponseMessage> PrivatedExecuteAsync(this HttpClient client,
            string url,
            HttpMethod method,
            object? message,
            bool isStreaming,
            CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(method, url);
            if (message != null)
            {
                if (message is HttpContent httpContent)
                {
                    request.Content = httpContent;
                }
                else
                {
                    var jsonContent = JsonSerializer.Serialize(message, s_options);
                    var stringContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    request.Content = stringContent;
                }
            }
            var response = await client.SendAsync(request, isStreaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }
            else
            {
                throw new HttpRequestException(await response.Content.ReadAsStringAsync());
            }
        }
        internal static async Task<HttpResponseMessage> ExecuteAsync(this HttpClient client,
            string url,
            HttpMethod method,
            object? message,
            bool isStreaming,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (configuration.NeedClientEnrichment)
                await configuration.EnrichClientAsync(client);
            return await PrivatedExecuteAsync(client, url, method, message, isStreaming, cancellationToken);
        }
        internal static ValueTask<TResponse> DeleteAsync<TResponse>(this HttpClient client,
            string url,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
            => ExecuteWithResponseAsync<TResponse>(client, url, null, HttpMethod.Delete, configuration, cancellationToken);
        internal static ValueTask<TResponse> GetAsync<TResponse>(this HttpClient client,
            string url,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
            => ExecuteWithResponseAsync<TResponse>(client, url, null, HttpMethod.Get, configuration, cancellationToken);
        internal static ValueTask<TResponse> PatchAsync<TResponse>(this HttpClient client,
            string url,
            object? message,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
            => ExecuteWithResponseAsync<TResponse>(client, url, message, HttpMethod.Patch, configuration, cancellationToken);
        internal static ValueTask<TResponse> PutAsync<TResponse>(this HttpClient client,
            string url,
            object? message,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
            => ExecuteWithResponseAsync<TResponse>(client, url, message, HttpMethod.Put, configuration, cancellationToken);
        internal static ValueTask<TResponse> PostAsync<TResponse>(this HttpClient client,
            string url,
            object? message,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
            => ExecuteWithResponseAsync<TResponse>(client, url, message, HttpMethod.Post, configuration, cancellationToken);
        internal static async ValueTask<TResponse> ExecuteWithResponseAsync<TResponse>(this HttpClient client,
            string url,
            object? message,
            HttpMethod method,
            OpenAiConfiguration configuration,
            CancellationToken cancellationToken)
        {
            if (configuration.NeedClientEnrichment)
                await configuration.EnrichClientAsync(client);
            var response = await client.PrivatedExecuteAsync(url, method, message, false, cancellationToken);
            var responseAsString = await response.Content.ReadAsStringAsync();
            return !string.IsNullOrWhiteSpace(responseAsString) ? JsonSerializer.Deserialize<TResponse>(responseAsString)! : default!;
        }
        private const string StartingWith = "data: ";
        private const string Done = "[DONE]";
        internal static async IAsyncEnumerable<TResponse> StreamAsync<TResponse>(this HttpClient client,
            string url,
            object? message,
            HttpMethod httpMethod,
            OpenAiConfiguration configuration,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (configuration.NeedClientEnrichment)
                await configuration.EnrichClientAsync(client);
            var response = await client.PrivatedExecuteAsync(url, httpMethod, message, true, cancellationToken);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string line;
            var buffer = new StringBuilder();
            var curlyCounter = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith(StartingWith))
                    line = line[StartingWith.Length..];
                if (line == Done)
                {
                    yield break;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var chunkResponse = default(TResponse);
                    buffer.AppendLine(line);
                    try
                    {
                        if (line.Equals("{"))
                            curlyCounter++;
                        if (line.Equals("}"))
                            curlyCounter--;
                        if (curlyCounter == 0)
                        {
                            var bufferAsString = buffer.ToString();
                            chunkResponse = JsonSerializer.Deserialize<TResponse>(bufferAsString);
                            if (chunkResponse is ApiBaseResponse apiResult)
                                apiResult.SetHeaders(response);
                            buffer.Clear();
                        }
                    }
                    catch
                    {
                        //not useful 
                    }
                    if (chunkResponse != null)
                        yield return chunkResponse!;

                }
            }
        }
        private static void SetHeaders<TResponse>(this TResponse result, HttpResponseMessage response)
            where TResponse : ApiBaseResponse
        {
            try
            {
                response.Headers.TryGetValues("Openai-Organization", out var organizations);
                if (organizations?.Any() == true)
                    result.Organization = organizations.First();
                response.Headers.TryGetValues("X-Request-ID", out var requestIds);
                if (requestIds?.Any() == true)
                    result.RequestId = requestIds.First();
                response.Headers.TryGetValues("Openai-Processing-Ms", out var processings);
                if (processings?.Any() == true)
                    result.ProcessingTime = TimeSpan.FromMilliseconds(double.Parse(processings.First()));
                response.Headers.TryGetValues("Openai-Version", out var versions);
                if (versions?.Any() == true)
                    result.OpenaiVersion = versions.First();
                response.Headers.TryGetValues("Openai-Model", out var models);
                if (models?.Any() == true)
                    result.ModelId = models.First();
            }
            catch (Exception e)
            {
                Debug.Print($"Issue parsing metadata of OpenAi Response.  Error: {e.Message}.  This is probably ignorable.");
            }
        }
    }
}
