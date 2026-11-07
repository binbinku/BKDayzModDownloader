using System.Windows;
using System.Windows.Controls;

namespace BKDayzModDownloader;

internal sealed class AuthDialog : Window
{
    private readonly PasswordBox input = new() { Margin = new(0, 14, 0, 18) };
    public string Value => input.Password;
    public AuthDialog(string message, Window owner)
    {
        Owner = owner; Title = "Steam 身份验证";
        SetResourceReference(BackgroundProperty, "Surface");
        SetResourceReference(ForegroundProperty, "Ink");
        Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(26) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        panel.Children.Add(input);
        var button = new Button { Content = "提交", IsDefault = true };
        button.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Password)) DialogResult = true; };
        panel.Children.Add(button);
        Content = panel;
        Loaded += (_, _) => input.Focus();
    }
}
