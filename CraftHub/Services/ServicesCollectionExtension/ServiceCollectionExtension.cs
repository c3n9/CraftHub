using CraftHub.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Services.ServicesCollectionExtension
{
    public static class ServiceCollectionExtension
    {
        public static void AddCommonServices(this IServiceCollection services)
        {
            services.AddSingleton<NotificationService>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IJsonService, JsonService>();
            services.AddSingleton<IClassParserService, ClassParserService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ThemeService>();
        }
    }
}
