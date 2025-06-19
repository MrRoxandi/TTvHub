using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using TTvHub.Core.Managers;
using TTvHub.Core.Services.Controllers;

namespace TTvHub
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();
#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(_ => Task.Run(LuaStartUpManager.CreateAsync).GetAwaiter().GetResult());
            builder.Services.AddSingleton<TwitchController>();
            return builder.Build();
        }
    }
}
