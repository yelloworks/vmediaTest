using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProxyKit;

namespace vmediaTest
{
    public class Startup
    {
        private bool noNugetProxy = true;

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddProxy(p => p.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip
            }));
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }



            if (noNugetProxy)
            {
                app.UseMiddleware<ReverseProxyMiddleware>();
            }
            else
            {
                app.UseHttpsRedirection();
                const string host = "https://habr.com";

                app.RunProxy(async context =>
                {
                    var response = await context.ForwardTo(host).AddXForwardedHeaders().Send();
                    if (response.Content.Headers.ContentType?.MediaType != "text/html")
                        return response;

                    using (var body = await response.Content.ReadAsStreamAsync())
                    {
                        var doc = new HtmlDocument();
                        doc.Load(body);

                        var textNodes = doc.DocumentNode.SelectNodes("//text()");
                        if (textNodes != null)
                        {
                            foreach (HtmlNode node in textNodes)
                            {
                                node.InnerHtml = Regex.Replace(node.InnerHtml, @"\b(?<word>[\w]{6})\b", "${word}™️");
                            }
                        }
                            

                        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");
                        if (linkNodes != null)
                            foreach (HtmlNode linkNode in linkNodes)
                            {
                                var link = linkNode.GetAttributeValue("href", string.Empty);
                                if (link.StartsWith(host))
                                {
                                    linkNode.SetAttributeValue("href", "https://" + context.Request.Host.Value + link.Substring(host.Length));
                                }
                            }

                        using (var sw = new StringWriter())
                        {
                            doc.Save(sw);
                            response.Content = new StringContent(sw.ToString(),
                                Encoding.UTF8,
                                response.Content.Headers.ContentType.MediaType);


                            return response;
                        }
                    }
                });
            }


        }
    }
}
