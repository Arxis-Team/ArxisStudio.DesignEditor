using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DesignEditor.Demo.ViewModels;
using DesignEditor.Demo.Views;

namespace DesignEditor.Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // В Avalonia 12 валидация через DataAnnotations стала opt-in
            // (AppBuilder.WithDataAnnotationsValidation), поэтому ручное снятие
            // DataAnnotationsValidationPlugin больше не требуется — дублирования
            // с валидацией CommunityToolkit нет по умолчанию.
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
