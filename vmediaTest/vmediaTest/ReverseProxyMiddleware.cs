﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;

namespace vmediaTest
{
    public class ReverseProxyMiddleware
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly RequestDelegate _nextMiddleware;
        private const string _siteAdress = "https://habr.com";
        private string _host; 

        public ReverseProxyMiddleware(RequestDelegate nextMiddleware)
        {
            _nextMiddleware = nextMiddleware;
        }

        public async Task Invoke(HttpContext context)
        {
            _host = "https://" + context.Request.Host.Value;

            var targetUri = BuildTargetUri(context.Request);
            if (targetUri != null)
            {
                var targetRequestMessage = CreateTargetMessage(context, targetUri);
                using (var responseMessage = await _httpClient.SendAsync(targetRequestMessage,
                    HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
                {
                    context.Response.StatusCode = (int)responseMessage.StatusCode;
                    CopyFromTargetResponseHeaders(context, responseMessage);
                    var memStream = await GetBodyStream(responseMessage);
                    await memStream.CopyToAsync(context.Response.Body);
                }

                return;
            }

            await _nextMiddleware(context);
        }

        private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyFromTargetResponseContentAndHeaders(context, requestMessage);
            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            return requestMessage;
        }

        private HttpMethod GetMethod(string method)
        {
           if(HttpMethods.IsDelete(method)) return HttpMethod.Delete;
           if(HttpMethods.IsGet(method)) return HttpMethod.Get;
           if(HttpMethods.IsHead(method)) return HttpMethod.Head;
           if(HttpMethods.IsOptions(method)) return HttpMethod.Options;
           if(HttpMethods.IsPost(method)) return HttpMethod.Post;
           if(HttpMethods.IsPut(method)) return HttpMethod.Trace;
           return new HttpMethod(method);
        }

        private void CopyFromTargetResponseContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;

            if (!HttpMethods.IsGet(requestMethod) && HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) && !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(context.Request.Body);
                requestMessage.Content = streamContent;
            }


            foreach (var header in context.Request.Headers)
            {
                //Mb here we need to parse
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

        }

        private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
        {
            foreach (var header in responseMessage.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in responseMessage.Content.Headers)
            {
                //Mb here we need to parse
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            context.Response.Headers.Remove("transfer-encoding");
        }


        private Uri BuildTargetUri(HttpRequest request)
        {
            return new Uri(_siteAdress + request.Path);
        }

        private async Task<MemoryStream> GetBodyStream(HttpResponseMessage response)
        {
            var memStream = new MemoryStream();
            await response.Content.CopyToAsync(memStream);
            memStream.Position = 0;
            string responseBody = new StreamReader(memStream).ReadToEnd();

            return GetChangedBody(responseBody); ;

        }

        private MemoryStream GetChangedBody(string content)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            foreach (HtmlNode node in doc.DocumentNode.SelectNodes("//body//text()[not(self::script)]"))
            {
                node.InnerHtml = Regex.Replace(node.InnerHtml, @"\b(?<word>[\w]{6})\b", "${word}™️");
            }

            foreach (HtmlNode linkNode in doc.DocumentNode.SelectNodes("//a[@href]"))
            {
                var link = linkNode.GetAttributeValue("href", string.Empty);
                if (link.StartsWith(_siteAdress))
                {
                    linkNode.SetAttributeValue("href", _host + link.Substring(_siteAdress.Length));
                }
            }

            MemoryStream outStream = new MemoryStream();
            doc.Save(outStream);
            outStream.Position = 0;
            return outStream;
        }

    }
}