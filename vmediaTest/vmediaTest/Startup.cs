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
        private bool noNugetProxy = false;

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

                const string habrAddress = "https://habr.com";
                app.RunProxy(async context =>
                {
                    var response = await context.ForwardTo(habrAddress).AddXForwardedHeaders().Send();
                    if (response.Content.Headers.ContentType?.MediaType != "text/html")
                        return response;

                    using (var body = await response.Content.ReadAsStreamAsync())
                    {
                        var htmlDoc = new HtmlDocument();
                        htmlDoc.Load(body);

                        var textNodes = htmlDoc.DocumentNode.SelectNodes("//text()");
                        if (textNodes != null)
                            foreach (HtmlNode node in textNodes)
                                node.InnerHtml = Regex.Replace(node.InnerHtml, @"\b(?<word>[\w]{6})\b", "${word}™️");

                        var linkNodes = htmlDoc.DocumentNode.SelectNodes("//a[@href]");
                        if (linkNodes != null)
                            foreach (HtmlNode linkNode in linkNodes)
                            {
                                var link = linkNode.GetAttributeValue("href", string.Empty);
                                if (link.StartsWith(habrAddress))
                                {
                                    linkNode.SetAttributeValue("href", "https://" + context.Request.Host.Value + link.Substring(habrAddress.Length));
                                }
                            }

                        using (var sw = new StringWriter())
                        {
                            htmlDoc.Save(sw);
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
