using CraftHub.ViewModels;
using CraftHub.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Services.ServicesCollectionExtension
{
    public static class ViewsAndViewModelsCollectionExtension
    {
        public static void AddViewModels(this IServiceCollection services)
        {
            services.AddSingleton<MainWindowViewModel>();
            services.AddTransient<WorkspaceViewModel>();
        }

        public static void AddViews(this IServiceCollection services)
        {
            services.AddSingleton<MainWindow>();
        }
    }
}
