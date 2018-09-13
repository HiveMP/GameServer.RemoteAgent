
//------------------------
// <auto-generated>
//     Generated with HiveMP SDK Generator
// </auto-generated>
//------------------------




#if UNITY_5 || UNITY_5_3_OR_NEWER
#define IS_UNITY
#endif
#if !(NET35 || (IS_UNITY && !NET_4_6))
#define HAS_TASKS
#endif
#if !NET35 && !IS_UNITY
#define HAS_HTTPCLIENT
#endif
#if IS_UNITY && NET_4_6 && UNITY_2017_1
#error Unity 2017.1 with a .NET 4.6 runtime is not supported due to known bugs in Unity (bit.ly/2xeicxY). Either upgrade to 2017.2 or use the .NET 2.0 runtime.
#endif


using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
#if HAS_HTTPCLIENT
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
#endif
using System.Threading;
#if HAS_TASKS
using System.Threading.Tasks;
#endif

namespace HiveMP.Api
{
    public class RetryableHttpClient : IDisposable
    {
#if HAS_HTTPCLIENT
        private readonly HttpClient _httpClient;
#endif

        static RetryableHttpClient()
        {
            HiveMP.Api.HiveMPSDK.EnsureInited();
        }

        public RetryableHttpClient()
        {
#if HAS_HTTPCLIENT
            _httpClient = new HttpClient();
#else
            DefaultRequestHeaders = new System.Collections.Generic.Dictionary<string, string>();
#endif
        }

#if HAS_HTTPCLIENT
        public HttpRequestHeaders DefaultRequestHeaders
        {
            get => _httpClient.DefaultRequestHeaders;
        }
#else
        public System.Collections.Generic.Dictionary<string, string> DefaultRequestHeaders { get; set; }
#endif

        public void Dispose()
        {
#if HAS_HTTPCLIENT
            _httpClient.Dispose();
#endif
        }

        public HttpWebRequest UpdateRequest(HttpWebRequest request)
        {
            foreach (var kv in DefaultRequestHeaders)
            {
#if HAS_HTTPCLIENT
                request.Headers.Add(kv.Key, kv.Value.First());
#else
                request.Headers.Add(kv.Key, kv.Value);
#endif
            }
            return request;
        }

        public HttpWebResponse ExecuteRequest(HttpWebRequest request)
        {
            // TODO: Handle #6001 errors with retry logic
            return (HttpWebResponse)request.GetResponse();
        }

#if HAS_HTTPCLIENT
        public Task<HttpResponseMessage> GetAsync(string requestUri)
        {
            return SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUri), HttpCompletionOption.ResponseContentRead, CancellationToken.None);
        }

        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        {
            return SendAsync(new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content }, HttpCompletionOption.ResponseContentRead, CancellationToken.None);
        }

        public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content)
        {
            return SendAsync(new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content }, HttpCompletionOption.ResponseContentRead, CancellationToken.None);
        }

        public Task<HttpResponseMessage> DeleteAsync(string requestUri)
        {
            return SendAsync(new HttpRequestMessage(HttpMethod.Delete, requestUri), HttpCompletionOption.ResponseContentRead, CancellationToken.None);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        {
            using (var memory = new MemoryStream())
            {
                byte[] bytes = null;
                if (request.Content != null)
                {
                    await request.Content.CopyToAsync(memory);
                    memory.Seek(0, SeekOrigin.Begin);

                    bytes = new byte[memory.Length];
                    await memory.ReadAsync(bytes, 0, bytes.Length);
                }

                var delay = 1000;
                do
                {
                    // Make the request retryable
                    var newContent = bytes != null ? new ByteArrayContent(bytes) : null;
                    if (newContent != null)
                    {
                        if (request.Content.Headers != null)
                        {
                            foreach (var h in request.Content.Headers)
                            {
                                newContent.Headers.Add(h.Key, h.Value);
                            }
                        }
                    }

                    var newRequest = new HttpRequestMessage
                    {
                        Content = newContent,
                        Method = request.Method,
                        RequestUri = request.RequestUri,
                        Version = request.Version
                    };
                    foreach (var h in request.Headers)
                    {
                        newRequest.Headers.Add(h.Key, h.Value);
                    }
                    foreach (var p in request.Properties)
                    {
                        newRequest.Properties.Add(p);
                    }
                    
                    HttpResponseMessage response;
                    try
                    {
                        response = await _httpClient.SendAsync(newRequest, completionOption, cancellationToken);
                    }
                    catch (AggregateException ex) when (ex.InnerExceptions.Any(x => x?.Message == "Server returned nothing (no headers, no data)"))
                    {
                        // This indicates we had a cURL exception inside the request, and that the server didn't
                        // respond with anything (even though the TCP connection was successful). Treat this as a
                        // "retry operation" scenario.
                        await Task.Delay(delay);
                        delay *= 2;
                        delay = Math.Min(30000, delay);
                        continue;
                    }
                    catch (TaskCanceledException)
                    {
                        var t = Task.Delay(delay);
                        await t;
                        delay *= 2;
                        delay = Math.Min(30000, delay);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var responseData = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var result = default(HiveMPSystemError);
                        try
                        {
                            result = JsonConvert.DeserializeObject<HiveMPSystemError>(responseData);
                        }
                        catch (Exception)
                        {
                            // Allow the handle to fail parsing this.
                            return response;
                        }

                        if (result == null)
                        {
                            // Unable to parse response, let the handle
                            // fail parsing this.
                            return response;
                        }

                        if (result.Code == 6001)
                        {
                            var t = Task.Delay(delay);
                            await t;
                            delay *= 2;
                            delay = Math.Min(30000, delay);
                            continue;
                        }
                    }

                    return response;
                }
                while (true);
            }
        }
#endif
    }
}    
