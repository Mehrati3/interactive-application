using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel.Syndication;
using System.Xml;
using System.Xml.Linq;

namespace ImageUpload
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            // Apply migrations and create the database
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var dbContext = services.GetRequiredService<ApiDbContext>();
                await dbContext.Database.MigrateAsync();
            }

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    // Specify the URL for the application to listen on
                    // webBuilder.UseUrls("http://localhost:5000");
                });

        // Define the ByteArrayTypeHandler class
        public class ByteArrayTypeHandler : SqlMapper.TypeHandler<byte[]>
        {
            public override byte[] Parse(object value)
            {
                return (byte[])value;
            }

            public override void SetValue(IDbDataParameter parameter, byte[] value)
            {
                parameter.Value = value;
            }
        }
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<ApiDbContext>(options =>
            {
                options.UseSqlite("Data Source=users.db");
            });
            services.AddScoped<ApiDbContext>();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseStaticFiles();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "index.html");
                    if (File.Exists(filePath))
                    {
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        await context.Response.WriteAsync(fileContent);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                });

                endpoints.MapPost("/login", async context =>
                {
                    var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();
                    var email = context.Request.Form["email"].ToString(); // Convert StringValues to string
                    var password = context.Request.Form["password"].ToString(); // Convert StringValues to string

                    var user = dbContext.Users.FirstOrDefault(u => u.Email == email && u.Password == password);
                    if (user != null)
                    {
                        context.Response.Redirect("/feed"); // Redirect to the feed page
                        // await context.Response.WriteAsync("<h1>Logged in successfully!</h1>");
                    }
                    else
                    {
                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "index.html");
                        var fileContent = await File.ReadAllTextAsync(filePath);
                       fileContent = fileContent.Replace("<input type=\"email\" name=\"email\" id=\"email\" placeholder=\"example@gmail.com\" required>",
                            $"<input type=\"email\" name=\"email\" id=\"email\" placeholder=\"Email or pass isn't correct.\" required>\n");
                         await context.Response.WriteAsync(fileContent);
                    }
                });

                endpoints.MapGet("/signup", async context =>
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "signup.html");
                    if (File.Exists(filePath))
                    {
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        await context.Response.WriteAsync(fileContent);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                });

                endpoints.MapPost("/register", async context =>
                {
                    var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();
                    var email = context.Request.Form["email"].ToString();
                    var password = context.Request.Form["password"].ToString();
                    var confirm = context.Request.Form["Confirm"].ToString();

                    var userExists = dbContext.Users.Any(u => u.Email == email);

                    if (userExists)
                    {
                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "signup.html");
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        fileContent = fileContent.Replace("<input type=\"email\" name=\"email\" id=\"email\" placeholder=\"example@gmail.com\" required>",
                            $"<input type=\"email\" name=\"email\" id=\"email\" placeholder=\"Email already exists.\" required>\n");
                        await context.Response.WriteAsync(fileContent);
                    }
                    else if (password.Length < 8)
                    {
                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "signup.html");
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        fileContent = fileContent.Replace("<input type=\"password\" name=\"password\" id=\"password\" placeholder=\"Password\" required>",
                            $"<input type=\"password\" name=\"password\" id=\"password\" placeholder=\"Pass is less than 8 letters.\" required>\n");
                        await context.Response.WriteAsync(fileContent);
                    }
                    else if (password != confirm)
                    {
                        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "signup.html");
                        var fileContent = await File.ReadAllTextAsync(filePath);
                        fileContent = fileContent.Replace("<input type=\"password\" name=\"Confirm\" id=\"Confirm\" placeholder=\"Password\" required>",
                            $"<input type=\"password\" name=\"Confirm\" id=\"Confirm\" placeholder=\"Password doesn't match.\" required>\n");
                        await context.Response.WriteAsync(fileContent);
                    }
                    else
                    {
                        dbContext.Users.Add(new User { Email = email, Password = password });
                        await dbContext.SaveChangesAsync();
                        context.Response.Redirect("/"); // Redirect to the login page
                    }
                });

                endpoints.MapGet("/feed", async context =>
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "feed.html");
                    if (File.Exists(filePath))
                    {
                        var fileContent = await File.ReadAllTextAsync(filePath);

                        // Retrieve the RSS/ATOM feeds from the database
                        var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();
                        var feeds = await dbContext.Feeds.ToListAsync();

                        // Render the feeds in the HTML
                        var feedOptions = new StringBuilder();
                        foreach (var feed in feeds)
                        {
                            feedOptions.AppendLine($"<option value=\"{feed.Id}\">{feed.Url}</option>");
                        }
                        fileContent = fileContent.Replace("<option disabled hidden selected>Select a feed to remove</option>",
                            feedOptions.ToString());

                        await context.Response.WriteAsync(fileContent);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                });

                endpoints.MapPost("/feed", async context =>
                {
                    var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();

                    
                    // Add a new RSS/ATOM feed to the database
                    if (context.Request.Form.ContainsKey("add-url"))
                    {
                        var addURL = context.Request.Form["add-url"].ToString();

                        // Check if the URL already exists in the database
                        var existingFeed = await dbContext.Feeds.FirstOrDefaultAsync(f => f.Url == addURL);
                        if (existingFeed != null)
                        {
                            await context.Response.WriteAsync("That URL already exists.");
                            return;
                        }

                        // Apply the condition to validate the URL
                        if (!await IsValidRssFeed(addURL))
                        {
                            await context.Response.WriteAsync("Invalid URL.");
                            return;
                        }

                        dbContext.Feeds.Add(new Feed { Url = addURL });
                        await dbContext.SaveChangesAsync();
                    }

                    var feeds = await dbContext.Feeds.ToListAsync();
                    var feedOptions = new StringBuilder();
                    foreach (var feed in feeds)
                    {
                        feedOptions.AppendLine($"<option value=\"{feed.Id}\">{feed.Url}</option>");
                    }
                                    
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "feed.html");
                    var fileContent = await File.ReadAllTextAsync(filePath);
                    
                    var feedItems = await GetFeedItems(feeds);
                    var feedItemsHtml = GenerateFeedItemsHtml(feedItems);

                    // Replace the placeholder in the HTML with the updated feed options
                    fileContent = fileContent.Replace("<option disabled hidden selected>Select a feed to remove</option>", feedOptions.ToString());
                    await context.Response.WriteAsync(fileContent);
                    
                    if (feedItemsHtml != null) // Check if feedItemsHtml is not null before replacing the placeholder
                    {
                        await context.Response.WriteAsync(feedItemsHtml);
                    }
                    else
                    {
                        // Handle the case when feedItemsHtml is null (e.g., display an error message)
                        fileContent = fileContent.Replace("<!--FEED_ITEMS-->", "<p>Error retrieving feed items.</p>");
                    }
                    
                });

                endpoints.MapPost("/remove-feed", async context =>
                {
                    var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();

                    if (context.Request.Form.ContainsKey("remove"))
                    {
                        Console.Write("dakhal");
                        var feedUrl = context.Request.Form["remove"].ToString();
                        var feed = await dbContext.Feeds.FirstOrDefaultAsync(f => f.Url == feedUrl);
                        if (feed != null)
                        {
                            dbContext.Feeds.Remove(feed);
                            await dbContext.SaveChangesAsync();
                        }
                    }

                    var feeds = await dbContext.Feeds.ToListAsync();
                    var feedOptions = new StringBuilder();
                    foreach (var feed in feeds)
                    {
                        feedOptions.AppendLine($"<option value=\"{feed.Url}\">{feed.Url}</option>");
                    }

                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "feed.html");
                    var fileContent = await File.ReadAllTextAsync(filePath);
                    var feedItems = await GetFeedItems(feeds);
                    var feedItemsHtml = GenerateFeedItemsHtml(feedItems);

                    // Replace the placeholder in the HTML with the updated feed options
                    fileContent = fileContent.Replace("<!-- Options will be dynamically added by JavaScript -->", feedOptions.ToString());
                    await context.Response.WriteAsync(fileContent);
                     if (feedItemsHtml != null) // Check if feedItemsHtml is not null before replacing the placeholder
                    {
                        await context.Response.WriteAsync(feedItemsHtml);
                    }
                    else
                    {
                        // Handle the case when feedItemsHtml is null (e.g., display an error message)
                        fileContent = fileContent.Replace("<!--FEED_ITEMS-->", "<p>Error retrieving feed items.</p>");
                    }
                });
                async Task<bool> IsValidRssFeed(string url)
                {
                    try
                    {
                       using var client = new HttpClient();
                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                        
                        response.EnsureSuccessStatusCode();

                        var content = await response.Content.ReadAsStringAsync();
                        using var reader = XmlReader.Create(new StringReader(content));
                        reader.MoveToContent();

                        return reader.Name == "rss" || reader.Name == "feed";
                    }
                    catch (InvalidOperationException)
                    {
                        // Invalid request URI provided
                        return false;
                    }
                    catch (HttpRequestException)
                    {
                        return false;
                    }
                    catch (XmlException)
                    {
                        return false;
                    }
                }

                async Task<List<SyndicationItem>> GetFeedItems(List<Feed> feeds)
                {
                    var feedItems = new List<SyndicationItem>();

                    foreach (var feed in feeds)
                    {
                        try
                        {
                            using var reader = XmlReader.Create(feed.Url);
                            var syndicationFeed = SyndicationFeed.Load(reader);
                            feedItems.AddRange(syndicationFeed.Items.OfType<SyndicationItem>());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to fetch feed from URL: {feed.Url}. Error: {ex.Message}");
                            // You can handle the error as per your requirement, e.g., log it, skip the feed, etc.
                        }
                    }

                    return feedItems;
                }

                // Helper method to generate HTML for the feed items
                string GenerateFeedItemsHtml(List<SyndicationItem> feedItems)
                {
                    if (feedItems == null)
                    {
                        return string.Empty;
                    }

                    var html = new StringBuilder();

                    foreach (var item in feedItems)
                    {
                        html.AppendLine("<div class=\"wrapper\">");
                        html.AppendLine("<form class=\"p-3 mt-3\">");
                        html.AppendLine($"<h1 class=\"mt-5 name\"><a href=\"{item.Links.FirstOrDefault()?.Uri}\">{item.Title?.Text}></h1>");
                        html.AppendLine($"<p class=\"mt-5 name\">{item.Summary?.Text}</p>");
                        html.AppendLine("</form>");
                        html.AppendLine("</div>");
                        
                    }

                    return html.ToString();
                }
                // endpoints.MapPost("/remove-feed", async context =>
                // {
                //     var dbContext = context.RequestServices.GetRequiredService<ApiDbContext>();

                //     if (context.Request.Form.ContainsKey("remove"))
                //     {
                //         var feedUrl = context.Request.Form["remove"].ToString();
                //         var feed = await dbContext.Feeds.FirstOrDefaultAsync(f => f.Url == feedUrl);
                //         if (feed != null)
                //         {
                //             Console.Write("hhhhhhhhhhhhhhhhhhhhhhhhhhhhh");
                //             dbContext.Feeds.Remove(feed);
                //             await dbContext.SaveChangesAsync();
                //             Console.Write("el mafrod removed");
                //         }
                //     }

                //     var feeds = await dbContext.Feeds.ToListAsync();
                //     var selectOptions = feeds.Select(f => $"<option value=\"{f.Url}\">{f.Url}</option>");
                //     var selectHtml = "<select name=\"remove\" id=\"remove\">" +
                //                     "<option disabled hidden selected>Select a feed to remove</option>" +
                //                     string.Join("", selectOptions) +
                //                     "</select>";

                //     var filePath = Path.Combine(Directory.GetCurrentDirectory(), "feed.html");
                //     var fileContent = await File.ReadAllTextAsync(filePath);
                //     fileContent = fileContent.Replace("<select name=\"remove\" id=\"remove\">" +
                //                                     "<option disabled hidden selected>Select a feed to remove</option>" +
                //                                     "</select>",
                //                                     selectHtml);
                //     await context.Response.WriteAsync(fileContent);
                // });

                // async Task<bool> IsValidRssFeed(string url)
                // {
                //     try
                //     {
                //         using HttpClient client = new HttpClient();
                //         HttpResponseMessage response = await client.GetAsync(url);
                //         response.EnsureSuccessStatusCode();

                //         string content = await response.Content.ReadAsStringAsync();
                //         using XmlReader reader = XmlReader.Create(new System.IO.StringReader(content));
                //         reader.MoveToContent();

                //         return reader.Name == "rss" || reader.Name == "feed";
                //     }
                //     catch (HttpRequestException e)
                //     {
                //         return false;
                //     }
                //     catch (XmlException e)
                //     {
                //         return false;
                //     }
                // }
                endpoints.MapPost("/logout", async context =>
                {
                    context.Response.Redirect("/"); // Redirect to the login page
                });
            });
        }
    }

    public class User
    {
        public int Id { get; set; }
        public string? Email { get; set; } // Nullable property
        public string? Password { get; set; } // Nullable property
    }
    public class Feed
    {
        public int Id { get; set; }
        public string Url { get; set; }
    }
    

    public class ApiDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=users.db");
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Feed> Feeds { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Feed>(entity =>
            {
                entity.ToTable("Feeds");
                entity.HasKey(e => e.Id);
            });
        }
        public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options) { 
            // Database.ExecuteSqlRaw("DROP TABLE IF EXISTS Users;");
        } // Add constructor

    }
}