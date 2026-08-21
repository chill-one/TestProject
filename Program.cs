using TestProject.Services;

namespace TestProject {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            // Add services to the container.
            string homeDirectory = builder.Configuration["FileBrowser:HomeDirectory"]
                                          ?? throw new InvalidOperationException(
                                            "FileBrowser:HomeDirectory is not configured."
                                          );

            builder.Services.AddScoped<FileService>(_ =>
            {
                return new FileService(homeDirectory);
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.MapControllers();

            app.Run();
        }
    }
}