using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Helpwing
{
    /// <summary>HTTP over UnityWebRequest. Must be called from the main thread, which the client always is.</summary>
    public sealed class UnityWebRequestHttp : ITransportHttp
    {
        /// <summary>Seconds before a request counts as lost. Zero waits forever.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        public Task<HttpResponseData> SendAsync(HttpRequestData request)
        {
            var completion = new TaskCompletionSource<HttpResponseData>();
            var web = new UnityWebRequest(request.Url, request.Method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (request.Body != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body)) { contentType = "application/json" };
            }
            foreach (var header in request.Headers) web.SetRequestHeader(header.Key, header.Value);

            web.SendWebRequest().completed += _ =>
            {
                try
                {
                    // No status at all is no answer at all: offline, DNS, TLS, timeout.
                    if (web.result == UnityWebRequest.Result.ConnectionError || web.responseCode == 0)
                    {
                        completion.SetException(new Exception(string.IsNullOrEmpty(web.error) ? "The network request failed." : web.error));
                    }
                    else
                    {
                        completion.SetResult(new HttpResponseData((int)web.responseCode, web.downloadHandler?.text));
                    }
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
                finally
                {
                    web.Dispose();
                }
            };
            return completion.Task;
        }
    }
}
